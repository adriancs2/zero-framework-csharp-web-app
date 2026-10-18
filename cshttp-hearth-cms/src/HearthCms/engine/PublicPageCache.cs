using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    public class PageCache
    {
        public string Path;
        public string Body;
        public string ContentType = "text/html; charset=utf-8";
        public DateTime CreatedUtc = DateTime.UtcNow;

        // Last time this entry was served from the RAM cache (or first stored).
        // Drives LRU eviction: the least-recently-accessed entries go first when
        // the RAM cache exceeds its configured byte budget.
        public DateTime LastAccessUtc = DateTime.UtcNow;

        // Approximate in-memory size of this entry's body, in bytes. Computed
        // once at store time (UTF-16 chars -> 2 bytes each) and used to keep a
        // running total without re-measuring every entry on each insert.
        public long SizeBytes;
    }

    // ============================================================
    // PublicPageCache - two-level lazy cache for public pages.
    //
    //   L1 (RAM):  ConcurrentDictionary<path, PageCache>   (this class)
    //   L2 (file): delegated to FileCacheEngine             (disk)
    //
    // Eligibility is decided per-request: only public read paths,
    // GET requests, no query string. Login state is NOT a factor -
    // public pages render identical markup for every visitor, so the
    // cache is shared by anonymous and logged-in users alike.
    //
    // Read path:   RAM -> file (lifted into RAM) -> miss
    // Write path:  RAM (sync) + file (background fire-and-forget)
    //
    // RAM budget / eviction:
    //   The RAM tier is bounded by the `cache_ram_max_mb` setting
    //   (default 250 MB; 0 means UNLIMITED - no eviction). A running
    //   byte total is maintained; when a store pushes the total over the
    //   budget, the least-recently ACCESSED entries (smallest
    //   LastAccessUtc) are evicted until the total is back under budget.
    //   Every cache hit refreshes the entry's LastAccessUtc, so hot pages
    //   survive and cold pages fall out first (LRU). The file tier is
    //   unbounded (disk is cheap).
    //
    // Invalidation has two flavours:
    //   InvalidateSlug(slug) - targeted: clears only the root ("/" and
    //       "/home") plus the given slug. Used for routine content edits
    //       (a single page/post changing) so the rest of the file cache
    //       survives. NOT a nuke.
    //   InvalidateAll()      - full wipe of both tiers. Used for site-wide
    //       changes (settings, theme) that bake into every page.
    //
    // Toggleable via two settings (read live, no restart needed):
    //   cache_ram_enabled  ("1" or "0")
    //   cache_file_enabled ("1" or "0")
    //   cache_ram_max_mb   (integer MB; default 250)
    // ============================================================
    public static class PublicPageCache
    {
        public static readonly ConcurrentDictionary<string, PageCache> Cache =
            new ConcurrentDictionary<string, PageCache>();

        // Running total of the approximate bytes held in the RAM tier. Kept in
        // sync with Cache via Interlocked on every add/remove. Reads of this are
        // advisory (eviction re-checks under a lock), so no torn-read concerns.
        static long _ramBytes = 0;

        // Serializes the trim-to-budget step. Reads/writes of the dictionary stay
        // lock-free; only eviction (which must scan + remove a consistent set) is
        // guarded so two concurrent stores can't both over-evict.
        static readonly object _evictLock = new object();

        // Clamp bounds for the configured budget (MB).
        const int MinRamMb = 5;
        const int MaxRamMb = 4096;
        const int DefaultRamMb = 250;

        // ===== Settings (live; read on every check) =====
        public static bool RamEnabled
        {
            get { return Settings.CacheRamEnabled; }
        }
        public static bool FileEnabled
        {
            get { return Settings.CacheFileEnabled; }
        }
        public static bool AnyEnabled { get { return RamEnabled || FileEnabled; } }

        // Sentinel: a budget of 0 bytes means "unlimited" - never evict, let the
        // RAM cache grow as large as the site needs (operator has guaranteed the
        // server has the memory). Any positive value is clamped to a sane range.
        public const long Unlimited = 0L;

        // Configured RAM budget in bytes. 0 == unlimited (no eviction). A positive
        // value is clamped to [MinRamMb, MaxRamMb]. A malformed/missing setting
        // falls back to the default budget.
        public static long RamMaxBytes
        {
            get
            {
                int mb = Settings.CacheRamMaxMb;
                if (mb == 0) return Unlimited;          // explicit "no limit"
                if (mb < MinRamMb) mb = MinRamMb;
                if (mb > MaxRamMb) mb = MaxRamMb;
                return (long)mb * 1024L * 1024L;
            }
        }

        // Current approximate RAM usage in bytes (advisory snapshot).
        public static long RamBytesUsed { get { return Interlocked.Read(ref _ramBytes); } }

        // ===== Eligibility =====
        public static bool IsCacheablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path == "/" || path == "/home") return true;
            // Pages and posts are served prefix-less at the root ("/{slug}").
            // Any single-segment path that isn't a reserved app route is a
            // content candidate and therefore cacheable. Multi-segment paths
            // (e.g. "/category/x", "/admin/...") are handled by their own rules
            // and are not cached here.
            if (IsRootContentPath(path)) return true;
            return false;
        }

        // Single path segment that is not a reserved top-level route. Mirrors
        // Global.IsRootSlugCandidate so cache eligibility tracks routing.
        static bool IsRootContentPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/' || path == "/") return false;
            if (path.IndexOf('/', 1) != -1) return false;

            switch (path.Substring(1))
            {
                case "home":
                case "latest-post":
                case "categories-latest-post":
                case "login":
                case "logout":
                case "api":
                case "admin":
                case "category":
                case "favicon.ico":
                case "robots.txt":
                case "sitemap.xml":
                    return false;
            }
            return true;
        }

        // ===== Vary-by-query escape hatch =====
        // Public content routes are a PURE FUNCTION OF THEIR PATH: handlers never
        // read Request.QueryString to change their output. That is what lets the
        // cache IGNORE the query string entirely and serve every "?utm=...",
        // "?fbclid=...", "?gclid=..." variant from the single path-keyed entry
        // (the key is Request.Path, which already excludes the query string).
        // This both eliminates the cache bypass on hotlinked/social/search traffic
        // AND prevents cache-key explosion from junk query parameters.
        //
        // INVARIANT: any path eligible for this cache MUST render identically
        // regardless of its query string. If a future route genuinely varies its
        // output by query string (e.g. an on-site search, query-string paging),
        // register its EXACT path here so it is never served a path-only cached
        // copy. Such a route bypasses the cache (renders fresh every time).
        // Compared case-insensitively against the normalized path ("/search").
        static readonly HashSet<string> VaryByQueryPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // (empty today — no public route varies by query string)
                // e.g. "/search",
            };

        // GET + cacheable path + at least one tier enabled. The query string is
        // intentionally IGNORED (see VaryByQueryPaths above) UNLESS this exact
        // path is registered as vary-by-query, in which case a request carrying a
        // query string bypasses the cache.
        // NOTE: login state is intentionally NOT checked. The public templates
        // emit no per-user markup, so the rendered HTML is identical for every
        // visitor and can be safely shared between anonymous and logged-in users.
        public static bool IsEligibleRequest(string path)
        {
            if (!AnyEnabled) return false;

            var ctx = HttpContext.Current;
            if (ctx == null) return false;
            if (!string.Equals(ctx.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)) return false;
            if (!IsCacheablePath(path)) return false;

            // Vary-by-query routes are not safe to serve path-only: only bypass
            // them when a query string is actually present (the bare path is still
            // cacheable). Everything else ignores the query string entirely.
            if (ctx.Request.QueryString.Count > 0 && VaryByQueryPaths.Contains(path))
                return false;

            return true;
        }

        // ===== Read =====
        // Returns true if served from cache (caller should stop processing).
        public static bool TryServe(string path)
        {
            if (!IsEligibleRequest(path)) return false;

            PageCache hit = null;

            // L1 (RAM)
            if (RamEnabled)
            {
                Cache.TryGetValue(path, out hit);
            }

            // L2 (file) fall-back; lift hits into RAM so the next request is RAM-fast.
            if (hit == null && FileEnabled)
            {
                hit = FileCacheEngine.Load(path);
                if (hit != null && RamEnabled)
                {
                    StoreRam(path, hit);   // accounts size + may evict
                }
            }

            if (hit == null) return false;

            // Refresh recency on every hit so LRU eviction keeps hot pages.
            hit.LastAccessUtc = DateTime.UtcNow;

            var ctx = HttpContext.Current;
            ctx.Response.ContentType = hit.ContentType;
            ctx.Response.AddHeader("X-Cache", "HIT");
            ctx.Response.Write(hit.Body);
            ApiHelper.EndResponse();
            return true;
        }

        // Normalize the current request's path the same way the router does.
        public static string CurrentPath()
        {
            var ctx = HttpContext.Current;
            if (ctx == null) return "/";
            string p = (ctx.Request.Path ?? "").ToLowerInvariant().TrimEnd('/');
            return string.IsNullOrEmpty(p) ? "/" : p;
        }

        // ===== Write =====
        // Convenience: derive the path from the current request.
        public static void WriteAndCache(string html, string contentType = "text/html; charset=utf-8")
        {
            WriteAndCache(CurrentPath(), html, contentType);
        }

        // Replaces ApiHelper.WriteHtml + EndResponse - does both AND, if eligible,
        // caches the rendered HTML: RAM synchronously, file in the background.
        public static void WriteAndCache(string path, string html, string contentType = "text/html; charset=utf-8")
        {
            var ctx = HttpContext.Current;

            bool eligible = IsEligibleRequest(path);
            if (eligible)
            {
                var entry = new PageCache
                {
                    Path = path,
                    Body = html,
                    ContentType = contentType,
                    CreatedUtc = DateTime.UtcNow,
                    LastAccessUtc = DateTime.UtcNow
                };
                // Feed RAM cache (immediate, size-accounted), then hand the file
                // write off to the background engine (fire-and-forget per slug).
                if (RamEnabled) StoreRam(path, entry);
                if (FileEnabled) FileCacheEngine.EnqueueWrite(path, entry);
                if (ctx != null) ctx.Response.AddHeader("X-Cache", "MISS-STORED");
            }
            else if (ctx != null)
            {
                ctx.Response.AddHeader("X-Cache", "BYPASS");
            }

            if (ctx != null)
            {
                ctx.Response.ContentType = contentType;
                ctx.Response.Write(html);
            }
            ApiHelper.EndResponse();
        }

        // ===== RAM store + accounting =====
        // Inserts (or replaces) an entry in the RAM tier, keeps the running byte
        // total in sync, and trims to the configured budget afterwards. Replacing
        // an existing key first reclaims that key's old size.
        static void StoreRam(string path, PageCache entry)
        {
            entry.SizeBytes = ApproxSize(entry);

            // Atomically remove any existing entry so exactly ONE thread reclaims
            // its size, even when two stores race on the same key (miss stampede).
            // TryGetValue + overwrite would let both racers subtract the same
            // prev.SizeBytes, drifting _ramBytes low and breaking the budget.
            // The brief window where the key is absent is harmless: a concurrent
            // reader just misses and re-renders once.
            PageCache prev;
            if (Cache.TryRemove(path, out prev) && prev != null)
                Interlocked.Add(ref _ramBytes, -prev.SizeBytes);

            Cache[path] = entry;
            Interlocked.Add(ref _ramBytes, entry.SizeBytes);

            TrimToBudget();
        }

        // Calculates a highly accurate approximation of the entry's in-memory footprint on a 64-bit CLR.
        // Accounts for string character data (UTF-16 chars at 2 bytes each), string object headers,
        // the PageCache instance size, and the ConcurrentDictionary bucket node overhead.
        static long ApproxSize(PageCache e)
        {
            if (e == null) return 0;

            // 1. PageCache object instance itself on 64-bit CLR: 64 bytes
            long size = 64;
            
            // 2. ConcurrentDictionary internal node overhead: ~48 bytes
            size += 48;
            
            // 3. Measure key string (stored in dictionary) and properties
            size += GetStringMemorySize(e.Path);        // Dictionary key + Path property (assumes shared reference)
            size += GetStringMemorySize(e.Body);        // HTML Body
            size += GetStringMemorySize(e.ContentType); // ContentType string

            return size;
        }

        // Helper for calculating exact C# string memory layout on 64-bit systems
        // Align(22 + 2 * Length, 8)
        static long GetStringMemorySize(string s)
        {
            if (s == null) return 0;
            long bytes = 22 + ((long)s.Length * 2);
            // Align to 8-byte boundary
            return (bytes + 7) & ~7;
        }

        // Evicts least-recently-ACCESSED entries until the RAM total is within
        // budget. Guarded by _evictLock so concurrent stores don't double-trim.
        // Reads of the dictionary elsewhere stay lock-free.
        static void TrimToBudget()
        {
            long budget = RamMaxBytes;
            if (budget == Unlimited) return;            // 0 == no limit, never evict
            if (Interlocked.Read(ref _ramBytes) <= budget) return;

            lock (_evictLock)
            {
                if (Interlocked.Read(ref _ramBytes) <= budget) return;

                // Snapshot, order by oldest access first, and remove until under
                // budget. We tolerate the snapshot being slightly stale: any key
                // already gone by the time we try to remove it is simply skipped.
                var snapshot = new List<KeyValuePair<string, PageCache>>(Cache);
                snapshot.Sort(delegate (KeyValuePair<string, PageCache> a, KeyValuePair<string, PageCache> b)
                {
                    return a.Value.LastAccessUtc.CompareTo(b.Value.LastAccessUtc);
                });

                foreach (var kv in snapshot)
                {
                    if (Interlocked.Read(ref _ramBytes) <= budget) break;
                    PageCache removed;
                    if (Cache.TryRemove(kv.Key, out removed) && removed != null)
                        Interlocked.Add(ref _ramBytes, -removed.SizeBytes);
                }
            }
        }

        // ===== Invalidate (targeted) =====
        // Clears only the homepage ("/" + "/home") and the supplied slug from
        // BOTH tiers. Everything else in the cache survives. Use for routine
        // single-page/single-post content changes.
        //
        // `fullPath` may be passed as a full root route ("/foo") or built from a
        // prefix + bare slug via the overload below.
        public static void InvalidateSlug(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) { InvalidateRootOnly(); return; }

            string path = fullPath.ToLowerInvariant().TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";

            RemoveOne("/");
            RemoveOne("/home");
            RemoveOne(path);

            // A page/post was added, edited, or removed: the sitemap may have
            // gained/lost a URL, so drop its cache too (rebuilds on next request).
            SitemapGenerator.Invalidate();
        }

        // Convenience overload: build the route from a prefix + bare slug.
        //   InvalidateSlug("/", "my-post")  -> clears "/", "/home", "/my-post"
        public static void InvalidateSlug(string routePrefix, string slug)
        {
            string full = (routePrefix ?? "") + (slug ?? "");
            InvalidateSlug(full);
        }

        static void InvalidateRootOnly()
        {
            RemoveOne("/");
            RemoveOne("/home");
        }

        static void RemoveOne(string path)
        {
            PageCache removed;
            if (Cache.TryRemove(path, out removed) && removed != null)   // L1
                Interlocked.Add(ref _ramBytes, -removed.SizeBytes);
            FileCacheEngine.Delete(path);                                 // L2
        }

        // ===== Invalidate (full wipe) =====
        // Clears both tiers entirely. Use for site-wide changes (settings,
        // theme activation/edit/delete) that are baked into every page.
        public static void InvalidateAll()
        {
            Cache.Clear();
            Interlocked.Exchange(ref _ramBytes, 0);
            FileCacheEngine.Clear();

            // Site-wide change (settings, theme, bulk content): the sitemap could
            // be affected, so drop its cache too (rebuilds on next request).
            SitemapGenerator.Invalidate();
        }

        // Clears ONLY the RAM tier (and resets its byte accounting). Used when the
        // RAM cache is switched off so freed memory is reclaimed immediately while
        // the file tier (if enabled) is left intact.
        public static void ClearRam()
        {
            Cache.Clear();
            Interlocked.Exchange(ref _ramBytes, 0);
        }

        // ===== Markdown source cache (for use of CsTemplate type of Themes to show markdown to the frontpage "View Markdown") =====
        // The article-markdown endpoint can't ride the normal page cache (it has a
        // query string and a /api path, both ineligible), so it caches under a
        // NON-path key that can't collide with page entries. RAM tier only, and
        // gated on cache_ram_enabled (there is no file tier for these).
        public const string MarkdownKeyPrefix = "[##MARKDOWN##]";

        public static string MarkdownKey(int postId) { return MarkdownKeyPrefix + postId; }

        // Cached markdown for postId, or null on miss / when the RAM cache is off.
        public static string TryGetMarkdown(int postId)
        {
            if (!RamEnabled) return null;
            PageCache hit;
            if (Cache.TryGetValue(MarkdownKey(postId), out hit) && hit != null)
            {
                hit.LastAccessUtc = DateTime.UtcNow;   // keep hot entries (LRU)
                return hit.Body;
            }
            return null;
        }

        // Stores a post's markdown in the RAM tier (size-accounted, LRU-evictable).
        // No-op when the RAM cache is disabled.
        public static void StoreMarkdown(int postId, string markdown)
        {
            if (!RamEnabled) return;
            string key = MarkdownKey(postId);
            StoreRam(key, new PageCache
            {
                Path = key,
                Body = markdown ?? "",
                ContentType = "text/plain; charset=utf-8",
                CreatedUtc = DateTime.UtcNow,
                LastAccessUtc = DateTime.UtcNow
            });
        }

        // Drops a single post's cached markdown from the RAM tier (no file tier).
        // Call on post edit/delete to avoid serving stale markdown.
        public static void InvalidateMarkdown(int postId)
        {
            PageCache removed;
            if (Cache.TryRemove(MarkdownKey(postId), out removed) && removed != null)
                Interlocked.Add(ref _ramBytes, -removed.SizeBytes);
        }
    }
}

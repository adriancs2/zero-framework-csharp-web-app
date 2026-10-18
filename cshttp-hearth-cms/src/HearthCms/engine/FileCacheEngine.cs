using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // FileCacheEngine - standalone L2 (on-disk) page-cache engine.
    //
    // Owns everything about the disk tier and NOTHING about HTTP
    // eligibility or the RAM tier. PublicPageCache orchestrates the
    // two tiers; this engine just persists rendered HTML to:
    //
    //     App_Data/page_cache/{sha1(path)}.html
    //
    // Write path is fire-and-forget with per-slug de-duplication:
    // concurrent write requests for the SAME slug collapse to one
    // background write. Different slugs run independently. The HTTP
    // response is never blocked on disk I/O.
    //
    // Background work is queued via HostingEnvironment so IIS delays
    // app-pool shutdown until in-flight writes drain (best-effort).
    // ============================================================
    public static class FileCacheEngine
    {
        // Registry of in-flight background writes, keyed by sha1(path).
        // Presence of a key == "a write for this slug is already scheduled".
        static readonly ConcurrentDictionary<string, byte> _pending =
            new ConcurrentDictionary<string, byte>();

        // ===== Folder management =====
        // MUST be read on the request thread - uses HttpContext, which is
        // null on background threads.
        public static string Folder
        {
            get
            {
                if (HttpContext.Current == null) return null;
                string p = HttpContext.Current.Server.MapPath("~/App_Data/page_cache/");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        // ===== Read =====
        public static PageCache Load(string path)
        {
            string folder = Folder;
            if (folder == null) return null;
            string file = System.IO.Path.Combine(folder, KeyFor(path) + ".html");
            try
            {
                if (!File.Exists(file)) return null;
                string body = File.ReadAllText(file, Encoding.UTF8);
                return new PageCache
                {
                    Path = path,
                    Body = body,
                    ContentType = "text/html; charset=utf-8",
                    CreatedUtc = File.GetLastWriteTimeUtc(file)
                };
            }
            catch { return null; }
        }

        // ===== Write (fire-and-forget, de-duplicated) =====
        // Schedules a background disk write for this slug. If a write for the
        // same slug is already in flight, does nothing and returns. Never
        // blocks the caller on disk I/O.
        public static void EnqueueWrite(string path, PageCache entry)
        {
            if (entry == null) return;

            string key = KeyFor(path);

            // "Is there already a task registered writing this slug?"
            // TryAdd is atomic & lock-free: fails iff the key is already present.
            if (!_pending.TryAdd(key, 0)) return;

            // Snapshot everything the background thread needs NOW, on the
            // request thread - HttpContext (and thus Folder) is unavailable
            // once we're off-thread.
            string folder = Folder;
            string body = entry.Body;

            if (folder == null)
            {
                // Couldn't resolve the folder (no HttpContext) - deregister and bail.
                byte ignore;
                _pending.TryRemove(key, out ignore);
                return;
            }

            HostingEnvironment.QueueBackgroundWorkItem(_ =>
            {
                try { WriteToDisk(folder, key, body); }
                catch { /* best-effort cache - swallow IO errors */ }
                finally
                {
                    byte ignore;
                    _pending.TryRemove(key, out ignore);  // deregister
                }
            });
        }

        // Synchronous write - exposed for callers that explicitly want to block
        // (not used on the hot path).
        public static void Save(string path, PageCache entry)
        {
            if (entry == null) return;
            string folder = Folder;
            if (folder == null) return;
            try { WriteToDisk(folder, KeyFor(path), entry.Body); }
            catch { /* best-effort */ }
        }

        static void WriteToDisk(string folder, string key, string body)
        {
            string file = System.IO.Path.Combine(folder, key + ".html");
            File.WriteAllText(file, body ?? "", Encoding.UTF8);
        }

        // ===== Delete =====
        // Remove a single slug's cached file.
        public static void Delete(string path)
        {
            string folder = Folder;
            if (folder == null) return;
            string file = System.IO.Path.Combine(folder, KeyFor(path) + ".html");
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* swallow */ }
        }

        // Wipe the entire file cache.
        public static void Clear()
        {
            try
            {
                string folder = Folder;
                if (folder != null && Directory.Exists(folder))
                {
                    foreach (string f in Directory.GetFiles(folder))
                    {
                        try { File.Delete(f); } catch { /* per-file */ }
                    }
                }
            }
            catch { /* folder-level */ }
        }

        // ===== Key derivation =====
        static string KeyFor(string path)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
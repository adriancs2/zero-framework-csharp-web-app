using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // SitemapGenerator - search-engine sitemap + robots.txt
    //
    // Strategy: ONE source of truth, served live.
    //
    //   GET /sitemap.xml  -> Serve()        (always current)
    //   GET /robots.txt   -> ServeRobots()  (points crawlers at the sitemap)
    //
    // The XML is built from the database (published, non-deleted pages and
    // posts; all categories; the home + listing routes) and cached in a single
    // static string. The cache is invalidated automatically through
    // PublicPageCache (Invalidate* call into here), so any content/settings
    // change makes the next request rebuild. The cached document persists for
    // the life of the app process (until an invalidate, a manual regenerate, or
    // a base-URL/host change) — there is no time-based expiry. An IIS app-pool
    // recycle simply drops it and the next request rebuilds it once.
    //
    // The admin "Regenerate now" button calls Generate(), which forces a rebuild
    // and stamps a timestamp - it is the SAME generator, just forced, so the
    // manual and automatic paths can never drift apart.
    //
    // Submit https://yoursite/sitemap.xml once to Google Search Console / Bing
    // Webmaster Tools; it never needs re-uploading.
    // ============================================================
    public static class SitemapGenerator
    {
        // Settings key holding the last manual-generation timestamp (UTC, ISO).
        public const string LastGeneratedKey = "sitemap_last_generated";

        // ===== Cache state =====
        static readonly object _lock = new object();
        static string _xml;                 // cached document, null == needs build
        static int _urlCount;               // number of <url> entries in _xml
        static string _builtBase = "";      // base URL the cache was built for

        // ===== Public entry points =====

        // GET /sitemap.xml - serve the live document (build/refresh as needed).
        public static void Serve()
        {
            string baseUrl = BaseUrl();
            string xml = EnsureBuilt(baseUrl, false);

            var resp = HttpContext.Current.Response;
            resp.ContentType = "application/xml; charset=utf-8";
            resp.AddHeader("X-Robots-Tag", "noindex");   // don't index the file itself
            resp.Write(xml);
            ApiHelper.EndResponse();
        }

        // GET /robots.txt - allow all, and advertise the sitemap so every
        // webmaster tool auto-discovers it.
        public static void ServeRobots()
        {
            string baseUrl = BaseUrl();
            var sb = new StringBuilder();
            sb.Append("User-agent: *\n");
            sb.Append("Allow: /\n");
            sb.Append("Disallow: /admin\n");
            sb.Append("Sitemap: ").Append(baseUrl).Append("/sitemap.xml\n");

            var resp = HttpContext.Current.Response;
            resp.ContentType = "text/plain; charset=utf-8";
            resp.Write(sb.ToString());
            ApiHelper.EndResponse();
        }

        // Manual "Regenerate now" from the admin Settings page. Forces a rebuild
        // for the current request's host and records the timestamp. Returns the
        // number of URLs written.
        public static int Generate()
        {
            string baseUrl = BaseUrl();
            EnsureBuilt(baseUrl, true);
            Db.SaveSetting(LastGeneratedKey, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            return _urlCount;
        }

        // Drop the cache so the next request rebuilds. Wired into
        // PublicPageCache invalidation so content edits refresh the sitemap.
        public static void Invalidate()
        {
            lock (_lock) { _xml = null; }
        }

        // Last manual generation timestamp (UTC ISO string), or "" if never.
        public static string LastGenerated
        {
            get { return Db.GetSetting(LastGeneratedKey, ""); }
        }

        // Absolute URL of the live sitemap, for display/submission.
        public static string SitemapUrl() { return BaseUrl() + "/sitemap.xml"; }

        // ===== Build =====

        // Returns the cached document, rebuilding only when forced, when the
        // cache is empty (first request after start / recycle / invalidate), or
        // when built for a different base URL (host changed). There is no
        // time-based expiry: once built, it persists for the app's lifetime until
        // something explicitly invalidates it.
        static string EnsureBuilt(string baseUrl, bool force)
        {
            lock (_lock)
            {
                bool needsBuild = force || _xml == null || _builtBase != baseUrl;

                if (needsBuild)
                {
                    int count;
                    _xml = Build(baseUrl, out count);
                    _urlCount = count;
                    _builtBase = baseUrl;
                }
                return _xml;
            }
        }

        // Builds the complete urlset from the database.
        static string Build(string baseUrl, out int urlCount)
        {
            int count = 0;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

            // ----- Fixed public routes -----
            AppendUrl(sb, baseUrl, "/", null, "daily", "1.0"); count++;
            AppendUrl(sb, baseUrl, "/latest-post", null, "daily", "0.7"); count++;
            AppendUrl(sb, baseUrl, "/categories-latest-post", null, "daily", "0.7"); count++;

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    // ----- Pages (published, not deleted) -----
                    var pages = s.GetObjectList<obPage>(
                        "SELECT slug, date_created, date_modified, date_published " +
                        "FROM pages WHERE is_published=1 AND is_deleted=0 ORDER BY slug ASC;");
                    foreach (var p in pages)
                    {
                        if (string.IsNullOrEmpty(p.Slug)) continue;
                        AppendUrl(sb, baseUrl, "/" + p.Slug,
                            BestDate(p.DateModified, p.DatePublished, p.DateCreated),
                            "weekly", "0.8");
                        count++;
                    }

                    // ----- Posts (published, not deleted) -----
                    var posts = s.GetObjectList<obPost>(
                        "SELECT slug, date_created, date_modified, date_published " +
                        "FROM posts WHERE is_published=1 AND is_deleted=0 ORDER BY slug ASC;");
                    foreach (var po in posts)
                    {
                        if (string.IsNullOrEmpty(po.Slug)) continue;
                        AppendUrl(sb, baseUrl, "/" + po.Slug,
                            BestDate(po.DateModified, po.DatePublished, po.DateCreated),
                            "weekly", "0.6");
                        count++;
                    }
                }
            }

            // ----- Category archive pages -----
            foreach (var c in CategoryManager.GetAll())
            {
                if (c == null || string.IsNullOrEmpty(c.Slug)) continue;
                AppendUrl(sb, baseUrl, "/category/" + c.Slug, null, "weekly", "0.5");
                count++;
            }

            sb.Append("</urlset>\n");
            urlCount = count;
            return sb.ToString();
        }

        // Emits one <url> block. `lastModUtc` null -> no <lastmod>.
        static void AppendUrl(StringBuilder sb, string baseUrl, string path,
            DateTime? lastModUtc, string changeFreq, string priority)
        {
            sb.Append("  <url>\n");
            sb.Append("    <loc>").Append(XmlEscape(baseUrl + path)).Append("</loc>\n");
            if (lastModUtc.HasValue && lastModUtc.Value > new DateTime(2000, 1, 1))
                sb.Append("    <lastmod>").Append(lastModUtc.Value.ToString("yyyy-MM-dd")).Append("</lastmod>\n");
            sb.Append("    <changefreq>").Append(changeFreq).Append("</changefreq>\n");
            sb.Append("    <priority>").Append(priority).Append("</priority>\n");
            sb.Append("  </url>\n");
        }

        // Most recent meaningful timestamp among the candidates (ignores the
        // 2026-01-01 schema default / MinValue placeholders).
        static DateTime? BestDate(DateTime modified, DateTime published, DateTime created)
        {
            var floor = new DateTime(2010, 1, 1);
            DateTime best = DateTime.MinValue;
            if (modified > best && modified > floor) best = modified;
            if (published > best && published > floor) best = published;
            if (created > best && created > floor) best = created;
            return best == DateTime.MinValue ? (DateTime?)null : best;
        }

        static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }

        // Optional base-URL override (settings: site_url), else the live request
        // host. Trailing slash stripped so callers append "/path" cleanly.
        static string BaseUrl()
        {
            string configured = (Db.GetSetting("site_url", "") ?? "").Trim();
            string baseUrl = configured.Length > 0 ? configured : ApiHelper.GetBaseUrl();
            return baseUrl.TrimEnd('/');
        }
    }
}

using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // Public-facing Internal Content Analytics API.
    //   /api/analytics/start     -> AnalyticsApi.HandleStart()
    //   /api/analytics/heartbeat -> AnalyticsApi.HandleHeartbeat()
    //
    // No auth (these are hit from public pages). Must be robust to garbage
    // or replayed input - any unknown/invalid token is just ignored, never
    // an error surfaced to the client. Both endpoints only enqueue a
    // lightweight command; the actual SQLite work happens on the single
    // background batch worker (see engine/Analytics.cs).
    //
    // Tracks page views ONLY (no IP addresses are ever read or stored).
    // Global.asax never routes an admin/login path here, but this handler
    // adds its own belt-and-braces guard against being pointed at one.
    // ============================================================
    public static class AnalyticsApi
    {
        public static void HandleStart()
        {
            if (!Settings.AnalyticsEnabled) { ApiHelper.WriteError("Analytics disabled", 403); ApiHelper.EndResponse(); return; }

            var req = HttpContext.Current.Request;
            string path = (req.Form["path"] + "").Trim();
            if (string.IsNullOrEmpty(path)) path = (req.QueryString["path"] + "").Trim();
            // Record the bare page path only: strip any query string or fragment so
            // hits on the same page aggregate into one row regardless of the tracking
            // params (?utm_source=..., ?fbclid=..., ?q=...) that social and search
            // referrers routinely append. We lose the search terms, but the report is
            // about which page was viewed, not how the visitor got there.
            int cut = path.IndexOfAny(new char[] { '?', '#' });
            if (cut >= 0) path = path.Substring(0, cut).Trim();
            if (string.IsNullOrEmpty(path)) path = "/";
            // Normalise the trailing slash so "/about/" and "/about" record as one
            // row. The root "/" is preserved (it is nothing but the trailing slash);
            // only paths longer than "/" have their trailing slash(es) removed.
            if (path.Length > 1 && path.EndsWith("/"))
            {
                path = path.TrimEnd('/');
                if (path.Length == 0) path = "/";
            }
            // Lower-case the path so "/About" and "/about" aggregate to one row. The
            // router already matches paths case-insensitively (ToLowerInvariant), so
            // this keeps the recorded path consistent with how the CMS resolves it.
            path = path.ToLowerInvariant();
            if (!IsTrackablePublicPath(path))
            {
                ApiHelper.WriteError("Not a trackable path", 400);
                ApiHelper.EndResponse();
                return;
            }

            string token = Guid.NewGuid().ToString("N");

            DateTime? publishDate = TryResolvePublishDate(path);

            Analytics.EnqueueStartVisit(token, path, publishDate);

            ApiHelper.WriteSuccess("OK", new { token });
            ApiHelper.EndResponse();
        }

        public static void HandleHeartbeat()
        {
            if (!Settings.AnalyticsEnabled) { ApiHelper.WriteSuccess("ignored"); ApiHelper.EndResponse(); return; }

            var req = HttpContext.Current.Request;
            string token = (req.Form["token"] + "").Trim();
            if (string.IsNullOrEmpty(token)) token = (req.QueryString["token"] + "").Trim();

            // Garbage/replay input is simply ignored - no error surfaced.
            if (!string.IsNullOrEmpty(token) && IsPlausibleToken(token))
                Analytics.EnqueueHeartbeat(token);

            ApiHelper.WriteSuccess("OK");
            ApiHelper.EndResponse();
        }

        // A GUID's "N" format is 32 hex chars - reject anything wildly off
        // shape before it ever reaches the queue.
        static bool IsPlausibleToken(string token)
        {
            if (token.Length < 8 || token.Length > 64) return false;
            foreach (char c in token)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // Never track admin pages or the login flow, even if a caller tries.
        static bool IsTrackablePublicPath(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.StartsWith("/admin")) return false;
            if (p.StartsWith("/api/")) return false;
            if (p == "/login" || p == "/logout") return false;
            return true;
        }

        // Best-effort lookup of the page's publish date for a post/page slug, so
        // Page 1's report can show it when easily available. Returns null (not
        // an error) whenever the path doesn't resolve to a known post/page -
        // this is a nice-to-have, never required for a row to be recorded.
        static DateTime? TryResolvePublishDate(string path)
        {
            string slug = path.Trim('/');
            if (string.IsNullOrEmpty(slug) || slug.IndexOf('/') >= 0) return null;

            try
            {
                using (var conn = new Data.SQLite.SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new Data.SQLite.SQLiteExpress(cmd);
                        var prm = new System.Collections.Generic.Dictionary<string, object> { { "@s", slug } };

                        var post = s.GetObject<obPost>(
                            "SELECT * FROM posts WHERE slug=@s AND is_published=1 AND is_deleted=0 LIMIT 1;", prm);
                        if (post != null && post.Id > 0 && post.DatePublished != DateTime.MinValue)
                            return post.DatePublished;

                        var page = s.GetObject<obPage>(
                            "SELECT * FROM pages WHERE slug=@s AND is_published=1 AND is_deleted=0 LIMIT 1;", prm);
                        if (page != null && page.Id > 0 && page.DatePublished != DateTime.MinValue)
                            return page.DatePublished;
                    }
                }
            }
            catch { /* best-effort only */ }

            return null;
        }
    }
}

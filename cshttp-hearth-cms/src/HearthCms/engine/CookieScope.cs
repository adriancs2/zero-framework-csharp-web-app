using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // CookieScope - disambiguates cookie names per listening port.
    //
    // Why this exists: cookies are scoped by HOST but NOT by port (RFC 6265).
    // So two Hearth instances on the same machine - e.g. http://localhost:8001
    // and http://localhost:8002 - share one cookie jar under host "localhost".
    // With a fixed cookie name ("ssid"), each instance keeps overwriting the
    // other's login cookie, so signing in on one appears to sign you out of the
    // other. Suffixing the cookie name with the port gives each instance its own
    // cookie ("ssid_8001" vs "ssid_8002"), so they stop clobbering each other.
    //
    // Standard ports (80 http, 443 https) get NO suffix, so production cookie
    // names stay clean ("ssid", "rmt"). Only non-standard dev/multi-instance
    // ports are suffixed. The result is stable per instance because the bound
    // port is fixed for the life of the site.
    // ============================================================
    public static class CookieScope
    {
        // Return the port-scoped cookie name for `baseName`. Appends "_<port>"
        // for any port other than 80/443; returns baseName unchanged otherwise
        // (or when no request context / port is available).
        public static string Name(string baseName)
        {
            int port = CurrentPort();
            if (port <= 0 || port == 80 || port == 443) return baseName;
            return baseName + "_" + port;
        }

        // The port this request arrived on, or 0 when unavailable.
        static int CurrentPort()
        {
            try
            {
                HttpContext ctx = HttpContext.Current;
                if (ctx == null || ctx.Request == null) return 0;
                var url = ctx.Request.Url;
                return url != null ? url.Port : 0;
            }
            catch { return 0; }
        }
    }
}

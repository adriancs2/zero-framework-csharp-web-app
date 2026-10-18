using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // Admin-only API for the Analytics page:
    //   action=toggle  -> save analytics_enabled ("1"/"0")
    //   action=clear   -> delete every row from the Visits table
    // Routed at /api/admin/analytics (see Global.asax.cs).
    // ============================================================
    public static class AdminAnalyticsApi
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "toggle": Toggle(); break;
                    case "clear": Clear(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Toggle()
        {
            var req = HttpContext.Current.Request;
            bool enable = (req.Form["enabled"] + "").Trim() == "1";
            Db.SaveSetting("analytics_enabled", enable ? "1" : "0");
            // Cached public pages bake the <script> tag in (or out) - drop the
            // cache so the toggle takes effect on the very next page view.
            PublicPageCache.InvalidateAll();
            ApiHelper.WriteSuccess(enable ? "Analytics enabled" : "Analytics disabled");
        }

        static void Clear()
        {
            Analytics.ClearAll();
            ApiHelper.WriteSuccess("All analytics data cleared");
        }
    }
}

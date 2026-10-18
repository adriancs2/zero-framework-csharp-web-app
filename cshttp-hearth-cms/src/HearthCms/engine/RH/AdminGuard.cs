using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminGuard
    {
        // Returns true if request should continue, false if it was redirected/handled.
        public static bool RequireLogin()
        {
            if (Settings.IsDevMode && !AppSession.IsLoggedIn)
            {
                // Auto-login as the first admin user for development
                LoginFirstAdmin();
            }

            if (!AppSession.IsLoggedIn)
            {
                // No separate /login route: render the login page in place at
                // whatever admin path was requested (the admin slug is the only
                // entry point). A successful login reloads into the panel.
                LoginPage.HandleRequest();
                return false;
            }
            return true;
        }

        public static bool RequireLoginApi()
        {
            // 1. Dev Mode: auto-login the first admin (local/testing only).
            if (Settings.IsDevMode && !AppSession.IsLoggedIn)
            {
                LoginFirstAdmin();
            }

            // 2. API token: a request that presents the configured shared secret
            // is authorised as the first admin, with NO login session. This is the
            // path for automated/unattended posting (migration tools, scripts, an
            // AI agent over MCP). Disabled automatically when no token is set.
            if (!AppSession.IsLoggedIn && TokenMatches())
            {
                LoginFirstAdmin();
            }

            if (!AppSession.IsLoggedIn)
            {
                ApiHelper.WriteError("Not signed in", 401);
                ApiHelper.EndResponse();
                return false;
            }
            return true;
        }

        // Sign in as the first admin user (lowest id). Shared by the Dev Mode and
        // API-token paths. No-op if there are no users yet.
        static void LoginFirstAdmin()
        {
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var u = s.GetObject<obUser>("SELECT * FROM users ORDER BY id LIMIT 1;");
                    if (u != null && u.Id > 0) AppSession.LoginUser = u;
                }
            }
        }

        // True when the request carries the configured API token. The token may
        // arrive as the X-Api-Token header, or an "api_token" query-string / form
        // field. Comparison is constant-time. Always false when no token is
        // configured (an empty secret can never be matched).
        static bool TokenMatches()
        {
            string configured = Settings.ApiToken;
            if (string.IsNullOrEmpty(configured)) return false;

            var ctx = HttpContext.Current;
            if (ctx == null || ctx.Request == null) return false;
            var req = ctx.Request;

            string presented = req.Headers["X-Api-Token"];
            if (string.IsNullOrEmpty(presented)) presented = req["api_token"]; // query string or form
            if (string.IsNullOrEmpty(presented)) return false;

            return ConstantTimeEquals(presented.Trim(), configured);
        }

        // Length-aware constant-time string compare, to avoid leaking the token
        // via response-timing differences.
        static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i < b.Length ? i : 0];
            return diff == 0;
        }
    }
}

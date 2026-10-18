using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ===== Per-user session "bag" =====
    public class StateObject
    {
        public ConcurrentDictionary<string, object> Data = new ConcurrentDictionary<string, object>();
        public DateTime LastAccessUtc = DateTime.UtcNow;

        public object this[string key]
        {
            get
            {
                object v;
                return Data.TryGetValue(key, out v) ? v : null;
            }
            set
            {
                if (value == null)
                {
                    object dummy;
                    Data.TryRemove(key, out dummy);
                }
                else
                {
                    Data[key] = value;
                }
            }
        }
    }

    public static class AppSessionKeys
    {
        public const string LoginUser = "LoginUser";
        public const string Flash = "Flash";
    }

    public static class SessionStore
    {
        // Base cookie name; the actual name is port-scoped via CookieScope so
        // multiple instances on the same host (different ports) don't share one
        // cookie. See CookieScope for the rationale.
        public const string BaseCookieName = "ssid";
        public static string CookieName { get { return CookieScope.Name(BaseCookieName); } }
        const string CtxItemKey = "SessionStore.CurrentState";

        public static readonly ConcurrentDictionary<string, StateObject> Sessions =
            new ConcurrentDictionary<string, StateObject>();

        static string NewId()
        {
            byte[] buf = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            return BitConverter.ToString(buf).Replace("-", "").ToLowerInvariant();
        }

        public static StateObject Current
        {
            get
            {
                HttpContext ctx = HttpContext.Current;
                if (ctx == null) return null;

                // Per-request cache: subsequent calls within the same request return
                // the SAME StateObject. Without this, the freshly-issued ssid cookie
                // hasn't round-tripped yet, so a second access to .Current would
                // create another fresh state and silently drop anything we just set.
                var cached = ctx.Items[CtxItemKey] as StateObject;
                if (cached != null)
                {
                    cached.LastAccessUtc = DateTime.UtcNow;
                    return cached;
                }

                string sid = ctx.Request.Cookies[CookieName]?.Value;

                StateObject state;
                if (string.IsNullOrEmpty(sid) || !Sessions.TryGetValue(sid, out state))
                {
                    sid = NewId();
                    state = new StateObject();
                    Sessions[sid] = state;

                    HttpCookie c = new HttpCookie(CookieName, sid);
                    c.HttpOnly = true;
                    c.Secure = ctx.Request.IsSecureConnection;
                    c.SameSite = SameSiteMode.Lax;
                    c.Path = "/";
                    ctx.Response.Cookies.Set(c);
                }
                state.LastAccessUtc = DateTime.UtcNow;
                ctx.Items[CtxItemKey] = state;
                return state;
            }
        }

        // Peek: returns true if the request has an ssid cookie that maps to a
        // session in our dictionary AND that session has a LoginUser. Does NOT
        // create a new session or touch Response cookies. Safe to call from the
        // cache layer for anonymous requests (won't bake in Set-Cookie).
        public static bool HasActiveLogin()
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) return false;

            // Fast path: per-request cached state
            var cached = ctx.Items[CtxItemKey] as StateObject;
            if (cached != null) return cached[AppSessionKeys.LoginUser] is obUser;

            HttpCookie c = ctx.Request.Cookies[CookieName];
            if (c == null || string.IsNullOrEmpty(c.Value)) return false;
            StateObject state;
            if (!Sessions.TryGetValue(c.Value, out state)) return false;
            return state[AppSessionKeys.LoginUser] is obUser;
        }

        public static void Abandon()
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) return;
            string sid = ctx.Request.Cookies[CookieName]?.Value;
            if (!string.IsNullOrEmpty(sid))
            {
                StateObject removed;
                Sessions.TryRemove(sid, out removed);
            }
            ctx.Items.Remove(CtxItemKey);
            HttpCookie c = new HttpCookie(CookieName, "");
            c.Expires = DateTime.UtcNow.AddDays(-1);
            c.Path = "/";
            ctx.Response.Cookies.Set(c);
        }
    }

    public static class AppSession
    {
        public static obUser LoginUser
        {
            get
            {
                var s = SessionStore.Current;
                return s == null ? null : s[AppSessionKeys.LoginUser] as obUser;
            }
            set
            {
                var s = SessionStore.Current;
                if (s != null) s[AppSessionKeys.LoginUser] = value;
            }
        }

        public static bool IsLoggedIn { get { return LoginUser != null; } }

        public static void SetFlash(string message)
        {
            var s = SessionStore.Current;
            if (s != null) s[AppSessionKeys.Flash] = message;
        }

        public static string PopFlash()
        {
            var s = SessionStore.Current;
            if (s == null) return "";
            string m = s[AppSessionKeys.Flash] as string;
            s[AppSessionKeys.Flash] = null;
            return m ?? "";
        }
    }
}

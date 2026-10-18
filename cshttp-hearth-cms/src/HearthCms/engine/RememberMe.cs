using System.Collections.Generic;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // Persistent "remember me" login via a long-lived cookie backed by SQLite.
    // Cookie format: "<selector>.<validator>" (both hex). The selector indexes the
    // row; the validator is compared constant-time against its SHA-256 hash. A DB
    // read alone does not yield a usable token, and stolen cookies can be revoked
    // by deleting the row.
    public static class RememberMe
    {
        // Base cookie name; the actual name is port-scoped via CookieScope so
        // multiple instances on the same host (different ports) don't share one
        // remember-me cookie. See CookieScope for the rationale.
        public const string BaseCookieName = "rmt";
        public static string CookieName { get { return CookieScope.Name(BaseCookieName); } }
        const int SelectorBytes = 12;
        const int ValidatorBytes = 32;
        const int DefaultDays = 30;
        // How long the just-rotated (previous) validator stays acceptable, so a
        // burst of concurrent requests carrying the same pre-rotation cookie all
        // authenticate instead of tripping theft detection. See TryRestore.
        const int GraceSeconds = 30;
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static void Issue(int userId)
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null || userId <= 0) return;

            string selector = RandomHex(SelectorBytes);
            byte[] validatorBytes = RandomBytes(ValidatorBytes);
            string validator = ToHex(validatorBytes);
            string validatorHash = Sha256Hex(validatorBytes);
            DateTime expires = DateTime.UtcNow.AddDays(DefaultDays);

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var d = new Dictionary<string, object>();
                    d["selector"] = selector;
                    d["validator_hash"] = validatorHash;
                    d["user_id"] = userId;
                    d["expires_at"] = expires;
                    d["date_created"] = DateTime.UtcNow;
                    s.Insert("user_sessions", d);
                }
            }

            var c = new HttpCookie(CookieName, selector + "." + validator);
            c.HttpOnly = true;
            c.Secure = ctx.Request.IsSecureConnection;
            c.SameSite = SameSiteMode.Lax;
            c.Path = "/";
            c.Expires = expires;
            ctx.Response.Cookies.Set(c);
        }

        // If no active login but a valid remember cookie is present, load the user
        // and populate AppSession.LoginUser. Rotates the validator on success.
        public static void TryRestore()
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) return;
            if (SessionStore.HasActiveLogin()) return;

            HttpCookie cookie = ctx.Request.Cookies[CookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value)) return;

            string[] parts = cookie.Value.Split('.');
            if (parts.Length != 2) { ClearCookie(); return; }
            string selector = parts[0];
            string validator = parts[1];
            if (string.IsNullOrEmpty(selector) || string.IsNullOrEmpty(validator))
            {
                ClearCookie(); return;
            }

            obUser user = null;

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var p = new Dictionary<string, object> { { "@s", selector } };
                    var dt = s.Select(
                        "SELECT id, validator_hash, prev_validator_hash, rotated_unix, user_id, expires_at FROM user_sessions WHERE selector=@s LIMIT 1;", p);
                    if (dt == null || dt.Rows.Count == 0) { ClearCookie(); return; }
                    var r = dt.Rows[0];
                    int rowId = Convert.ToInt32(r["id"]);
                    string storedHash = r["validator_hash"] as string ?? "";
                    string prevHash = r["prev_validator_hash"] as string ?? "";
                    long rotatedUnix = (r["rotated_unix"] == null || r["rotated_unix"] is DBNull)
                        ? 0L : Convert.ToInt64(r["rotated_unix"]);
                    int userId = Convert.ToInt32(r["user_id"]);
                    DateTime expires = Convert.ToDateTime(r["expires_at"]);

                    if (DateTime.UtcNow > expires)
                    {
                        var pp = new Dictionary<string, object> { { "@i", rowId } };
                        s.Execute("DELETE FROM user_sessions WHERE id=@i;", pp);
                        ClearCookie();
                        return;
                    }

                    byte[] presented;
                    try { presented = FromHex(validator); }
                    catch { ClearCookie(); return; }
                    string presentedHash = Sha256Hex(presented);

                    bool isCurrent = ConstantTimeEquals(presentedHash, storedHash);
                    // Grace window: also accept the immediately-PREVIOUS validator for
                    // a few seconds after it was rotated out. When a browser reopens
                    // and restores several admin tabs at once, all the requests carry
                    // the same not-yet-rotated cookie and arrive together; the first to
                    // be served rotates the token, and without this window every other
                    // concurrent request would present the now-superseded validator and
                    // be misread as a stolen cookie - deleting the row and logging the
                    // user out for good. Genuine theft (an old cookie replayed long
                    // after rotation) still falls outside the window and is caught.
                    bool isPrevGrace = !isCurrent
                        && prevHash.Length > 0
                        && ConstantTimeEquals(presentedHash, prevHash)
                        && (NowUnix() - rotatedUnix) <= GraceSeconds;

                    if (!isCurrent && !isPrevGrace)
                    {
                        // Matches neither the current token nor a just-superseded one:
                        // treat as a stolen/forged cookie and revoke the session.
                        var pp = new Dictionary<string, object> { { "@i", rowId } };
                        s.Execute("DELETE FROM user_sessions WHERE id=@i;", pp);
                        ClearCookie();
                        return;
                    }

                    var pu = new Dictionary<string, object> { { "@i", userId } };
                    user = s.GetObject<obUser>("SELECT * FROM users WHERE id=@i LIMIT 1;", pu);
                    if (user == null || user.Id == 0)
                    {
                        var pp = new Dictionary<string, object> { { "@i", rowId } };
                        s.Execute("DELETE FROM user_sessions WHERE id=@i;", pp);
                        ClearCookie();
                        return;
                    }

                    // Rotate ONLY when the current validator was presented, and do it
                    // as an atomic compare-and-swap on the validator we just read
                    // (WHERE ... AND validator_hash=@cur). Of N concurrent requests
                    // exactly one wins the swap (changes()==1) and re-issues the
                    // cookie; the losers (changes()==0) were rotated out a moment ago
                    // and simply ride the grace window above, leaving the winner's
                    // fresh cookie untouched so the client converges on one token.
                    if (isCurrent)
                    {
                        byte[] newValidatorBytes = RandomBytes(ValidatorBytes);
                        string newValidator = ToHex(newValidatorBytes);
                        string newHash = Sha256Hex(newValidatorBytes);
                        DateTime newExpires = DateTime.UtcNow.AddDays(DefaultDays);
                        var pr = new Dictionary<string, object>
                        {
                            { "@h", newHash },
                            { "@ph", storedHash },
                            { "@ru", NowUnix() },
                            { "@e", newExpires },
                            { "@i", rowId },
                            { "@cur", storedHash }
                        };
                        s.Execute("UPDATE user_sessions SET validator_hash=@h, prev_validator_hash=@ph, rotated_unix=@ru, expires_at=@e WHERE id=@i AND validator_hash=@cur;", pr);
                        long changed = s.ExecuteScalar<long>("SELECT changes();");
                        if (changed == 1)
                        {
                            var c = new HttpCookie(CookieName, selector + "." + newValidator);
                            c.HttpOnly = true;
                            c.Secure = ctx.Request.IsSecureConnection;
                            c.SameSite = SameSiteMode.Lax;
                            c.Path = "/";
                            c.Expires = newExpires;
                            ctx.Response.Cookies.Set(c);
                        }
                        // changed == 0: a concurrent request already rotated this row;
                        // do not reissue - that request's cookie is the live one.
                    }
                }
            }

            AppSession.LoginUser = user;
        }

        public static void RevokeCurrent()
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) return;
            HttpCookie cookie = ctx.Request.Cookies[CookieName];
            if (cookie != null && !string.IsNullOrEmpty(cookie.Value))
            {
                string[] parts = cookie.Value.Split('.');
                if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]))
                {
                    try
                    {
                        using (var conn = new SQLiteConnection(Config.GetConnString()))
                        {
                            conn.Open();
                            using (var cmd = conn.CreateCommand())
                            {
                                var s = new SQLiteExpress(cmd);
                                var p = new Dictionary<string, object> { { "@s", parts[0] } };
                                s.Execute("DELETE FROM user_sessions WHERE selector=@s;", p);
                            }
                        }
                    }
                    catch { }
                }
            }
            ClearCookie();
        }

        static void ClearCookie()
        {
            HttpContext ctx = HttpContext.Current;
            if (ctx == null) return;
            var c = new HttpCookie(CookieName, "");
            c.Expires = DateTime.UtcNow.AddDays(-1);
            c.Path = "/";
            ctx.Response.Cookies.Set(c);
        }

        // Current time as whole seconds since the Unix epoch. Stored/compared as an
        // integer so the grace-window math is immune to the DateTime-kind/timezone
        // quirks of how SQLite round-trips DATETIME columns.
        static long NowUnix()
        {
            return (long)(DateTime.UtcNow - UnixEpoch).TotalSeconds;
        }

        static byte[] RandomBytes(int n)
        {
            byte[] buf = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            return buf;
        }

        static string RandomHex(int n) { return ToHex(RandomBytes(n)); }

        static string ToHex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        static byte[] FromHex(string hex)
        {
            if (hex == null) hex = "";
            if ((hex.Length & 1) != 0) throw new FormatException("odd hex length");
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(data));
        }

        static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}

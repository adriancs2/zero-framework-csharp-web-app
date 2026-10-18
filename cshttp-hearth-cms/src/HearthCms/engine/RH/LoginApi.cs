using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class LoginApi
    {
        public static void HandleRequest()
        {
            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "login": Login(); break;

                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Login()
        {
            var req = HttpContext.Current.Request;
            string username = (req.Form["username"] + "").Trim();
            string password = req.Form["password"] + "";
            string rememberRaw = (req.Form["remember"] + "").Trim().ToLowerInvariant();
            bool remember = rememberRaw == "1" || rememberRaw == "true" || rememberRaw == "on" || rememberRaw == "yes";

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ApiHelper.WriteError("Username and password are required");
                return;
            }

            obUser user = null;
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var p = new Dictionary<string, object> { { "@u", username } };
                    user = s.GetObject<obUser>(
                        "SELECT * FROM users WHERE username=@u LIMIT 1;", p);
                }
            }

            if (user == null || user.Id == 0 || !Auth.Verify(password, user.PasswordHash))
            {
                ApiHelper.WriteError("Invalid username or password", 401);
                return;
            }

            AppSession.LoginUser = user;
            if (remember) RememberMe.Issue(user.Id);
            ApiHelper.WriteSuccess("Signed in");
        }


    }
}

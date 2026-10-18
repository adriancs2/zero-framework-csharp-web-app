using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // AdminUserApi - JSON endpoint for the User Management page.
    // Route: /api/admin/users  (POST, action=...).
    //
    // Thin dispatcher: all the real work lives in AdminUser so the page
    // handler and the API share one implementation. Actions:
    //   list   -> { success, data: { html } }  (pre-rendered table block)
    //   get    -> { success, data: { ...user } } (no password hash)
    //   save   -> create or update
    //   delete -> remove (with last-user / self guards)
    // ============================================================
    public static class AdminUserApi
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
                    case "list":
                        ApiHelper.WriteSuccess("OK", new { html = GetUserListHtml() });
                        break;
                    case "get": GetUser(); break;
                    case "save": SaveUser(); break;
                    case "delete": DeleteUser(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }


        // ===== Pre-rendered list table (initial load + API "list" refresh) =====
        // Returns the full table block (or an empty-state card). Self-contained
        // HTML so the client can drop it straight into #userListWrap.
        public static string GetUserListHtml()
        {
            List<obUser> users = new List<obUser>();
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        users = s.GetObjectList<obUser>(
                            "SELECT * FROM users ORDER BY username COLLATE NOCASE ASC;");
                    }
                }
            }
            catch { }

            if (users == null || users.Count == 0)
            {
                return @"
<div class='empty-card'>
    <i class='fa-solid fa-users empty-icon'></i>
    <h2>No users yet</h2>
    <p>Add an account so someone can sign in to the admin panel.</p>
    <button type='button' class='btn btn-primary' onclick='userOpenCreate()'><i class='fa-solid fa-plus'></i> Add a user</button>
</div>";
            }

            int currentId = AppSession.LoginUser != null ? AppSession.LoginUser.Id : 0;

            var sb = new StringBuilder();
            sb.Append(@"
<div class='data-table-wrap'>
<table class='data-table'>
    <thead>
        <tr>
            <th>Username</th>
            <th>Display name</th>
            <th>Email</th>
            <th class='col-narrow'>Role</th>
            <th class='col-actions'></th>
        </tr>
    </thead>
    <tbody>
");
            foreach (var u in users)
            {
                string display = string.IsNullOrEmpty(u.DisplayName)
                    ? "<span class='text-muted'>-</span>" : HttpUtility.HtmlEncode(u.DisplayName);
                string email = string.IsNullOrEmpty(u.Email)
                    ? "<span class='text-muted'>-</span>" : HttpUtility.HtmlEncode(u.Email);
                string role = string.IsNullOrEmpty(u.Role) ? "admin" : u.Role;
                string youBadge = (u.Id == currentId)
                    ? " <span class='pill pill-muted'>you</span>" : "";

                // Delete is disabled for the signed-in account; the JS also guards
                // the last-user case after a server check.
                string deleteBtn = (u.Id == currentId)
                    ? "<button type='button' class='icon-btn' title='You cannot delete your own account' disabled><i class='fa-solid fa-trash'></i></button>"
                    : $"<button type='button' class='icon-btn icon-btn-danger' onclick='userDelete({u.Id})' title='Delete'><i class='fa-solid fa-trash'></i></button>";

                sb.Append($@"
        <tr>
            <td><button type='button' class='row-title linklike' onclick='userOpenEdit({u.Id})'>{HttpUtility.HtmlEncode(u.Username)}</button>{youBadge}</td>
            <td>{display}</td>
            <td>{email}</td>
            <td><span class='pill'>{HttpUtility.HtmlEncode(role)}</span></td>
            <td class='col-actions'>
                <button type='button' class='icon-btn' onclick='userOpenEdit({u.Id})' title='Edit'><i class='fa-solid fa-pen'></i></button>
                {deleteBtn}
            </td>
        </tr>");
            }
            sb.Append(@"
    </tbody>
</table>
</div>");
            return sb.ToString();
        }

        // ===== API logic: get a single user (for the edit dialog) =====
        // Never returns the password hash. Writes the JSON response directly.
        public static void GetUser()
        {
            var req = HttpContext.Current.Request;
            int id = 0; int.TryParse(req["id"] + "", out id);
            if (id <= 0) { ApiHelper.WriteError("Invalid user id"); return; }

            obUser u = LoadById(id);
            if (u == null) { ApiHelper.WriteError("User not found"); return; }

            ApiHelper.WriteSuccess("OK", new
            {
                id = u.Id,
                username = u.Username,
                display_name = u.DisplayName,
                email = u.Email,
                role = string.IsNullOrEmpty(u.Role) ? "admin" : u.Role
            });
        }

        // ===== API logic: create or update a user =====
        // Password rules: required when creating; on edit, a blank password means
        // "keep the existing one" and only a non-blank value re-hashes.
        public static void SaveUser()
        {
            var req = HttpContext.Current.Request;

            int id = 0; int.TryParse(req.Form["id"] + "", out id);
            string username = (req.Form["username"] + "").Trim();
            string displayName = (req.Form["display_name"] + "").Trim();
            string email = (req.Form["email"] + "").Trim();
            string role = (req.Form["role"] + "").Trim().ToLowerInvariant();
            string password = (req.Form["password"] + "");   // not trimmed: spaces may be intentional

            if (string.IsNullOrEmpty(username)) { ApiHelper.WriteError("Username is required"); return; }
            if (!IsValidUsername(username))
            {
                ApiHelper.WriteError("Username may use only letters, digits, dot, dash and underscore.");
                return;
            }
            if (role != "admin" && role != "editor") role = "admin";

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    // Username uniqueness (excluding self).
                    var pp = new Dictionary<string, object> { { "@u", username }, { "@id", id } };
                    int dup = s.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM users WHERE username=@u AND id<>@id;", pp);
                    if (dup > 0) { ApiHelper.WriteError("Another user already has that username"); return; }

                    var d = new Dictionary<string, object>();
                    d["username"] = username;
                    d["display_name"] = displayName;
                    d["email"] = email;
                    d["role"] = role;

                    if (id <= 0)
                    {
                        // Create: password is mandatory.
                        if (string.IsNullOrEmpty(password))
                        {
                            ApiHelper.WriteError("A password is required for a new user");
                            return;
                        }
                        d["password_hash"] = Auth.Hash(password);
                        d["date_created"] = DateTime.UtcNow;
                        s.Insert("users", d);
                        id = (int)s.LastInsertId;
                        AppSession.SetFlash("User created");
                    }
                    else
                    {
                        var pCheck = new Dictionary<string, object> { { "@id", id } };
                        var existing = s.GetObject<obUser>(
                            "SELECT * FROM users WHERE id=@id LIMIT 1;", pCheck);
                        if (existing == null || existing.Id == 0) { ApiHelper.WriteError("User not found"); return; }

                        // Blank password on edit = keep the existing hash.
                        if (!string.IsNullOrEmpty(password))
                            d["password_hash"] = Auth.Hash(password);

                        s.Update("users", d, "id", id);

                        // If the edited user is the one currently signed in, refresh
                        // the cached session copy so the topbar / author name update.
                        if (AppSession.LoginUser != null && AppSession.LoginUser.Id == id)
                        {
                            var refreshed = s.GetObject<obUser>(
                                "SELECT * FROM users WHERE id=@id LIMIT 1;", pCheck);
                            if (refreshed != null && refreshed.Id > 0) AppSession.LoginUser = refreshed;
                        }

                        AppSession.SetFlash("User updated");
                    }
                }
            }

            ApiHelper.WriteSuccess("Saved", new { id });
        }

        // ===== API logic: delete a user =====
        // Guards: cannot delete the signed-in account, and cannot delete the
        // last remaining user (an instance must keep at least one login).
        public static void DeleteUser()
        {
            var req = HttpContext.Current.Request;
            int id = 0; int.TryParse(req.Form["id"] + "", out id);
            if (id <= 0) { ApiHelper.WriteError("Invalid user id"); return; }

            if (AppSession.LoginUser != null && AppSession.LoginUser.Id == id)
            {
                ApiHelper.WriteError("You cannot delete your own account");
                return;
            }

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    int total = s.ExecuteScalar<int>("SELECT COUNT(*) FROM users;");
                    if (total <= 1)
                    {
                        ApiHelper.WriteError("This is the only user account and cannot be deleted");
                        return;
                    }

                    var p = new Dictionary<string, object> { { "@id", id } };
                    var existing = s.GetObject<obUser>(
                        "SELECT * FROM users WHERE id=@id LIMIT 1;", p);
                    if (existing == null || existing.Id == 0) { ApiHelper.WriteError("User not found"); return; }

                    // Remove the user and any persistent "remember me" sessions tied
                    // to them, so a deleted account can't be restored from a cookie.
                    s.Execute("DELETE FROM user_sessions WHERE user_id=@id;", p);
                    s.Execute("DELETE FROM users WHERE id=@id;", p);
                }
            }

            AppSession.SetFlash("User deleted");
            ApiHelper.WriteSuccess("Deleted");
        }

        // ----- helpers -----

        static obUser LoadById(int id)
        {
            if (id <= 0) return null;
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var p = new Dictionary<string, object> { { "@id", id } };
                        var u = s.GetObject<obUser>("SELECT * FROM users WHERE id=@id LIMIT 1;", p);
                        return (u != null && u.Id > 0) ? u : null;
                    }
                }
            }
            catch { return null; }
        }

        // Usernames: letters, digits, dot, dash, underscore; 1-80 chars.
        static bool IsValidUsername(string username)
        {
            if (string.IsNullOrEmpty(username) || username.Length > 80) return false;
            foreach (char ch in username)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
                    || (ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
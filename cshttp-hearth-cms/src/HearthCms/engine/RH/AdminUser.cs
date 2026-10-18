using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // AdminUser - the User Management admin page (/admin/users) and the
    // logic backing its API (/api/admin/users, dispatched by AdminUserApi).
    //
    // The page renders a table of all users. The table body is pre-rendered
    // server-side by AdminUserApi.GetUserListHtml() during the initial page request, so the
    // first paint already shows the list (no extra fetch on load). Add / edit
    // happen in a popup dialog driven by /js/admin-user.js, which posts to the
    // API and then refreshes the table via the same GetUserListHtml() output.
    //
    // Roles are free-text but the UI offers "admin" and "editor". The only
    // hard rule the engine enforces is that the LAST remaining user cannot be
    // deleted (an instance must always keep at least one login), and a user
    // cannot delete their own currently-signed-in account.
    // ============================================================
    public static class AdminUser
    {
        // ===== /admin/users : full page (table pre-populated inline) =====
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            var tpl = new AdminTemplate
            {
                Title = "Users",
                ActiveItem = "users",
                PageHeading = "Users",
                PageHeadingActionsHtml =
                    "<button type='button' class='btn btn-primary btn-sm' onclick='userOpenCreate()'><i class='fa-solid fa-plus'></i> New user</button>",
                ExtraFooterText = "<script src='/js/admin-user.js'></script>\n"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            // Pre-render the list table block on the initial request so the page
            // arrives already populated. The same markup is returned by the API
            // ("list" action) when the client needs to refresh after a change.
            sb.Append(@"
<div id='userListWrap'>
");
            sb.Append(AdminUserApi.GetUserListHtml());
            sb.Append(@"
</div>
");

            // ----- the add/edit dialog (hidden until opened by admin-user.js) -----
            sb.Append(@"
<div id='userDialogOverlay' class='user-dialog-overlay' style='display:none' onclick='userDialogBackdrop(event)'>
    <div class='user-dialog' role='dialog' aria-modal='true' aria-labelledby='userDialogTitle'>
        <div class='user-dialog-head'>
            <h2 id='userDialogTitle'>New user</h2>
            <button type='button' class='icon-btn' onclick='userCloseDialog()' aria-label='Close'><i class='fa-solid fa-xmark'></i></button>
        </div>
        <form id='userForm' onsubmit='return userSubmit(event)' class='user-dialog-body'>
            <input type='hidden' id='user_id' name='id' value='0' />
            <div class='form-field'>
                <label for='user_username'>Username</label>
                <input type='text' id='user_username' name='username' maxlength='80' autocomplete='off' pattern='[a-zA-Z0-9_.-]+' required />
                <small class='form-hint'>Letters, digits, dot, dash and underscore.</small>
            </div>
            <div class='form-field'>
                <label for='user_display_name'>Display name</label>
                <input type='text' id='user_display_name' name='display_name' maxlength='120' autocomplete='off' placeholder='Shown as the author name (optional)' />
            </div>
            <div class='form-field'>
                <label for='user_email'>Email</label>
                <input type='email' id='user_email' name='email' maxlength='200' autocomplete='off' placeholder='(optional)' />
            </div>
            <div class='form-field'>
                <label for='user_role'>Role</label>
                <select id='user_role' name='role'>
                    <option value='admin'>admin</option>
                    <option value='editor'>editor</option>
                </select>
            </div>
            <div class='form-field'>
                <label for='user_password' id='user_password_label'>Password</label>
                <input type='password' id='user_password' name='password' maxlength='200' autocomplete='new-password' />
                <small class='form-hint' id='user_password_hint'>Set a password for the new user.</small>
            </div>
            <div class='user-dialog-actions'>
                <button type='submit' class='btn btn-primary'><i class='fa-solid fa-floppy-disk'></i> Save user</button>
                <button type='button' class='btn btn-ghost' onclick='userCloseDialog()'>Cancel</button>
            </div>
        </form>
    </div>
</div>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

    }
}

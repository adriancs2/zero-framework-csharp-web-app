using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // First-run installer.
    //
    //   /install          -> InstallPage.HandleRequest()  (the form)
    //   /api/install      -> InstallApi.HandleRequest()   (create + seed)
    //
    // Until the database file exists the router forces every request to the
    // setup page (see Global.asax). The form collects a site name, the first
    // admin's username/password and a custom admin login path; on submit it
    // creates the database, seeds starter content + navigation, sets the site
    // name and a footer copyright column, and stamps the schema version. The
    // existence of the database file is what marks the site installed.
    // ============================================================
    public static class InstallPage
    {
        public static void HandleRequest()
        {
            // If already installed, never show the installer; send to the admin.
            if (Db.IsInstalled)
            {
                ApiHelper.Redirect("/" + AdminSlug.Current);
                return;
            }

            var sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1.0' />
<title>Install</title>
<link rel='preconnect' href='https://fonts.googleapis.com'>
<link rel='preconnect' href='https://fonts.gstatic.com' crossorigin>
<link rel='stylesheet' href='https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap'>
<link rel='stylesheet' href='/fonts/fontawesome/css/all.min.css' />
<link rel='stylesheet' href='/css/admin.css' />
<link rel='stylesheet' href='/css/login.css' />
</head>
<body class='page-login'>
<div class='login-shell'>
    <div class='login-card'>
        <div class='login-brand'>
            <i class='fa-solid fa-feather'></i>
            <span>Set up your site</span>
        </div>
        <h1 class='login-title'>Welcome</h1>
        <p class='login-sub'>A few details to get started. This runs once.</p>
        <form id='installForm' onsubmit='return doInstall(event)'>
            <div class='form-field'>
                <label for='site_name'>Site name</label>
                <input id='site_name' name='site_name' type='text' required autofocus placeholder='My Site' />
            </div>
            <div class='form-field'>
                <label for='username'>Admin username</label>
                <input id='username' name='username' type='text' autocomplete='username' required placeholder='Username' />
            </div>
            <div class='form-field'>
                <label for='password'>Admin password</label>
                <input id='password' name='password' type='password' autocomplete='new-password' required placeholder='Password' />
            </div>
            <div class='form-field'>
                <label for='email'>Admin email (optional)</label>
                <input id='email' name='email' type='email' placeholder='email@example.com' />
                <small style='display:block;margin-top:.35rem;color:#888'>Optional. Enables matching email sign-in from Open Auth (Google, Apple, GitHub, Microsoft).</small>
            </div>
            <div class='form-field'>
                <label for='admin_path'>Admin login path</label>
                <input id='admin_path' name='admin_path' type='text' placeholder='admin' />
                <small style='display:block;margin-top:.35rem;color:#888'>The URL where you'll sign in, e.g. <code>/admin</code>. Letters, numbers, dashes, underscores. Leave blank for <code>admin</code>.</small>
            </div>
            <div id='installError' class='login-error' style='display:none'></div>
            <button type='submit' class='btn btn-primary btn-block'>
                <span class='btn-label'>Install</span>
                <i class='fa-solid fa-arrow-right'></i>
            </button>
        </form>
    </div>
</div>
<script>
async function doInstall(e) {
    e.preventDefault();
    var err = document.getElementById('installError');
    err.style.display = 'none';
    var fd = new FormData();
    fd.append('action', 'install');
    fd.append('site_name', document.getElementById('site_name').value.trim());
    fd.append('username', document.getElementById('username').value.trim());
    fd.append('password', document.getElementById('password').value.trim());
    fd.append('email', document.getElementById('email').value.trim());
    fd.append('admin_path', document.getElementById('admin_path').value.trim());
    try {
        var r = await fetch('/api/install', { method: 'POST', body: fd });
        var d = await r.json();
        if (d.success) {
            window.location = (d.data && d.data.adminBase) ? d.data.adminBase : '/admin';
        } else {
            err.textContent = d.message || 'Install failed';
            err.style.display = 'block';
        }
    } catch (ex) {
        err.textContent = 'Network error. Please try again.';
        err.style.display = 'block';
    }
    return false;
}
</script>
</body>
</html>");
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }
    }

    public static class InstallApi
    {
        public static void HandleRequest()
        {
            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "install": Install(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Install()
        {
            if (Db.IsInstalled)
            {
                ApiHelper.WriteError("This site is already installed.");
                return;
            }

            var req = HttpContext.Current.Request;
            string siteName = req.Form["site_name"];
            string username = req.Form["username"];
            string password = req.Form["password"];
            string adminPath = req.Form["admin_path"];
            string email = req.Form["email"];

            string error;
            if (!Db.RunInstall(siteName, username, password, adminPath, email, out error))
            {
                ApiHelper.WriteError(error);
                return;
            }

            // Fresh install wipes any pre-existing cache so the new site name,
            // footer and nav render immediately.
            PublicPageCache.InvalidateAll();

            // The admin slug may have just been set; re-resolve so the redirect
            // target points at the chosen login path.
            AdminSlug.Reload();

            ApiHelper.WriteSuccess("Installed", new { adminBase = "/" + AdminSlug.Current });
        }
    }
}

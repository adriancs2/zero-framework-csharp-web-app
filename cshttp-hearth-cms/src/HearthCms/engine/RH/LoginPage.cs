using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class LoginPage
    {
        public static void HandleRequest()
        {
            if (AppSession.IsLoggedIn)
            {
                ApiHelper.Redirect("/" + AdminSlug.Current);
                return;
            }

            string brand = Settings.SiteName;
            string flash = AppSession.PopFlash();
            string flashHtml = string.IsNullOrEmpty(flash)
                ? ""
                : "<div class='login-flash'>" + HttpUtility.HtmlEncode(flash) + "</div>";

            // The login API lives under the admin slug too, so it stays as
            // hidden as the panel itself. Build it from the resolved slug
            // (e.g. /backend/api/login) and inject it into the form's JS.
            string loginApi = "/" + AdminSlug.Current + "/api/login";

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1.0' />
<title>Sign in · {HttpUtility.HtmlEncode(brand)}</title>
<link rel='preconnect' href='https://fonts.googleapis.com'>
<link rel='preconnect' href='https://fonts.gstatic.com' crossorigin>
<link rel='stylesheet' href='https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap'>
<link rel='stylesheet' href='/fonts/fontawesome/css/all.min.css' />
<link rel='stylesheet' href='/css/admin.css' />
<link rel='stylesheet' href='/css/login.css?v=3' />
</head>
<body class='page-login'>
<div class='login-shell'>
    <div class='login-card'>
        <div class='login-brand'>
            <i class='fa-solid fa-feather'></i>
            <span>{HttpUtility.HtmlEncode(brand)}</span>
        </div>
        <h1 class='login-title'>Welcome back</h1>
        <p class='login-sub'>Sign in to manage your site.</p>
        {flashHtml}
        <form id='loginForm' onsubmit='return doLogin(event)'>
            <div class='form-field'>
                <label for='username'>Username</label>
                <input id='username' name='username' type='text' autocomplete='username' required autofocus />
            </div>
            <div class='form-field'>
                <label for='password'>Password</label>
                <input id='password' name='password' type='password' autocomplete='current-password' required />
            </div>
            <div class='form-field form-field-inline'>
                <label class='remember-label'>
                    <input id='remember' name='remember' type='checkbox' value='1' />
                    <span>Remember me</span>
                </label>
            </div>
            <div id='loginError' class='login-error' style='display:none'></div>
            <button type='submit' class='btn btn-primary btn-block'>
                <span class='btn-label'>Sign in</span>
                <i class='fa-solid fa-arrow-right'></i>
            </button>
        </form>

        <p class='login-meta'><a href='/'><i class='fa-solid fa-arrow-left'></i> Back to site</a></p>
    </div>
</div>
<script>
async function doLogin(e) {{
    e.preventDefault();
    var err = document.getElementById('loginError');
    err.style.display = 'none';
    var u = document.getElementById('username').value.trim();
    var p = document.getElementById('password').value;
    var rm = document.getElementById('remember').checked ? '1' : '';
    var fd = new FormData();
    fd.append('action', 'login');
    fd.append('username', u);
    fd.append('password', p);
    fd.append('remember', rm);
    try {{
        var r = await fetch('{loginApi}', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success) {{
            // Reload wherever we already are (the admin slug root / path the
            // user requested). There is no fixed /admin redirect, so a custom
            // slug stays in the address bar. Fall back to '/' defensively.
            window.location.reload();
        }} else {{
            err.textContent = d.message || 'Invalid credentials';
            err.style.display = 'block';
        }}
    }} catch (ex) {{
        err.textContent = 'Network error. Please try again.';
        err.style.display = 'block';
    }}
    return false;
}}


</script>
</body>
</html>");
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }
    }
}

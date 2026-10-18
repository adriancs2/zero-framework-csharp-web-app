using System.Collections.Generic;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // /admin/guidelines - "General Guidelines".
    //
    // Operational notes for site operators. The prose lives as a static Markdown
    // file (/App_Data/AdminDocumentation/GeneralGuidelines.md) rendered by
    // AdminDocRenderer. Two pieces are install-specific and therefore NOT in the
    // file:
    //
    //   1. A live "This install" block rendered at the TOP, showing the current
    //      Hidden Admin Path and whether config.txt is pinning it / Dev Mode.
    //   2. The {hidden_admin_path} token inside the body, substituted with this
    //      install's actual admin path so in-prose references match reality.
    //
    // Terminology note: the official term for the renamed admin URL segment is
    // the "Hidden Admin Path". The static doc refers to it as {hidden_admin_path}
    // and we substitute the live value here.
    // ============================================================
    public static class AdminGuidelines
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            // Live install values.
            string slug = AdminSlug.Current;
            string hiddenPath = "/" + slug;
            string hiddenPathEnc = HttpUtility.HtmlEncode(hiddenPath);
            bool slugLocked = AdminSlug.IsLockedByConfig;
            bool devMode = Settings.IsDevMode;
            bool devModeLocked = Settings.IsDevModeLockedByConfig;

            // ----- top "This install" block (server-rendered, not Markdown) -----
            var top = new System.Text.StringBuilder();
            top.Append(@"
<div class='card'>
    <div class='card-header'><h2><i class='fa-solid fa-circle-info'></i> This install</h2></div>
    <div class='card-body'>
        <p style='margin-top:0'>The <strong>Hidden Admin Path</strong> is the single URL segment the admin panel and login screen live under. On this install it is currently:</p>
        <p style='font-size:18px;margin:6px 0'><code>" + hiddenPathEnc + @"</code></p>");

            if (slugLocked)
            {
                top.Append(@"
        <p class='form-hint' style='color:var(--warn,#d97706)'><i class='fa-solid fa-lock'></i> The Hidden Admin Path is pinned by <code>/App_Data/config.txt</code>, so the Admin URL field in Settings is read-only. Change it in the file and reload via <code>/reset_app</code>.</p>");
            }

            if (devMode)
            {
                string src = devModeLocked ? "pinned on by <code>/App_Data/config.txt</code>" : "enabled in Settings";
                top.Append(@"
        <p class='form-hint' style='color:var(--warn,#d97706)'><i class='fa-solid fa-triangle-exclamation'></i> <strong>Dev Mode is ON</strong> (" + src + @"). The pipeline auto-logs the first admin, so admin and API requests succeed without a login. <strong>Never leave this on for a public site.</strong></p>");
            }

            top.Append(@"
    </div>
</div>");

            // Substitute the live Hidden Admin Path into the static body.
            var repl = new Dictionary<string, string>
            {
                { "hidden_admin_path", hiddenPath }
            };

            AdminDocRenderer.Render(
                "GeneralGuidelines.md",
                "General Guidelines",
                "General Guidelines",
                "guidelines",
                repl,
                top.ToString());
        }
    }
}

using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    public class AdminTemplate
    {
        public string Title = "Admin";
        public string ActiveItem = "dashboard"; // dashboard | pages | posts | media | settings
        public string ExtraHeaderText = "";
        public string ExtraFooterText = "";
        public string PageHeading = "";
        public string PageHeadingActionsHtml = "";

        public string RenderHeader()
        {
            string brand = Settings.SiteName;
            string encodedTitle = HttpUtility.HtmlEncode(Title + " · " + brand);
            string flash = AppSession.PopFlash();
            string userDisplay = AppSession.LoginUser != null
                ? (string.IsNullOrEmpty(AppSession.LoginUser.DisplayName)
                    ? AppSession.LoginUser.Username
                    : AppSession.LoginUser.DisplayName)
                : "Guest";

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    <title>{encodedTitle}</title>
    <link rel='preconnect' href='https://fonts.googleapis.com'>
    <link rel='preconnect' href='https://fonts.gstatic.com' crossorigin>
    <link rel='stylesheet' href='https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap'>
    <link rel='stylesheet' href='/fonts/fontawesome/css/all.min.css' />
    <link rel='stylesheet' href='/css/admin.css' />
    {PublicTemplate.FaviconLinkTags()}
");
            if (!string.IsNullOrEmpty(ExtraHeaderText)) sb.AppendLine(ExtraHeaderText);

            sb.Append($@"</head>
<body>
<div class='admin-shell'>
");
            sb.Append(RenderSidebar(brand));
            sb.Append($@"    <div class='admin-main'>
        <header class='admin-topbar'>
            <button class='admin-burger' onclick='adminToggleSidebar()' aria-label='Menu'>
                <i class='fa-solid fa-bars'></i>
            </button>
            <div class='admin-topbar-title'>{HttpUtility.HtmlEncode(PageHeading)}</div>
            <div class='admin-topbar-actions'>
                {PageHeadingActionsHtml}
                <a href='/' class='admin-topbar-link' title='View site' target='_blank'><i class='fa-solid fa-up-right-from-square'></i></a>
                <div class='admin-user'>
                    <i class='fa-solid fa-circle-user'></i>
                    <span>{HttpUtility.HtmlEncode(userDisplay)}</span>
                </div>
            </div>
        </header>
");
            if (!string.IsNullOrEmpty(flash))
            {
                sb.Append($@"        <script>window.__pendingFlash = {{ ok: true, title: 'Success', message: {HttpUtility.JavaScriptStringEncode(flash, true)} }};</script>
");
            }
            sb.Append("        <main class='admin-content'>\n");
            return sb.ToString();
        }

        string RenderSidebar(string brand)
        {
            var sb = new StringBuilder();
            sb.Append($@"    <aside class='admin-sidebar' id='adminSidebar'>
        <div class='admin-brand'>
            <i class='fa-solid fa-feather'></i>
            <span>{HttpUtility.HtmlEncode(brand)}</span>
        </div>
        <nav class='admin-nav'>
            {NavItem("dashboard", "/admin", "fa-gauge-high", "Dashboard")}
            {NavItem("pages", "/admin/pages", "fa-file-lines", "Pages")}
            {NavItem("posts", "/admin/posts", "fa-pen-nib", "Posts")}
            {NavItem("categories", "/admin/categories", "fa-tags", "Categories")}
            {NavItem("media", "/admin/media", "fa-images", "Media")}
            {NavItem("themes", "/admin/themes", "fa-palette", "Themes")}
            {NavItem("nav", "/admin/nav", "fa-bars-staggered", "Navigation")}
            {NavItem("analytics", "/admin/analytics", "fa-chart-line", "Analytics")}
            {NavItem("settings", "/admin/settings", "fa-sliders", "Settings")}
            {NavItem("users", "/admin/users", "fa-users", "Users")}
            {NavItem("guidelines", "/admin/guidelines", "fa-circle-info", "General Guidelines")}
        </nav>
        <div class='admin-sidebar-footer'>
            <a href='/logout' class='admin-logout'><i class='fa-solid fa-right-from-bracket'></i> Sign out</a>
        </div>
    </aside>
    <div class='admin-sidebar-overlay' id='adminSidebarOverlay' onclick='adminToggleSidebar()'></div>
");
            return sb.ToString();
        }

        string NavItem(string key, string href, string icon, string label)
        {
            string cls = ActiveItem == key ? "admin-nav-item is-active" : "admin-nav-item";
            return $"<a class='{cls}' href='{href}'><i class='fa-solid {icon}'></i><span>{label}</span></a>";
        }

        public string RenderFooter()
        {
            var sb = new StringBuilder();
            sb.Append(@"        </main>
    </div>
</div>
<script src='/js/admin.js'></script>
");
            if (!string.IsNullOrEmpty(ExtraFooterText)) sb.AppendLine(ExtraFooterText);
            sb.Append("</body>\n</html>");
            return sb.ToString();
        }
    }
}

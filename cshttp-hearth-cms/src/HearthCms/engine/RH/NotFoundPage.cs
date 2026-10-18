using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class NotFoundPage
    {
        public static void HandleRequest()
        {
            HttpContext.Current.Response.StatusCode = 404;

            var pt = new PublicTemplate { Title = "Page not found", BodyClass = "page-error" };

            // Render the active theme's 404.html body fragment the same way the
            // home page renders home.html: build a token model, fill the body
            // template, then wrap it in the theme's _layout.html via RenderPage.
            // A theme without its own 404.html falls back to the built-in theme's
            // copy automatically (TemplateEngine.Load handles the fallback), so a
            // 404 always renders inside the active theme's shell.
            string slug = ThemeManager.GetActiveSlug();
            string siteName = Settings.SiteName;

            var bodyModel = new TemplateModel();
            bodyModel.SetText("site_name", siteName);
            string body = TemplateEngine.Render(slug, "404.html", bodyModel);

            ApiHelper.WriteHtml(pt.RenderPage(body));
            ApiHelper.EndResponse();
        }
    }
}

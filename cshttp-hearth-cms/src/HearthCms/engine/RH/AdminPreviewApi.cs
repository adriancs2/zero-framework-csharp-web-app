using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine.Markdown;
using System.engine.CsTemplate;

namespace System.engine.RH
{
    // Markdown preview pipeline (single round-trip):
    //   POST /api/admin/preview-markdown  (form: markdown)
    //     -> renders the markdown to a full themed HTML document and returns it
    //        inline as JSON: { success: true, html: "<!DOCTYPE html>..." }
    //
    // The client reads result.html on success and injects it into the preview
    // iframe via srcdoc. No token, no server-side store, no second request.
    public static class AdminPreviewApi
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            try
            {
                var req = HttpContext.Current.Request;
                string markdown = req.Form["markdown"] + "";

                string body = MarkdownToHtml.ToHtml(markdown ?? "");
                string html = BuildPreviewDocument(body);

                // Return the rendered document inline. ApiHelper serialises the
                // anonymous object to JSON; the client injects html into the
                // iframe's srcdoc on success.
                ApiHelper.WriteJson(new { success = true, html = html });
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        // Build the full HTML document string for the preview. Folder/HTML themes
        // render through the SAME pipeline a real published page uses (the theme's
        // article template wrapped in its _layout.html) so the preview inherits the
        // designer's COMPLETE <head>. C# (code) themes have no reusable HTML
        // templates, so they fall back to the self-contained wrapper. Any failure
        // also falls back to the wrapper, so the preview always shows the converted
        // HTML rather than an error page.
        static string BuildPreviewDocument(string bodyHtml)
        {
            bool isCsTheme = CsThemeRegistry.IsActiveCsTemplate && CsThemeRegistry.Active != null;
            if (!isCsTheme)
            {
                string templated = TryBuildTemplatedPreview(bodyHtml);
                if (!string.IsNullOrEmpty(templated)) return templated;
            }
            return BuildWrappedPreview(bodyHtml);
        }

        // Render the preview body through the active folder theme's real page
        // template (article-full-width.html + _layout.html). Returns null on any
        // failure so the caller can fall back to the self-contained wrapper.
        static string TryBuildTemplatedPreview(string bodyHtml)
        {
            try
            {
                var model = new DocModel
                {
                    Title = "Preview",
                    Layout = "stack",     // full-width, no "recent posts" aside
                    ShowAside = false,
                    RenderedContentHtml = bodyHtml
                };
                string page = new PublicTemplate
                {
                    Title = "Preview",
                    BodyClass = "page-doc"
                }.RenderPage(DocLayout.RenderTemplated(model));

                return string.IsNullOrEmpty(page) ? null : page;
            }
            catch
            {
                return null;
            }
        }

        // Self-contained wrapper. Mirrors PublicTemplate's <head> (active theme
        // slug and CSS) so the preview inherits the same typography and CSS
        // variables as the public site. Body/wrapper classes mirror the homepage
        // "custom page" rendering so theme rules keyed off those classes apply. We
        // deliberately skip site-header, nav-overlay, and site-footer - chrome the
        // editor doesn't need to preview.
        static string BuildWrappedPreview(string bodyHtml)
        {
            // Resolve the active theme's real stylesheet URL. A C# (code) theme
            // ships its own bundled CSS and does NOT publish a {slug}/style.css, so
            // the folder-theme convention 404s and the preview renders unstyled.
            // Use the active theme's declared CssHref in that case; fall back to the
            // folder convention for HTML themes.
            string activeThemeSlug = ThemeManager.GetActiveSlug();
            string themeHref;
            if (CsThemeRegistry.IsActiveCsTemplate && CsThemeRegistry.Active != null)
                themeHref = CsThemeRegistry.Active.CssHref;
            else
                themeHref = ThemeManager.ResolveCssPublicUrl(activeThemeSlug)
                            ?? ThemeManager.CssPublicUrl(activeThemeSlug);

            var sb = new StringBuilder();
            sb.Append(@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    <title>Preview</title>
    <link rel='stylesheet' href='/fonts/fontawesome/css/all.min.css' />
    <link rel='stylesheet' href='" + themeHref + @"' />
</head>
<body class='page-home page-doc'>
<main class='site-main'>
<article class='doc'>
    <div class='container container-narrow'>
        <div class='doc-content prose'>
");
            sb.Append(bodyHtml);
            sb.Append(@"
        </div>
    </div>
</article>
</main>
</body>
</html>");
            return sb.ToString();
        }
    }
}

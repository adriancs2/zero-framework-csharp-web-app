using System.engine;
using System.engine.RH;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.CsTemplate.Broadsheet
{
    // Shared layout helper for the Broadsheet (C#) theme: renders the document
    // head/header and footer so the page handlers only build the body. This is
    // the C# theme's equivalent of the folder theme's _layout.html, and is kept
    // structurally in lock-step with App_Data/themes/Broadsheet/_layout.html
    // (Noto Sans / Ubuntu Condensed / Oswald fonts, atom-one-dark code styling,
    // a plain text masthead brand).
    //
    // Head/footer markup uses C# 7.3 interpolated verbatim strings ($@"...").
    public class Layout
    {
        public string Title = "";
        public string MetaDescription = "";

        // Per-page Open Graph image (root-relative or absolute). Set by the post
        // handler from the post's cover image; empty for the home/listing/page
        // handlers, so the engine falls back to the site default og_image_url.
        // Mirrors PublicTemplate.OgImage on the HTML (RenderPage) path.
        public string OgImage = "";

        // Public URL of the theme's asset folder (e.g. "/assets/themes/broadsheet-cs"),
        // passed in by the handler so CSS/JS links resolve to the right slug.
        public string AssetBaseUrl = "";

        static string Enc(string v) { return HttpUtility.HtmlEncode(v ?? ""); }
        static string AttrEnc(string v) { return HttpUtility.HtmlAttributeEncode(v ?? ""); }

        public string RenderHeader()
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx != null && ctx.Request != null)
                {
                    string rawPath = ctx.Request.Path ?? "";
                    string path = rawPath.ToLowerInvariant().TrimEnd('/');
                    if (string.IsNullOrEmpty(path)) path = "/";
                    if (path == "/" || path == "/home")
                    {
                        string siteName = Settings.SiteName;
                        string tagline = (Settings.SiteTagline ?? "").Trim();
                        if (!string.IsNullOrEmpty(tagline))
                        {
                            Title = siteName + " - " + tagline;
                        }
                        else
                        {
                            Title = siteName;
                        }
                    }
                }
            }
            catch { }

            // {{head_meta}} equivalent: the page <title>, meta description,
            // the site-wide favicon, plus the full meta block (theme-color and
            // Open Graph / Twitter cards). Built by the shared, template-engine-
            // independent PublicTemplate.SiteMetaHeadTags - the same builder the
            // HTML RenderPage path uses - so favicon set, canonical/og:url and the
            // og:image fallback (per-page cover wins, else the site default
            // og_image_url) stay identical across both template systems.
            string headMeta = "<title>" + Enc(Title) + "</title>\n"
                + "    <meta name='description' content='" + Enc(MetaDescription) + "'>\n    "
                + PublicTemplate.SiteMetaHeadTags(Title, MetaDescription, OgImage);

            return $@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    {headMeta}
    <link rel='stylesheet' href='/fonts/fontawesome/css/all.min.css' />
    <link rel='preconnect' href='https://fonts.googleapis.com'>
    <link rel='preconnect' href='https://fonts.gstatic.com' crossorigin>
    <link rel='stylesheet' href='https://fonts.googleapis.com/css2?family=Noto+Sans:ital,wght@0,400;0,600;0,700;1,400&family=Ubuntu+Condensed&family=Oswald:wght@400;600&display=swap'>
    <link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/atom-one-dark.min.css'>
    <link rel='stylesheet' href='{AttrEnc(AssetBaseUrl)}/broadsheet-cs.css?v=1' />
</head>
<body>
<a class='skip-link' href='#main'>Skip to content</a>
<header class='site-header'>
    <div class='container header-inner'>
        {PublicTemplate.RenderSiteLogo()}
        <button class='nav-toggle' onclick='toggleNav()' aria-label='Menu' aria-controls='siteNav'>
            <i class='fa-solid fa-bars'></i>
        </button>
        <nav class='site-nav' id='siteNav'>
            {NavMenu.RenderPublicNav()}
        </nav>
    </div>
</header>
<div class='nav-overlay' id='navOverlay' onclick='toggleNav()'></div>
<main class='site-main' id='main'>
";
        }

        public string RenderFooter()
        {
            string footerText = HttpUtility.HtmlEncode(Settings.FooterText);
            string analyticScript = "";

            // Internal Content Analytics: only emit the tag at all when the
            // setting is on - when disabled, no script is included (not just a
            // no-op script), so nothing about analytics reaches the page.
            if (Settings.AnalyticsEnabled)
                analyticScript = "<script src='/js/analytics.js'></script>";

            return $@"
</main>
<footer class='site-footer'>
    <div class='container footer-cols'>{BuildFooterColumns()}</div>
    <div class='container footer-inner'>
        <div class='footer-meta'>{footerText}</div>
    </div>
</footer>

<script src='/js/site.js'></script>
<script src='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js'></script>
<script>setTimeout(function(){{document.querySelectorAll('pre code').forEach(function(el){{try{{hljs.highlightElement(el);}}catch(e){{}}}});}},250);</script>
{analyticScript}

</body>
</html>";
        }

        // The optional multi-column footer area, mirroring components/footer-column.html.
        // Each configured column's markdown is rendered to HTML and wrapped in a
        // .footer-col. Returns "" when disabled or every column is empty.
        static string BuildFooterColumns()
        {
            int count = Settings.FooterColCount;
            if (count < 1) return "";
            if (count > 4) count = 4;

            var sb = new StringBuilder();
            for (int i = 1; i <= count; i++)
            {
                string md = Settings.FooterColumn(i);
                if (md.Length == 0) continue;
                string html = System.engine.Markdown.MarkdownToHtml.ToHtml(md);
                sb.Append("<div class='footer-col'>").Append(html).Append("</div>");
            }
            return sb.ToString();
        }
    }
}

using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    public class PublicTemplate
    {
        string _title = "";
        public string Title
        {
            get
            {
                string siteName = Settings.SiteName;
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
                            string tagline = (Settings.SiteTagline ?? "").Trim();
                            if (!string.IsNullOrEmpty(tagline))
                            {
                                return siteName + " - " + tagline;
                            }
                            return siteName;
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(_title)) return siteName;
                if (_title.Contains(siteName)) return _title;
                return _title + " - " + siteName;
            }
            set { _title = value; }
        }

        public string Description = "";
        public string ExtraHeaderText = "";
        public string ExtraFooterText = "";
        public string BodyClass = "";

        // Per-page Open Graph image (root-relative or absolute URL). Set by
        // PostPage/PagePage from the content's cover image. When empty the site
        // default `og_image_url` is used; when that is empty too, no og:image
        // (and no og/twitter image block) is emitted.
        public string OgImage = "";

        // When set, RenderPage renders with this theme slug instead of the active
        // one (used by the admin theme preview to render any theme on demand).
        public string ForceThemeSlug = "";

        // Site-wide favicon <link> tags built from the generated icon set
        // (FaviconGenerator). Empty until the user uploads a favicon image in
        // settings, so a fresh install emits no favicon at all. Used by the
        // admin shell and folded into the full SiteMetaHeadTags() below (which
        // adds manifest, theme-color and OG/Twitter on top).
        public static string FaviconLinkTags()
        {
            if (!FaviconGenerator.HasGeneratedSet()) return "";
            return $@"<link rel='icon' type='image/png' sizes='32x32' href='{FaviconGenerator.Url32}'>
    <link rel='icon' type='image/png' sizes='16x16' href='{FaviconGenerator.Url16}'>
    <link rel='apple-touch-icon' sizes='180x180' href='{FaviconGenerator.Url180}'>
    <link rel='manifest' href='{FaviconGenerator.ManifestBustedUrl}'>
    <link rel='icon' href='{FaviconGenerator.IcoUrl}' sizes='48x48'>";
        }

        // ============================================================
        // SiteMetaHeadTags - the full <head> meta block for favicons, web
        // manifest, theme-color, Open Graph and Twitter cards.
        //
        // Favicon: if FaviconGenerator has produced a set (the .ico exists on
        // disk, which happens only after the user uploads a favicon image in
        // settings), emit the complete link set + manifest + theme-color. On a
        // fresh install with no favicon uploaded, nothing is emitted - exactly
        // as intended.
        //
        // Open Graph / Twitter: emitted only when there is something meaningful
        // to share. og:image is `ogImageOverride` (the page's cover) when set,
        // else the site default `og_image_url`; when BOTH are empty the image
        // tags are omitted entirely (no broken/blank preview image).
        //
        // `title`/`description` are passed already-decoded (raw) and encoded here.
        // `ogImageOverride` is the per-page cover (may be empty).
        // ============================================================
        public static string SiteMetaHeadTags(string title, string description, string ogImageOverride)
        {
            var sb = new StringBuilder();

            string themeColor = Settings.ThemeColor;

            // ---- Favicon set ----
            sb.Append(FaviconGenerator.HasGeneratedSet() ? FaviconLinkTags() + "\n    " : "");

            if (!string.IsNullOrEmpty(themeColor))
                sb.Append("<meta name='theme-color' content='" + HttpUtility.HtmlAttributeEncode(themeColor) + "'>\n    ");

            // ---- Open Graph / Twitter ----
            string encTitle = HttpUtility.HtmlAttributeEncode(title ?? "");
            string encDesc = HttpUtility.HtmlAttributeEncode(description ?? "");

            string ogImage = (ogImageOverride ?? "").Trim();
            if (string.IsNullOrEmpty(ogImage))
                ogImage = Settings.OgImageUrl;
            ogImage = AbsoluteUrl(ogImage);

            string ogType = string.IsNullOrEmpty(ogImageOverride) ? "website" : "article";
            string pageUrl = CurrentAbsoluteUrl();

            // Canonical URL: the clean, path-only address (CurrentAbsoluteUrl
            // excludes the query string). Because the cache serves every
            // "?utm=...", "?fbclid=..." variant from the path-keyed entry, this
            // tells search engines the single real address and stops dirty/hot-
            // linked URLs being indexed as duplicates. Matches og:url exactly.
            if (!string.IsNullOrEmpty(pageUrl))
                sb.Append("<link rel='canonical' href='" + HttpUtility.HtmlAttributeEncode(pageUrl) + "'>\n    ");

            sb.Append("<meta property='og:type' content='" + ogType + "'>\n");
            if (!string.IsNullOrEmpty(pageUrl))
                sb.Append("    <meta property='og:url' content='" + HttpUtility.HtmlAttributeEncode(pageUrl) + "'>\n");
            sb.Append("    <meta property='og:title' content='" + encTitle + "'>\n");
            sb.Append("    <meta property='og:description' content='" + encDesc + "'>\n");

            string twitterCard = string.IsNullOrEmpty(ogImage) ? "summary" : "summary_large_image";
            if (!string.IsNullOrEmpty(ogImage))
            {
                string encImg = HttpUtility.HtmlAttributeEncode(ogImage);
                sb.Append("    <meta property='og:image' content='" + encImg + "'>\n");
                sb.Append("    <meta name='twitter:image' content='" + encImg + "'>\n");
            }
            sb.Append("    <meta name='twitter:card' content='" + twitterCard + "'>\n");
            sb.Append("    <meta name='twitter:title' content='" + encTitle + "'>\n");
            sb.Append("    <meta name='twitter:description' content='" + encDesc + "'>");

            return sb.ToString();
        }

        // Turn a root-relative URL into an absolute one using the request host;
        // pass through anything already absolute (or empty).
        static string AbsoluteUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (url.IndexOf("://", StringComparison.Ordinal) >= 0 || url.StartsWith("//")) return url;
            try
            {
                var req = HttpContext.Current.Request;
                string baseUrl = req.Url.GetLeftPart(UriPartial.Authority);
                if (url[0] != '/') url = "/" + url;
                return baseUrl + url;
            }
            catch { return url; }
        }

        static string CurrentAbsoluteUrl()
        {
            try
            {
                var req = HttpContext.Current.Request;
                return req.Url.GetLeftPart(UriPartial.Authority) + req.Url.AbsolutePath;
            }
            catch { return ""; }
        }

        // ============================================================
        // RenderSiteLogo - markup for the {{site_logo}} token / brand area.
        //
        // Controlled by two settings:
        //   logo_mode : "text" | "image" | "image_text"   (default "text")
        //   logo_url  : uploaded logo image (used by image / image_text)
        //
        // Always returns an <a href='/'> brand link so themes can drop
        // {{site_logo}} straight into their header. Falls back to text when an
        // image mode is selected but no logo image has been uploaded.
        // ============================================================
        public static string RenderSiteLogo()
        {
            string siteName = Settings.SiteName;
            string mode = Settings.LogoMode;
            string logoUrl = Settings.LogoUrl;

            string encName = HttpUtility.HtmlEncode(siteName);
            bool haveImg = !string.IsNullOrEmpty(logoUrl);
            string encImg = haveImg ? HttpUtility.HtmlAttributeEncode(logoUrl) : "";

            // If an image mode is requested but no image exists, degrade to text.
            if ((mode == "image" || mode == "image_text") && !haveImg) mode = "text";

            string inner;
            if (mode == "image")
            {
                inner = "<img class='site-logo-img' src='" + encImg + "' alt='" + encName + "' />";
            }
            else if (mode == "image_text")
            {
                inner = "<img class='site-logo-img' src='" + encImg + "' alt='' />"
                      + "<span class='site-logo-text'>" + encName + "</span>";
            }
            else
            {
                inner = "<span class='site-logo-text'>" + encName + "</span>";
            }

            return "<a href='/' class='site-brand' aria-label='" + encName + "'>" + inner + "</a>";
        }

        // ============================================================
        // RenderPage - the template-driven render path for every public page.
        // Gathers the site-level layout values (title/description/meta, site
        // name, logo, nav, footer) and injects them into the active theme's
        // external _layout.html. `bodyHtml` is the page-specific content that
        // drops into {{body}} (already fully rendered & escaped by the caller).
        // ============================================================
        public string RenderPage(string bodyHtml)
        {
            string siteName = Settings.SiteName;
            string siteTagline = Settings.SiteTagline;
            string description = string.IsNullOrEmpty(Description)
                ? Db.GetSetting("site_description", siteTagline) : Description;

            string activeThemeSlug = string.IsNullOrEmpty(ForceThemeSlug)
                ? ThemeManager.GetActiveSlug() : ForceThemeSlug;
            string footerText = Db.GetSetting("footer_text", "© " + DateTime.Now.Year + " Modern CMS");

            // The <head>'s page-level meta is built in C# and injected as one
            // {{head_meta}} block - the template no longer carries {{title}}/{{description}}
            // tokens. Values are HTML-encoded inside the builders since this is
            // emitted raw. SiteMetaHeadTags adds favicon set, manifest, theme-color
            // and Open Graph / Twitter cards (per-page cover wins for og:image).
            string header = "<title>" + HttpUtility.HtmlEncode(Title) + "</title>\n"
                + "    <meta name='description' content='" + HttpUtility.HtmlEncode(description) + "'>\n    "
                + SiteMetaHeadTags(Title, description, OgImage);

            // Internal Content Analytics: only emit the tag at all when the
            // setting is on - when disabled, no script is included (not just a
            // no-op script), so nothing about analytics reaches the page.
            if (Settings.AnalyticsEnabled)
                header += "\n    <script src='/js/analytics.js'></script>";

            var m = new TemplateModel();
            m.SetRaw("head_meta", header);
            m.SetText("site_name", siteName);
            m.SetRaw("site_logo", RenderSiteLogo());
            m.SetRaw("nav_items", NavMenu.RenderPublicNav());
            m.SetText("footer_text", footerText);
            m.SetRaw("footer_column_list", BuildFooterColumns(activeThemeSlug));
            m.SetRaw("body", bodyHtml);

            return TemplateEngine.Render(activeThemeSlug, "_layout.html", m);
        }

        // Builds the optional multi-column footer area. C# owns NO markup here:
        // it loads the active theme's components/footer-column.html and, for each
        // configured column, substitutes that column's markdown (rendered to HTML
        // with raw-HTML passthrough) into the component's {{footer_content}} token,
        // then joins them. The per-column element and the surrounding grid live
        // entirely in the theme (the component + the {{footer_column_list}} container
        // in _layout.html). `footer_col_count` (0..4) sets how many columns.
        // Returns "" when disabled or every column is empty (block-or-nothing).
        static string BuildFooterColumns(string themeSlug)
        {
            int count = Settings.FooterColCount;
            if (count < 1) return "";
            if (count > 4) count = 4;

            string columnTemplate = TemplateEngine.Load(themeSlug, "components/footer-column.html");

            var sb = new StringBuilder();
            bool any = false;
            for (int i = 1; i <= count; i++)
            {
                string md = Settings.FooterColumn(i);
                if (md.Length == 0) continue;
                string html = System.engine.Markdown.MarkdownToHtml.ToHtml(md);
                sb.Append(columnTemplate.Replace("{{footer_content}}", html));
                any = true;
            }
            return any ? sb.ToString() : "";
        }
    }
}
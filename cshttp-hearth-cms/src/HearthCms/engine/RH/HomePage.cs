using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using CsHttp.Web;
using System.engine.Markdown;

namespace System.engine.RH
{
    public static class HomePage
    {
        public static void HandleRequest()
        {
            // The admin can repoint the main home page ("/" and "/home") at one of
            // several built-in layouts. Each target still keeps its own hard-coded
            // route (e.g. /latest-post); selecting it here simply ALSO renders it at
            // the site root. The delegated handlers call PublicPageCache.WriteAndCache
            // with no explicit path, so they cache under the current request path
            // ("/" or "/home") - never clobbering their standalone cache entries.
            switch (Settings.HomePageMode)
            {
                case "1": // A specific published Page
                    if (TryRenderSelectedPage()) return;
                    // Fall through to the default home page if the selection is missing/unpublished.
                    break;
                case "2": // Latest posts (flat list)  -> /latest-post
                    LatestPostPage.HandleRequest(); return;
                case "3": // Categories + latest post   -> /categories-latest-post
                    CategoriesLatestPostPage.HandleRequest(); return;
            }

            var pt = new PublicTemplate { Title = "", BodyClass = "page-home" };

            string slug = ThemeManager.GetActiveSlug();
            string siteName = Settings.SiteName;
            string siteTagline = Settings.SiteTagline;
            string siteDesc = Settings.SiteDescription;

            // How many recent posts to feature on the home page (admin-tunable).
            int homeCount = Settings.HomePostCount;
            if (homeCount < 1) homeCount = 1;
            if (homeCount > 50) homeCount = 50;

            // Recent posts
            List<obPost> recent = new List<obPost>();
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var qp = new Dictionary<string, object> { { "@n", homeCount } };
                        recent = s.GetObjectList<obPost>(
                            "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @n;", qp);
                    }
                }
            }
            catch { }

            // Home page treats the "Latest writing" block as optional: when there
            // are no posts the whole section is dropped (empty string), so the
            // template stays logic-free and never shows a heading with no cards.
            string latestSection = "";
            if (recent.Count > 0)
            {
                latestSection = TemplateEngine.Render(slug, "components/section-latest-posts.html",
                    new TemplateModel().SetRaw("post_card_list", RenderPostCards(slug, recent)));
            }

            var bodyModel = new TemplateModel();
            bodyModel.SetText("site_name", siteName);
            bodyModel.SetText("site_tagline", siteTagline);
            bodyModel.SetText("site_description", siteDesc);
            bodyModel.SetRaw("latest_posts_block", latestSection);
            string body = TemplateEngine.Render(slug, "home.html", bodyModel);
            // Apply the theme's saved home-content overrides (data-edit regions).
            body = ThemeManager.ApplyHomeEdits(body, ThemeManager.ReadHomeValues(slug));

            PublicPageCache.WriteAndCache(pt.RenderPage(body));
        }

        // Renders a list of posts into the theme's post-card component, one card
        // per post, joined into a single HTML block for {{post_card_list}}.
        // Each per-card value is escaped by TemplateModel (SetText/SetAttr); the
        // joined block is trusted HTML that the caller injects via SetRaw.
        static string RenderPostCards(string slug, List<obPost> posts)
        {
            string cardTpl = TemplateEngine.Load(slug, "components/post-card.html");
            var sb = new StringBuilder();
            foreach (var p in posts)
            {
                string excerpt = string.IsNullOrEmpty(p.Excerpt)
                    ? ToPlainText(p.Content, 160) : p.Excerpt;
                var m = new TemplateModel();
                m.SetAttr("post_url", "/" + p.Slug);
                m.SetRaw("post_pinned_class", p.IsPinned == 1 ? " is-pinned" : "");
                m.SetRaw("post_pinned", p.IsPinned == 1
                    ? "<span class='post-pinned'><i class='fa-solid fa-thumbtack'></i> Pinned</span>"
                    : "");
                m.SetText("post_title", p.Title);
                m.SetText("post_excerpt", excerpt);
                m.SetText("post_date", DateDisplay.Format(p.DatePublished));
                sb.Append(m.Render(cardTpl));
            }
            return sb.ToString();
        }

        // Renders the admin-selected published page as the home page.
        // Returns false if no valid published page is configured, so the caller
        // can fall back to the default home page.
        static bool TryRenderSelectedPage()
        {
            int pageId = Settings.HomePageId;
            if (pageId <= 0) return false;

            obPage page = null;
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var p = new Dictionary<string, object> { { "@id", pageId } };
                        page = s.GetObject<obPage>(
                            "SELECT * FROM pages WHERE id=@id AND is_published=1 AND is_deleted=0 LIMIT 1;", p);
                    }
                }
            }
            catch { }

            if (page == null || page.Id == 0) return false;

            // Title intentionally left blank: when a custom page is the home page
            // (served at "/" or "/home"), the <title> should be just the site
            // name, not "Page Title - Site Name".
            var pt = new PublicTemplate
            {
                Title = "",
                Description = string.IsNullOrEmpty(page.Excerpt) ? "" : page.Excerpt,
                BodyClass = "page-home page-doc"
            };

            string renderedContent = string.Equals(page.ContentFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                ? MarkdownToHtml.ToHtml(page.Content ?? "")
                : (page.Content ?? "");

            string body = $@"
<article class='doc'>
    <div class='container container-narrow'>
        <header class='doc-header'>
            <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>
        </header>
        <div class='doc-content prose'>
            {renderedContent}
        </div>
    </div>
</article>
";
            // Render through the template-driven path (same as the default home
            // and all other public pages) so the custom home page gets the active
            // theme's _layout.html shell - including {{site_logo}} and the full
            // head meta (favicon, theme-color, Open Graph / Twitter cards).
            PublicPageCache.WriteAndCache(pt.RenderPage(body));
            return true;
        }

        public static string StripHtml(string html, int maxLength)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) sb.Append(c);
            }
            string plain = HttpUtility.HtmlDecode(sb.ToString()).Trim();
            // collapse whitespace
            var sb2 = new StringBuilder();
            bool lastSpace = false;
            foreach (char c in plain)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace) sb2.Append(' ');
                    lastSpace = true;
                }
                else { sb2.Append(c); lastSpace = false; }
            }
            string result = sb2.ToString();
            if (result.Length > maxLength) result = result.Substring(0, maxLength).TrimEnd() + "…";
            return result;
        }

        // Removes common Markdown syntax so a value can be treated as plain text.
        // Used both to sanitize the (plain-text) excerpt field on save and to
        // derive a clean excerpt from Markdown post content when no excerpt is set.
        public static string StripMarkdown(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            string s = md;
            s = Regex.Replace(s, @"!\[([^\]]*)\]\([^)]*\)", "$1");      // images -> alt text
            s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");       // links  -> link text
            s = Regex.Replace(s, @"`{1,3}", "");                         // code fences / inline backticks
            s = Regex.Replace(s, @"(?m)^\s{0,3}#{1,6}\s*", "");          // ATX headings
            s = Regex.Replace(s, @"(?m)^\s{0,3}>\s?", "");               // blockquotes
            s = Regex.Replace(s, @"(?m)^\s{0,3}([-*+]|\d+\.)\s+", "");   // list markers
            s = Regex.Replace(s, @"(\*\*|__|\*|_|~~)", "");              // bold / italic / strike
            return s;
        }

        // Single-line plain text from Markdown or HTML: strips Markdown, then
        // tags, decodes entities and collapses whitespace (via StripHtml).
        // maxLength caps the result; pass int.MaxValue for no truncation.
        public static string ToPlainText(string raw, int maxLength)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return StripHtml(StripMarkdown(raw), maxLength);
        }
    }
}
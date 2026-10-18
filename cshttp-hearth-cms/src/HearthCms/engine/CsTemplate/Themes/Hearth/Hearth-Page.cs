using System;
using System.Collections.Generic;
using System.engine.Markdown;
using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine;
using System.engine.RH;

namespace System.engine.CsTemplate.Hearth
{
    public partial class Hearth : CsTemplate
    {
        // ----- single page : article, full width, no aside -----
        public override bool HandlePage(string slug)
        {
            // Data via the CsTemplate helper - no direct SQLite in theme code.
            obPage page = GetPageBySlug(slug);
            if (page == null) return false;

            string renderedContent = string.Equals(page.ContentFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                ? MarkdownToHtml.ToHtml(page.Content ?? "")
                : (page.Content ?? "");

            var model = new DocModel
            {
                Title = page.Title,
                Layout = "stack",      // pages render full width
                ShowAside = false,
                RenderedContentHtml = renderedContent,
                Breadcrumbs = new List<DocCrumb> { new DocCrumb("Home", "/"), new DocCrumb(page.Title) }
            };

            var layout = NewLayout(page.Title);
            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());
            sb.Append(ArticleHtml(model));
            sb.Append(layout.RenderFooter());
            WriteCached(sb.ToString());
            return true;
        }

        // ============================================================
        // ArticleHtml - renders a single page/post directly in C#, kept
        // structurally in lock-step with the folder theme's article
        // templates:
        //   stack -> article-full-width.html
        //   split -> article-sidebar.html
        // The block-or-nothing tokens (breadcrumbs, dates, author, cover,
        // aside) render a complete element when there is data, or empty.
        // ============================================================
        string ArticleHtml(DocModel m, int postId = 0)
        {
            string breadcrumbs = BuildBreadcrumbs(m.Breadcrumbs);
            string publishedDate = BuildDateMeta(m.PublishedDate);
            string updatedDate = BuildDateUpdate(m.UpdatedDate);
            string author = BuildAuthor(m.Author);
            string coverImage = BuildCoverImage(m.CoverImage);
            // Shared builder: inner content (+ the View Content / View Markdown
            // toggle for a post when the setting is on). The .doc-content.prose
            // wrapper in the template strings below is this theme's, matching the
            // HTML themes' article-*.html so every theme renders one structure.
            string content = ArticleTools.BuildContentArea(m.RenderedContentHtml, postId);
            bool hasAside = m.ShowAside && m.Recent != null && m.Recent.Count > 0;
            string aside = hasAside ? BuildAside(m) : "";

            if (m.Layout == "stack")
            {
                // article-full-width.html
                return string.Format(@"
<article class='doc doc-layout layout-stack'>
    <div class='container container-narrow'>
        <header class='doc-header'>
            {0}
            <h1>{1}</h1>
            <div class='doc-meta-row'>
                {2}
                {3}
                {4}
            </div>
        </header>
        {5}
        <div class='doc-content prose'>
            {6}
        </div>
    </div>
    <div class='container'>
        {7}
    </div>
</article>", breadcrumbs, H(m.Title), publishedDate, updatedDate, author, coverImage, content, aside);
            }

            // article-sidebar.html
            return string.Format(@"
<article class='doc doc-layout layout-split'>
    <div class='container'>
        <div class='doc-grid'>
            <div class='doc-main'>
                <header class='doc-header'>
                    {0}
                    <h1>{1}</h1>
                    <div class='doc-meta-row'>
                        {2}
                        {3}
                        {4}
                    </div>
                </header>
                {5}
                <div class='doc-content prose'>
                    {6}
                </div>
            </div>
            {7}
        </div>
    </div>
</article>", breadcrumbs, H(m.Title), publishedDate, updatedDate, author, coverImage, content, aside);
        }

        // Breadcrumb nav. Empty when there are no crumbs.
        string BuildBreadcrumbs(List<DocCrumb> crumbs)
        {
            if (crumbs == null || crumbs.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append("<nav class='breadcrumbs' aria-label='Breadcrumb'>");
            for (int i = 0; i < crumbs.Count; i++)
            {
                var c = crumbs[i];
                if (i > 0) sb.Append("<span class='sep'>/</span>");
                if (!string.IsNullOrEmpty(c.Href))
                    sb.Append("<a href='" + Attr(c.Href) + "'>" + H(c.Label) + "</a>");
                else
                    sb.Append("<span class='crumb-current'>" + H(c.Label) + "</span>");
            }
            sb.Append("</nav>");
            return sb.ToString();
        }

        // Published-date line, or empty (pages have none).
        string BuildDateMeta(DateTime? date)
        {
            if (!date.HasValue) return "";
            return "<div class='doc-meta'><i class='fa-regular fa-calendar'></i> " + DateDisplay.Format(date.Value) + "</div>";
        }

        // "Updated ..." line, or empty when no modified date.
        string BuildDateUpdate(DateTime? date)
        {
            if (!date.HasValue) return "";
            return "<div class='doc-meta doc-meta-update'><i class='fa-regular fa-pen-to-square'></i> Updated " + DateDisplay.Format(date.Value) + "</div>";
        }

        // Author line, or empty when the author has no display name.
        string BuildAuthor(string author)
        {
            if (string.IsNullOrEmpty(author)) return "";
            return "<div class='doc-meta doc-meta-author'><i class='fa-regular fa-user'></i> " + H(author) + "</div>";
        }

        // Cover-image element (components/cover-image.html), or empty when none.
        string BuildCoverImage(string coverUrl)
        {
            if (string.IsNullOrEmpty(coverUrl)) return "";
            return "<div class='content-cover-image'>\n    <img src='" + Attr(coverUrl) + "' alt='' />\n</div>";
        }

        // The "keep reading" recent-posts aside, or empty.
        string BuildAside(DocModel m)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(@"<aside class='doc-aside'>
                <h2 class='aside-heading'>{0}</h2>
                <div class='reclist'>", H(m.AsideHeading)));

            foreach (var r in m.Recent)
            {
                string thumbStyle = string.IsNullOrEmpty(r.ImageUrl)
                    ? ""
                    : " style='background-image:url(&quot;" + Attr(r.ImageUrl) + "&quot;)'";

                sb.Append(string.Format(@"
                    <article class='rec-item'>
                        <a class='rec-link' href='{0}'>
                            <span class='rec-thumb'{1}></span>
                            <span class='rec-body'>
                                <span class='rec-title'>{2}</span>
                                <span class='rec-date'><i class='fa-regular fa-calendar'></i> {3}</span>
                            </span>
                        </a>
                    </article>", Attr(r.Href), thumbStyle, H(r.Title), DateDisplay.Format(r.Date)));
            }

            sb.Append(@"
                </div>
            </aside>");
            return sb.ToString();
        }
    }
}

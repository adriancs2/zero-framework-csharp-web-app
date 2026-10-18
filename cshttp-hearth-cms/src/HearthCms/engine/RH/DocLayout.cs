using System.Collections.Generic;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // DocLayout - the ONE universal document renderer.
    //
    // Both "page/post layout 1" (left/right, split) and
    // "page/post layout 2" (top/bottom, stack) are produced by
    // THIS SINGLE method. The markup is identical for both; only
    // the layout class on the root <article> differs. CSS in
    // _base.css does all the rearranging from there.
    //
    //   layout = "split"  -> main content + right sidebar
    //   layout = "stack"  -> full-width content, recent posts below
    //
    // Pages pass ShowAside=false (no "recent posts"); posts pass
    // ShowAside=true. Default layout is decided by the caller
    // (post -> split, page -> stack) until a per-article [layout]
    // column exists.
    // ============================================================

    public static class DocLayout
    {
        // ============================================================
        // RenderTemplated - the external-template path. Picks the active
        // theme's article layout file by the resolved layout:
        //   stack -> article-full-width.html
        //   split -> article-sidebar.html
        // and fills its {{tokens}}. The layout is the real axis (a page or
        // a post can use either); the aside is a slot C# fills with the
        // recent-posts block or leaves empty (block-or-nothing in C#).
        // ============================================================
        public static string RenderTemplated(DocModel m)
        {
            string slug = ThemeManager.GetActiveSlug();
            bool hasAside = m.ShowAside && m.Recent != null && m.Recent.Count > 0;
            string templateName = (m.Layout == "stack")
                ? "article-full-width.html"
                : "article-sidebar.html";

            var model = new TemplateModel();
            model.SetText("article_title", m.Title);
            model.SetRaw("breadcrumbs", BuildBreadcrumbs(m.Breadcrumbs));
            model.SetRaw("published_date", BuildDateMeta(m.PublishedDate));
            model.SetRaw("updated_date", BuildDateUpdate(m.UpdatedDate));
            model.SetRaw("article_author", BuildAuthor(m.Author));
            // Cover image is its own component (components/cover-image.html) so
            // both article layouts share a single definition. Rendered only when
            // there is a cover; pages without one get nothing (the {{cover_image}}
            // token stays empty), so the component needs no "empty" modifier.
            if (!string.IsNullOrEmpty(m.CoverImage))
            {
                var coverModel = new TemplateModel();
                coverModel.SetAttr("article_cover", m.CoverImage);
                model.SetRaw("cover_image", TemplateEngine.Render(slug, "components/cover-image.html", coverModel));
            }
            // Inner content area. ArticleTools owns the "View Content / View
            // Markdown" toggle (post + setting on); the .doc-content.prose wrapper
            // stays in the theme's article-*.html, so no template needs editing.
            model.SetRaw("article_content", ArticleTools.BuildContentArea(m.RenderedContentHtml, m.PostId));
            model.SetRaw("article_aside", hasAside ? BuildAside(m) : "");

            return TemplateEngine.Render(slug, templateName, model);
        }

        static string BuildBreadcrumbs(List<DocCrumb> crumbs)
        {
            if (crumbs == null || crumbs.Count == 0) return "";
            var sb = new StringBuilder();
            sb.Append("<nav class='breadcrumbs' aria-label='Breadcrumb'>");
            for (int i = 0; i < crumbs.Count; i++)
            {
                var c = crumbs[i];
                if (i > 0) sb.Append("<span class='sep'>/</span>");
                if (!string.IsNullOrEmpty(c.Href))
                    sb.Append($"<a href='{HttpUtility.HtmlAttributeEncode(c.Href)}'>{HttpUtility.HtmlEncode(c.Label)}</a>");
                else
                    sb.Append($"<span class='crumb-current'>{HttpUtility.HtmlEncode(c.Label)}</span>");
            }
            sb.Append("</nav>");
            return sb.ToString();
        }

        static string BuildDateMeta(DateTime? date)
        {
            if (!date.HasValue) return "";
            return $"<div class='doc-meta'><i class='fa-regular fa-calendar'></i> {DateDisplay.Format(date.Value)}</div>";
        }

        // Optional "last updated" line. Block-or-nothing: empty when no date.
        static string BuildDateUpdate(DateTime? date)
        {
            if (!date.HasValue) return "";
            return $"<div class='doc-meta doc-meta-update'><i class='fa-regular fa-pen-to-square'></i> Updated {DateDisplay.Format(date.Value)}</div>";
        }

        // Optional author line. Block-or-nothing: empty when no author name.
        static string BuildAuthor(string author)
        {
            if (string.IsNullOrEmpty(author)) return "";
            return $"<div class='doc-meta doc-meta-author'><i class='fa-regular fa-user'></i> {HttpUtility.HtmlEncode(author)}</div>";
        }

        // Recent-posts aside ("more in {category}" / "keep reading"). Built inline
        // in C#, exactly like the breadcrumb/date/author helpers above - NOT from
        // component templates. The folder themes ship no aside component (there is
        // no components/article-aside.html or components/rec-item.html in any theme),
        // so the previous template-based build silently produced an empty string and
        // the sidebar never appeared. Their CSS, however, already styles this exact
        // class structure (.doc-aside/.aside-heading/.reclist/.rec-item/...), which
        // is the same markup the C# themes emit inline - so rendering it here makes
        // the sidebar appear and look identical across both template systems.
        static string BuildAside(DocModel m)
        {
            var sb = new StringBuilder();
            sb.Append("<aside class='doc-aside'>");
            sb.Append("<h2 class='aside-heading'>").Append(HttpUtility.HtmlEncode(m.AsideHeading)).Append("</h2>");
            sb.Append("<div class='reclist'>");

            foreach (var r in m.Recent)
            {
                string thumbImg = string.IsNullOrEmpty(r.ImageUrl)
                    ? ""
                    : "<img src='" + HttpUtility.HtmlAttributeEncode(ImageThumb.DisplayUrl(r.ImageUrl)) + "' alt='' />";

                sb.Append("<article class='rec-item'>");
                sb.Append("<a class='rec-link' href='").Append(HttpUtility.HtmlAttributeEncode(r.Href)).Append("'>");
                sb.Append("<span class='rec-thumb'>").Append(thumbImg).Append("</span>");
                sb.Append("<span class='rec-body'>");
                sb.Append("<span class='rec-title'>").Append(HttpUtility.HtmlEncode(r.Title)).Append("</span>");
                sb.Append("<span class='rec-date'><i class='fa-regular fa-calendar'></i> ")
                  .Append(HttpUtility.HtmlEncode(DateDisplay.Format(r.Date))).Append("</span>");
                sb.Append("</span>");
                sb.Append("</a>");
                sb.Append("</article>");
            }

            sb.Append("</div>");
            sb.Append("</aside>");
            return sb.ToString();
        }
    }
}

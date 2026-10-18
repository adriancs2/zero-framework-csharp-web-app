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
        // ----- single post : article (split = sidebar, stack = full width) -----
        public override bool HandlePost(string slug)
        {
            // Data via the CsTemplate helpers - no direct SQLite in theme code.
            obPost post = GetPostBySlug(slug);
            if (post == null) return false;

            string authorName = GetUserDisplayName(post.AuthorId);

            int asideLimit = GetCountSetting("article_sidebar_post_count");
            List<obPost> related = GetRelatedPosts(post.Id, post.CategoryId, asideLimit);

            string layoutMode = (post.Layout == "stack" || post.Layout == "split") ? post.Layout : "split";
            string qLayout = (HttpContext.Current.Request.QueryString["layout"] + "").Trim().ToLowerInvariant();
            if (qLayout == "stack" || qLayout == "split") layoutMode = qLayout;

            string renderedContent = string.Equals(post.ContentFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                ? MarkdownToHtml.ToHtml(post.Content ?? "")
                : (post.Content ?? "");

            var crumbs = new List<DocCrumb>
            {
                new DocCrumb("Home", "/")
            };
            string asideHeading = "Keep reading";
            if (post.CategoryId > 0)
            {
                var cat = CategoryManager.GetById(post.CategoryId);
                if (cat != null && cat.Id > 0)
                {
                    crumbs.Add(new DocCrumb(cat.Name, "/category/" + cat.Slug));
                    asideHeading = "More in " + cat.Name;
                }
            }
            crumbs.Add(new DocCrumb(post.Title));

            var model = new DocModel
            {
                Title = post.Title,
                Layout = layoutMode,
                ShowAside = true,
                CoverImage = post.CoverImage,
                RenderedContentHtml = renderedContent,
                PublishedDate = post.DatePublished,
                UpdatedDate = post.DateModified,
                Author = authorName,
                AsideHeading = asideHeading,
                Breadcrumbs = crumbs
            };
            foreach (var r in related)
            {
                model.Recent.Add(new DocRecentItem
                {
                    Title = r.Title,
                    Href = "/" + r.Slug,
                    ImageUrl = r.CoverImage,
                    Date = r.DatePublished
                });
            }

            var layout = NewLayout(post.Title);
            // Per-post social card: cover image wins for og:image, excerpt as the
            // description - mirrors PostPage (HTML path) setting PublicTemplate.OgImage.
            layout.OgImage = post.CoverImage;
            if (!string.IsNullOrEmpty(post.Excerpt)) layout.MetaDescription = post.Excerpt;
            var sb = new StringBuilder();
            sb.Append(layout.RenderHeader());
            sb.Append(ArticleHtml(model, post.Id));   // direct-C# article markup (mirrors article-*.html)
            sb.Append(layout.RenderFooter());
            WriteCached(sb.ToString());
            return true;
        }
    }
}

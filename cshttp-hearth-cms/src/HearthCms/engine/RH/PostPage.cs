using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;
using System.engine.Markdown;

namespace System.engine.RH
{
    public static class PostPage
    {
        // Back-compat entry point: resolves the post or emits a 404 on miss.
        public static void HandleRequest(string slug)
        {
            if (!TryHandleRequest(slug)) NotFoundPage.HandleRequest();
        }

        // Resolves and renders the post for `slug`. Returns false WITHOUT
        // writing any response if no published post matches, so the caller can
        // fall back to 404 (after pages have already been tried).
        public static bool TryHandleRequest(string slug)
        {
            obPost post = null;
            List<obPost> related = new List<obPost>();
            string authorName = "";
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var p = new Dictionary<string, object> { { "@s", slug } };
                        post = s.GetObject<obPost>(
                            "SELECT * FROM posts WHERE slug=@s AND is_published=1 AND is_deleted=0 LIMIT 1;", p);

                        if (post != null && post.Id > 0)
                        {
                            // Resolve the author's display name (empty if none).
                            if (post.AuthorId > 0)
                            {
                                var pa = new Dictionary<string, object> { { "@id", post.AuthorId } };
                                var u = s.GetObject<obUser>("SELECT * FROM users WHERE id=@id LIMIT 1;", pa);
                                if (u != null) authorName = u.DisplayName;
                            }

                            // "Recent posts" aside count is admin-tunable.
                            int asideLimit = HomeLayoutShared.CountFor("article_sidebar_post_count");

                            // "Recent posts" = same category when the post has one,
                            // otherwise the latest other posts.
                            if (post.CategoryId > 0)
                            {
                                var p2 = new Dictionary<string, object>
                                    { { "@id", post.Id }, { "@cat", post.CategoryId }, { "@lim", asideLimit } };
                                related = s.GetObjectList<obPost>(
                                    "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 AND id<>@id AND category_id=@cat ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p2);
                            }
                            if (related.Count == 0)
                            {
                                var p2 = new Dictionary<string, object> { { "@id", post.Id }, { "@lim", asideLimit } };
                                related = s.GetObjectList<obPost>(
                                    "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 AND id<>@id ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p2);
                            }
                        }
                    }
                }
            }
            catch { }

            if (post == null || post.Id == 0)
            {
                return false;
            }

            // Layout: the post's saved choice (Phase 4), defaulting to "split".
            // A ?layout= query still overrides for live preview.
            string layout = (post.Layout == "stack" || post.Layout == "split") ? post.Layout : "split";
            string q = (HttpContext.Current.Request.QueryString["layout"] + "").Trim().ToLowerInvariant();
            if (q == "stack" || q == "split") layout = q;

            string renderedContent = string.Equals(post.ContentFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                ? MarkdownToHtml.ToHtml(post.Content ?? "")
                : (post.Content ?? "");

            // Breadcrumbs: Home / Latest Post / [Category] / Title.
            // The category links to /category/{slug} (Phase 3 route).
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
                PostId = post.Id,                // enables the markdown toggle (posts only)
                Layout = layout,
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

            var pt = new PublicTemplate
            {
                Title = post.Title,
                Description = string.IsNullOrEmpty(post.Excerpt) ? "" : post.Excerpt,
                OgImage = post.CoverImage,
                BodyClass = "page-post"
            };

            PublicPageCache.WriteAndCache(pt.RenderPage(DocLayout.RenderTemplated(model)));
            return true;
        }
    }
}

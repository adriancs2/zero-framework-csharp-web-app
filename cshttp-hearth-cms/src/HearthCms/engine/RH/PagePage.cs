using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;
using System.engine.Markdown;

namespace System.engine.RH
{
    public static class PagePage
    {
        // Back-compat entry point: resolves the page or emits a 404 on miss.
        public static void HandleRequest(string slug)
        {
            if (!TryHandleRequest(slug)) NotFoundPage.HandleRequest();
        }

        // Resolves and renders the page for `slug`. Returns false WITHOUT
        // writing any response if no published page matches, so the caller can
        // try another content type (e.g. posts) before falling back to 404.
        public static bool TryHandleRequest(string slug)
        {
            obPage page = null;
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
                        page = s.GetObject<obPage>(
                            "SELECT * FROM pages WHERE slug=@s AND is_published=1 AND is_deleted=0 LIMIT 1;", p);

                        // Resolve the author's display name (empty if none).
                        if (page != null && page.Id > 0 && page.AuthorId > 0)
                        {
                            var pa = new Dictionary<string, object> { { "@id", page.AuthorId } };
                            var u = s.GetObject<obUser>("SELECT * FROM users WHERE id=@id LIMIT 1;", pa);
                            if (u != null) authorName = u.DisplayName;
                        }
                    }
                }
            }
            catch { }

            if (page == null || page.Id == 0)
            {
                return false;
            }

            // Layout: the page's saved choice (Phase 4), defaulting to "stack".
            // Pages never show a "recent posts" aside. ?layout= overrides for preview.
            string layout = (page.Layout == "stack" || page.Layout == "split") ? page.Layout : "stack";
            string q = (HttpContext.Current.Request.QueryString["layout"] + "").Trim().ToLowerInvariant();
            if (q == "stack" || q == "split") layout = q;

            string renderedContent = string.Equals(page.ContentFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                ? MarkdownToHtml.ToHtml(page.Content ?? "")
                : (page.Content ?? "");

            var model = new DocModel
            {
                Title = page.Title,
                Layout = layout,
                ShowAside = false,               // pages exclude "recent posts"
                CoverImage = "",                 // obPage has no cover image
                RenderedContentHtml = renderedContent,
                PublishedDate = null,            // pages don't show a published date
                UpdatedDate = page.DateModified, // but they can show "last updated"
                Author = authorName,
                Breadcrumbs = new List<DocCrumb>
                {
                    new DocCrumb("Home", "/"),
                    new DocCrumb(page.Title)
                }
            };

            var pt = new PublicTemplate
            {
                Title = page.Title,
                Description = string.IsNullOrEmpty(page.Excerpt) ? "" : page.Excerpt,
                BodyClass = "page-doc"
            };

            PublicPageCache.WriteAndCache(pt.RenderPage(DocLayout.RenderTemplated(model)));
            return true;
        }
    }
}

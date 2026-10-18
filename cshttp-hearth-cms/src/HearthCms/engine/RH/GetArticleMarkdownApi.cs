using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // Public, no-auth endpoint returning a single published post's markdown source
    // as text/plain. Used by the Broadsheet theme's "View Markdown" toggle, which
    // fetches it lazily into a textarea.
    //
    //   GET /api/get-article-markdown?id=123
    //
    // Source selection:
    //   - markdown-format posts return their stored source verbatim;
    //   - html-format posts are converted to Markdown on the fly via HtmlToMarkdown.
    //
    // The result is cached in the PublicPageCache RAM tier under the key
    // "[##MARKDOWN##]{id}" and honors the cache_ram_enabled setting (no caching
    // when RAM cache is off). Only published, non-deleted posts are exposed;
    // anything missing or invalid returns 404.
    public static class GetArticleMarkdownApi
    {
        public static void HandleRequest()
        {
            var ctx = HttpContext.Current;
            var resp = ctx.Response;
            resp.ContentType = "text/plain; charset=utf-8";

            int id = 0;
            int.TryParse((ctx.Request.QueryString["id"] + "").Trim(), out id);
            if (id <= 0)
            {
                resp.StatusCode = 404;
                resp.Write("Article not found.");
                ApiHelper.EndResponse();
                return;
            }

            // Cache hit (RAM tier; the lookup is a no-op when cache_ram_enabled = false).
            string cached = PublicPageCache.TryGetMarkdown(id);
            if (cached != null)
            {
                resp.AddHeader("X-Cache", "HIT");
                resp.Write(cached);
                ApiHelper.EndResponse();
                return;
            }

            string markdown = null;
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var p = new Dictionary<string, object> { { "@id", id } };
                        var post = s.GetObject<obPost>(
                            "SELECT * FROM posts WHERE id=@id AND is_published=1 AND is_deleted=0 LIMIT 1;", p);
                        if (post != null && post.Id > 0)
                        {
                            string src = post.Content ?? "";
                            // markdown posts: ship the stored source as-is.
                            // html posts: convert HTML -> Markdown for display.
                            markdown = string.Equals(post.ContentFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                                ? src
                                : System.engine.Markdown.HtmlToMarkdown.ToMarkdown(src);
                        }
                    }
                }
            }
            catch { }

            if (markdown == null)
            {
                resp.StatusCode = 404;
                resp.Write("Article not found.");
                ApiHelper.EndResponse();
                return;
            }

            // Store in the RAM tier (no-op when cache_ram_enabled = false).
            PublicPageCache.StoreMarkdown(id, markdown);
            resp.AddHeader("X-Cache", "MISS-STORED");
            resp.Write(markdown);
            ApiHelper.EndResponse();
        }
    }
}

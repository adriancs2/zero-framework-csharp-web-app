using System.Text;
using System.engine;

namespace System.engine.RH
{
    // ============================================================
    // ArticleTools - the ONE source of truth for the article content area,
    // including the "View Content / View Markdown" toggle.
    //
    // Every renderer that emits a single post/page body calls this:
    //   - the HTML-template engine (DocLayout.RenderTemplated -> {{article_content}})
    //   - the C# themes (Broadsheet, Hearth) ArticleHtml
    // so the rendered structure is byte-for-byte identical across all themes.
    //
    // OWNERSHIP CONTRACT
    //   This builder owns the INNER content only. The single .doc-content.prose
    //   wrapper is owned by the CALLER (the theme's article template file, or the
    //   C# theme's ArticleHtml). This is why no HTML template needs editing: each
    //   article-*.html already wraps {{article_content}} in .doc-content.prose,
    //   and this builder drops its markup straight inside that wrapper.
    //
    // TOGGLE GATE
    //   The toggle is emitted only for a real post (postId > 0) AND when the admin
    //   setting Settings.ShowMarkdownButton is on (default ON). Pages (postId == 0)
    //   and the setting being off both fall through to the plain content - the
    //   caller's .doc-content.prose then renders exactly as it always did.
    //
    // FIXED STRUCTURE (documented in the General Guidelines / Theme Authoring
    // Guide / C# Template Guide; every theme's own CSS must style it):
    //   <div class='md-toolbar'> ...two buttons... </div>
    //   <div id='post_content'> ...rendered HTML... </div>
    //   <div id='post_markdown' style='display:none'><textarea readonly></textarea></div>
    //   <script>const post_id = N;</script>
    //   <script> showContent() / showMarkdown() - lazy-fetches the markdown </script>
    //
    // No CSS is emitted here. Styling `.md-toolbar`, `.md-toolbar button` and
    // `#post_markdown textarea` is each theme's own responsibility, in its own
    // stylesheet - see the "Fixed CSS classes" section of the authoring guides.
    //
    // The markdown is fetched lazily from the public, no-login endpoint
    // /api/get-article-markdown?id=N (GetArticleMarkdownApi), which both engines
    // already share. IDs are safe here because exactly one article renders per
    // request.
    // ============================================================
    public static class ArticleTools
    {
        // Build the inner content for a post/page body. Returns the plain rendered
        // HTML for a page (postId <= 0) or when the toggle is disabled; otherwise
        // wraps it in the View Content / View Markdown toggle structure above.
        public static string BuildContentArea(string content, int postId)
        {
            content = content ?? "";

            if (postId <= 0 || !Settings.ShowMarkdownButton)
                return content;

            var sb = new StringBuilder();
            sb.Append(@"<div class='md-toolbar'>
<button type='button' onclick='showContent();'>View Content</button>
<button type='button' onclick='showMarkdown();'>View Markdown</button>
</div>
<div id='post_content'>
");
            sb.Append(content);
            sb.Append(@"
</div>
<div id='post_markdown' style='display:none'>
<textarea readonly></textarea>
</div>
");
            // Dynamic: the id this page fetches its raw markdown for.
            sb.AppendFormat("<script>\nconst post_id = {0};\n</script>", postId);
            // Static: the View Content / View Markdown behaviour (public API, no login).
            sb.Append(MarkdownToggleScript);
            return sb.ToString();
        }

        // Static toggle script - identical for every post. A plain (non-interpolated)
        // verbatim string, so the JS braces and the `${post_id}` template literal are
        // emitted to the browser as-is.
        const string MarkdownToggleScript = @"<script>
const API_ENDPOINT = `/api/get-article-markdown?id=${post_id}`;

function showContent() {
    document.getElementById('post_content').style.display = 'block';
    document.getElementById('post_markdown').style.display = 'none';
}

function showMarkdown() {
    document.getElementById('post_content').style.display = 'none';
    document.getElementById('post_markdown').style.display = 'block';
    var ta = document.querySelector('#post_markdown textarea');
    ta.value = 'Loading...';
    fetch(API_ENDPOINT)
        .then(function (r) { return r.text(); })
        .then(function (md) { ta.value = md; })
        .catch(function () { ta.value = 'Failed to load markdown.'; });
}
</script>";
    }
}

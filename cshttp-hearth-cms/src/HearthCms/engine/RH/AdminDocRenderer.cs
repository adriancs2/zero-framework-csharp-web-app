using System.IO;
using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine.Markdown;

namespace System.engine.RH
{
    // ============================================================
    // AdminDocRenderer - shared loader/renderer for the admin documentation
    // pages. Each doc lives as a static Markdown file under
    // /App_Data/AdminDocumentation/ and is converted to HTML at request time by
    // Hearth's own MarkdownToHtml engine (the same parser that renders posts),
    // then wrapped in the admin chrome.
    //
    // Why files instead of hardcoded C#: docs become plain Markdown anyone can
    // read, diff and edit WITHOUT recompiling the app - a contributor fixes a
    // typo in a .md and refreshes. The rendered output is wrapped in a
    // ".admin-doc" container so the admin stylesheet can style headings, tables
    // and code blocks consistently.
    //
    // Live data: a doc may contain {token} placeholders (e.g. {hidden_admin_path})
    // that are substituted with this install's real values AFTER Markdown
    // rendering, via the optional `replacements` map. Tokens absent from the map
    // are left untouched. This keeps the .md fully static while still letting a
    // page show install-specific values inline.
    // ============================================================
    public static class AdminDocRenderer
    {
        const string DocFolder = "~/App_Data/AdminDocumentation/";

        // Render a documentation page: load <fileName> from the docs folder,
        // convert Markdown -> HTML, apply any {token} replacements, and write the
        // full admin page. `activeItem` highlights the sidebar entry.
        public static void Render(
            string fileName,
            string title,
            string pageHeading,
            string activeItem = "guidelines",
            System.Collections.Generic.IDictionary<string, string> replacements = null,
            string topHtml = null,
            string headingActionsHtml = null)
        {
            if (!AdminGuard.RequireLogin()) return;

            var tpl = new AdminTemplate
            {
                Title = title,
                ActiveItem = activeItem,
                PageHeading = pageHeading
            };
            if (!string.IsNullOrEmpty(headingActionsHtml))
                tpl.PageHeadingActionsHtml = headingActionsHtml;

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            // Optional server-rendered block above the document (e.g. a live
            // "this install" notice). Rendered as-is, not through Markdown.
            if (!string.IsNullOrEmpty(topHtml)) sb.Append(topHtml);

            sb.Append("<div class='admin-doc'>");
            sb.Append(RenderBody(fileName, replacements));
            sb.Append("</div>");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        // Load + convert + substitute, returning just the inner HTML. Exposed so
        // a handler can compose the body itself if it needs to.
        public static string RenderBody(
            string fileName,
            System.Collections.Generic.IDictionary<string, string> replacements = null)
        {
            string md = LoadFile(fileName);
            if (md == null)
            {
                return "<div class='card'><div class='card-body'><p class='form-hint'>"
                     + "Documentation file <code>" + HttpUtility.HtmlEncode(fileName)
                     + "</code> was not found in <code>/App_Data/AdminDocumentation/</code>.</p></div></div>";
            }

            // Substitute tokens in the SOURCE markdown before conversion, so a
            // replacement value lands wherever the token sits (including inside
            // code spans / tables) and is then rendered in context.
            if (replacements != null)
            {
                foreach (var kv in replacements)
                    md = md.Replace("{" + kv.Key + "}", kv.Value ?? "");
            }

            // Markdown passthrough ON so any raw HTML in the docs survives.
            return MarkdownToHtml.ToHtml(md, true);
        }

        static string LoadFile(string fileName)
        {
            try
            {
                // Guard against path traversal: only a bare file name is allowed.
                if (string.IsNullOrEmpty(fileName)
                    || fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0
                    || fileName.IndexOf("..", StringComparison.Ordinal) >= 0)
                    return null;

                string dir = HttpContext.Current.Server.MapPath(DocFolder);
                string path = Path.Combine(dir, fileName);
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path);
            }
            catch { return null; }
        }
    }
}

using System.Collections.Generic;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // Theme editor page — /admin/themes/edit/{slug}
    //
    // Standalone class, fully separate from AdminThemes (which keeps the
    // library and server-side preview). Routed from Global.asax via
    // AdminTemplateEdit.HandleEditRequest(slug).
    //
    // The editor's CSS and JS are external static files:
    //   /css/AdminThemeEditor.css
    //   /js/AdminThemeEditor.js
    // The page only emits the per-request `THEME` and `missingFiles`
    // globals inline; the script file reads them on load.
    // ============================================================
    public static class AdminTemplateEdit
    {
        // ---------- /admin/themes/edit/{slug} : file editor ----------
        public static void HandleEditRequest(string slug)
        {
            if (!AdminGuard.RequireLogin()) return;

            ThemeInfo theme = ThemeManager.GetTheme(slug);
            if (theme == null)
            {
                AppSession.SetFlash("Theme not found");
                ApiHelper.Redirect("/admin/themes");
                return;
            }

            // Top-level templates and components are a FIXED set in the current
            // CMS design (a known limitation, for now). Only assets are dynamic —
            // theme authors add their own CSS/JS there.
            List<ThemeFile> ignoreTop, ignoreComp, assets;
            ThemeManager.ListThemeFiles(theme.Slug, out ignoreTop, out ignoreComp, out assets);

            List<ThemeFile> topFiles = BuildFixed(FixedTopLevel);
            List<ThemeFile> compFiles = BuildFixed(FixedComponents);

            var missing = new List<string>();
            foreach (ThemeFile f in topFiles) if (!ThemeManager.ThemeFileExists(theme.Slug, f.Path)) missing.Add(f.Path);
            foreach (ThemeFile f in compFiles) if (!ThemeManager.ThemeFileExists(theme.Slug, f.Path)) missing.Add(f.Path);

            var tpl = new AdminTemplate
            {
                Title = "Edit theme",
                ActiveItem = "themes",
                PageHeading = "Edit theme — " + theme.Name,
                PageHeadingActionsHtml = @"<a href='/admin/themes' class='btn btn-ghost btn-sm'><i class='fa-solid fa-arrow-left'></i> All themes</a>
<a href='/admin/themes/docs' class='btn btn-ghost btn-sm'><i class='fa-solid fa-book'></i> Documentation</a>",
                // Editor stylesheet in <head>.
                ExtraHeaderText = "<link rel='stylesheet' href='/css/AdminThemeEditor.css' />",
                // The home editor's image fields reuse the shared media browser.
                ExtraFooterText = "<script src='/js/media-browser.js'></script>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            bool canDelete = !theme.IsBuiltin && !theme.IsActive;
            string statusBadge = theme.IsActive
                ? "<span class='badge badge-success'><i class='fa-solid fa-check'></i> Active</span>"
                : "<span class='badge badge-muted'>Inactive</span>";
            string builtinBadge = theme.IsBuiltin ? " <span class='badge badge-info'><i class='fa-solid fa-lock'></i> Default</span>" : "";

            // ----- meta info -----
            sb.Append("<div class='te-meta'>\n");
            sb.Append("    <div class='te-meta-row'>" + MetaItem("Name", theme.Name) + MetaItem("Folder", theme.Slug) + MetaItem("Author", theme.Author) + MetaItem("Version", theme.Version) + MetaItem("Date", theme.Date) + "</div>\n");
            sb.Append($"    <div class='te-meta-row'><div class='te-meta-item'><span class='te-meta-k'>Status</span><span class='te-meta-v'>{statusBadge}{builtinBadge}</span></div>");
            if (!string.IsNullOrEmpty(theme.Url))
                sb.Append("<div class='te-meta-item'><span class='te-meta-k'>URL</span><span class='te-meta-v'>" + HttpUtility.HtmlEncode(theme.Url) + "</span></div>");
            sb.Append("</div>\n");
            if (!string.IsNullOrEmpty(theme.Description))
                sb.Append("    <p class='te-meta-desc'>" + HttpUtility.HtmlEncode(theme.Description) + "</p>\n");
            sb.Append("</div>\n");

            // ----- top action bar -----
            string activateDisabled = theme.IsActive ? "disabled" : "";
            string activateLabel = theme.IsActive ? "Active" : "Activate";

            string deleteDisabled = canDelete ? "" : "disabled";
            string deleteTitle =
                theme.IsBuiltin ? "The default theme cannot be deleted" :
                theme.IsActive ? "Activate another theme first" :
                                  "Delete this theme";

            sb.Append($@"
<div class='te-actions'>
    <button type='button' class='btn btn-primary' onclick='activate()' {activateDisabled}><i class='fa-solid fa-circle-check'></i> {activateLabel}</button>
    <button type='button' class='btn btn-ghost theme-danger' onclick='del()' {deleteDisabled} title='{deleteTitle}'><i class='fa-solid fa-trash'></i> Delete</button>
    <button type='button' class='btn btn-ghost' id='previewBtn' onclick='togglePreview()'><i class='fa-solid fa-eye'></i> Preview</button>
    <button type='button' class='btn btn-primary' id='homeBtn' onclick='editHome()'><i class='fa-solid fa-pen-to-square'></i> Edit Home Content</button>
</div>
");

            // ----- editor grid: file panel + textarea/iframe -----
            sb.Append($@"<div class='te-grid'>
    <aside class='te-files'>
{RenderFileSection("Top-level templates", topFiles)}
{RenderFileSection("Components", compFiles)}
{RenderFileSection("Assets (CSS / JS)", assets)}
    </aside>

    <div class='te-editor'>
        <div class='te-editor-bar' id='editorBar'>
            <span class='te-curfile' id='curFile'>Select a file</span>
            <span class='te-editor-tools'>
                <button type='button' class='btn btn-primary btn-sm' onclick='saveFile()'><i class='fa-solid fa-floppy-disk'></i> Save</button>
                <button type='button' class='btn btn-ghost btn-sm' onclick='refreshFile()'><i class='fa-solid fa-rotate'></i> Refresh</button>
            </span>
        </div>
        <textarea id='fileText' class='te-textarea' spellcheck='false' wrap='off' placeholder='Select a file from the left to edit it.'></textarea>
        <iframe id='previewFrame' class='te-iframe' style='display:none'></iframe>
    </div>
</div>
");

            string themeSlug = JsString(theme.Slug);
            string missingFiles = JsArray(missing);

            // ----- per-request globals + external editor script -----
            sb.Append($@"
<script>
var THEME = {themeSlug};
var missingFiles = {missingFiles};
</script>
<script src='/js/AdminThemeEditor.js'></script>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        static string MetaItem(string k, string v)
        {
            if (string.IsNullOrEmpty(v)) v = "—";
            return "<div class='te-meta-item'><span class='te-meta-k'>" + HttpUtility.HtmlEncode(k)
                + "</span><span class='te-meta-v'>" + HttpUtility.HtmlEncode(v) + "</span></div>";
        }

        static string RenderFileSection(string title, List<ThemeFile> files)
        {
            var sb = new StringBuilder();
            sb.Append("        <section class='te-fsec'>\n");
            sb.Append("            <h4>" + HttpUtility.HtmlEncode(title) + "</h4>\n");
            if (files == null || files.Count == 0)
            {
                sb.Append("            <p class='te-empty'>None</p>\n");
            }
            else
            {
                sb.Append("            <ul class='te-flist'>\n");
                foreach (ThemeFile f in files)
                {
                    string icon = IconFor(f.Name);
                    sb.Append($"                <li><button type='button' class='te-file' data-area='{HttpUtility.HtmlAttributeEncode(f.Area)}' data-path='{HttpUtility.HtmlAttributeEncode(f.Path)}' onclick='openFile(this)'><i class='fa-solid {icon}'></i> {HttpUtility.HtmlEncode(f.Name)}</button></li>\n");
                }
                sb.Append("            </ul>\n");
            }
            sb.Append("        </section>\n");
            return sb.ToString();
        }

        static string IconFor(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.EndsWith(".css")) return "fa-paintbrush";
            if (n.EndsWith(".js")) return "fa-code";
            if (n.EndsWith(".html") || n.EndsWith(".htm")) return "fa-file-code";
            if (n.EndsWith(".txt") || n.EndsWith(".md")) return "fa-file-lines";
            if (n.EndsWith(".json")) return "fa-brackets-curly";
            return "fa-file";
        }

        static string JsString(string s)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(s ?? "");
        }

        static string JsArray(List<string> items)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(items ?? new List<string>());
        }

        // The fixed template set (current CMS limitation: top-level templates and
        // components are a known, fixed list; only assets are dynamic).
        static readonly string[] FixedTopLevel =
        {
            "_layout.html", "home.html", "latest-post.html", "categories-latest-post.html",
            "category.html", "article-full-width.html", "article-sidebar.html", "config.txt"
        };
        static readonly string[] FixedComponents =
        {
            "components/row-post.html", "components/category-section.html", "components/post-card.html",
            "components/cat-mini-item.html", "components/cover-image.html",
            "components/section-latest-posts.html", "components/footer-column.html"
        };

        static List<ThemeFile> BuildFixed(string[] paths)
        {
            var list = new List<ThemeFile>();
            foreach (string p in paths)
            {
                int slash = p.LastIndexOf('/');
                string name = slash >= 0 ? p.Substring(slash + 1) : p;
                list.Add(new ThemeFile { Area = "tpl", Path = p, Name = name });
            }
            return list;
        }
    }
}

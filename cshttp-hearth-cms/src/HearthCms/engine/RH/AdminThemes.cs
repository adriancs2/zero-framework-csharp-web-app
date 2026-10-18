using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;
using System.IO;

namespace System.engine.RH
{
    // ============================================================
    // Folder-based theme admin.
    //   /admin/themes                 - library (cards: edit/delete/activate)
    //   /admin/themes/edit/{slug}     - file editor (3 sections + textarea + preview)
    //   /admin/themes/preview/{slug}  - server-rendered home page in that theme
    // Themes live on disk (App_Data/themes/{slug}/ + assets/themes/{slug}/);
    // there is no themes database table.
    // ============================================================
    public static class AdminThemes
    {
        // ---------- /admin/themes : library ----------
        public static void HandleListRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            List<ThemeInfo> themes = ThemeManager.GetAllThemesIncludingCs();

            // Sort cards: active theme(s) first, then alphabetically by name.
            themes.Sort(delegate (ThemeInfo a, ThemeInfo b)
            {
                if (a.IsActive != b.IsActive) return a.IsActive ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            var tpl = new AdminTemplate
            {
                Title = "Themes",
                ActiveItem = "themes",
                PageHeading = "Themes",
                PageHeadingActionsHtml =
                    "<a href='/admin/themes/docs' class='btn btn-ghost btn-sm'><i class='fa-solid fa-book'></i> Documentation</a>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            sb.Append("<p class='form-hint' style='margin-top:0'>Each theme is a folder under <code>/App_Data/themes/</code> with its assets under <code>/assets/themes/</code>. Browsing " + themes.Count + " theme" + (themes.Count == 1 ? "" : "s") + ".</p>\n");

            sb.Append("<div class='theme-grid'>\n");
            foreach (ThemeInfo t in themes) sb.Append(RenderThemeCard(t));
            sb.Append("</div>\n");

            sb.Append(@"
<script>
async function activateTheme(slug) {
    var fd = new FormData();
    fd.append('action', 'activate');
    fd.append('slug', slug);
    var r = await fetch('/api/admin/themes', { method: 'POST', body: fd });
    var d = await r.json();
    if (d.success) flashGoodAndReload('Activated', 'Theme activated.');
    else showErrorMessage('Activate failed', d.message);
}
async function deleteTheme(slug, name) {
    if (!confirm('Delete theme \'' + name + '\' and its assets? This cannot be undone.')) return;
    var fd = new FormData();
    fd.append('action', 'delete');
    fd.append('slug', slug);
    var r = await fetch('/api/admin/themes', { method: 'POST', body: fd });
    var d = await r.json();
    if (d.success) flashGoodAndReload('Deleted', 'Theme deleted.');
    else showErrorMessage('Delete failed', d.message);
}
</script>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        static string RenderThemeCard(ThemeInfo t)
        {
            string slugAttr = HttpUtility.HtmlAttributeEncode(t.Slug);
            string nameJs = HttpUtility.HtmlAttributeEncode(t.Name);
            string activeBadge = t.IsActive ? "<span class='badge badge-success'><i class='fa-solid fa-check'></i> Active</span>" : "";
            string builtinBadge = t.IsBuiltin ? "<span class='badge badge-info'><i class='fa-solid fa-lock'></i> Default</span>" : "";
            string csBadge = t.IsCsTemplate ? "<span class='badge badge-info'><i class='fa-solid fa-code'></i> C#</span>" : "";
            string desc = string.IsNullOrEmpty(t.Description)
                ? "<span class='text-muted'>No description</span>" : HttpUtility.HtmlEncode(t.Description);

            // A C# theme is compiled code, not an editable folder of HTML; show
            // its source location and a code badge, and omit Edit/Delete.
            string fileLine = t.IsCsTemplate
                ? "<code>Themes/ (compiled)</code>"
                : "<code>/App_Data/themes/" + HttpUtility.HtmlEncode(t.Slug) + "/</code>";

            var sb = new StringBuilder();
            sb.Append($@"<div class='theme-card{(t.IsActive ? " is-active" : "")}'>
    {RenderThumb(t)}
    <div class='theme-card-body'>
        <div class='theme-card-titlerow'>
            <h3>{HttpUtility.HtmlEncode(t.Name)}</h3>
            <div class='theme-card-badges'>{activeBadge} {builtinBadge} {csBadge}</div>
        </div>
        <p class='theme-card-desc'>{desc}</p>
        <p class='theme-card-file'>{fileLine}</p>
    </div>
    <div class='theme-card-actions'>
");
            // Edit applies only to folder (HTML) themes.
            if (!t.IsCsTemplate)
                sb.Append($"        <a href='/admin/themes/edit/{slugAttr}' class='btn btn-ghost btn-sm'><i class='fa-solid fa-pen'></i> Edit</a>\n");
            if (!t.IsActive)
                sb.Append($"        <button type='button' class='btn btn-primary btn-sm' onclick=\"activateTheme('{slugAttr}')\"><i class='fa-solid fa-circle-check'></i> Activate</button>\n");
            // Delete applies only to folder themes (can't delete compiled code).
            if (!t.IsCsTemplate && !t.IsBuiltin && !t.IsActive)
                sb.Append($"        <button type='button' class='btn btn-ghost btn-sm theme-danger' onclick=\"deleteTheme('{slugAttr}','{nameJs}')\"><i class='fa-solid fa-trash'></i></button>\n");
            sb.Append("    </div>\n</div>\n");
            return sb.ToString();
        }

        // Build the card thumbnail. If a screenshot exists at
        // /assets/themes/{slug}/showcase.jpg, render it as an overflow-clipped
        // image (top-aligned crop). Otherwise fall back to the gradient thumb.
        static string RenderThumb(ThemeInfo t)
        {
            string title = HttpUtility.HtmlEncode(t.Name);
            string showcasePath = Path.Combine(ThemeManager.AssetFolder(t.Slug), "showcase.jpg");

            bool hasShowcase = false;
            try { hasShowcase = File.Exists(showcasePath); }
            catch { hasShowcase = false; }

            if (hasShowcase)
            {
                // Cache-bust on file write time so updated screenshots refresh.
                long ver = 0;
                try { ver = File.GetLastWriteTimeUtc(showcasePath).Ticks; }
                catch { ver = 0; }

                string url = "/assets/themes/" + HttpUtility.UrlEncode(t.Slug) + "/showcase.jpg?v=" + ver;
                return
$@"<div class='theme-card-thumb theme-card-thumb-img'>
        <img class='theme-card-shot' src='{HttpUtility.HtmlAttributeEncode(url)}' alt='{HttpUtility.HtmlAttributeEncode(t.Name)} preview' loading='lazy' />
        <div class='theme-card-thumb-overlay'>
            <span class='theme-card-thumb-title'>{title}</span>
        </div>
    </div>";
            }

            return
$@"<div class='theme-card-thumb' style='background:{ThumbBackgroundFor(t.Slug)}'>
        <div class='theme-card-thumb-overlay'>
            <span class='theme-card-thumb-title'>{title}</span>
        </div>
    </div>";
        }

        // ---------- /admin/themes/preview/{slug} : real home page in this theme ----------
        public static void HandlePreviewRequest(string slug)
        {
            if (!AdminGuard.RequireLogin()) return;

            // C# (code) theme: render its home directly via the registry. The
            // preview path isn't cache-eligible, so HandleHome's WriteCached
            // just writes the response without polluting the public cache.
            var cs = CsTemplate.CsThemeRegistry.All()
                .Find(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (cs != null)
            {
                cs.HandleHome();
                return;
            }

            ThemeInfo theme = ThemeManager.GetTheme(slug);
            if (theme == null)
            {
                ApiHelper.WriteHtml("<!doctype html><meta charset='utf-8'><body style='font:15px sans-serif;padding:2rem;color:#555'>Theme not found.</body>");
                ApiHelper.EndResponse();
                return;
            }

            string siteName = Settings.SiteName;
            string siteTagline = Settings.SiteTagline;
            string siteDesc = Settings.SiteDescription;

            int homeCount = Settings.HomePostCount;
            if (homeCount < 1) homeCount = 1;
            if (homeCount > 50) homeCount = 50;

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
            body = ThemeManager.ApplyHomeEdits(body, ThemeManager.ReadHomeValues(slug));

            var pt = new PublicTemplate { Title = "", BodyClass = "page-home", ForceThemeSlug = slug };
            ApiHelper.WriteHtml(pt.RenderPage(body));
            ApiHelper.EndResponse();
        }

        static string RenderPostCards(string slug, List<obPost> posts)
        {
            string cardTpl = TemplateEngine.Load(slug, "components/post-card.html");
            var sb = new StringBuilder();
            foreach (obPost p in posts)
            {
                string excerpt = string.IsNullOrEmpty(p.Excerpt)
                    ? HomePage.ToPlainText(p.Content, 160) : p.Excerpt;
                var m = new TemplateModel();
                m.SetAttr("post_url", "/" + p.Slug);
                m.SetText("post_title", p.Title);
                m.SetText("post_excerpt", excerpt);
                m.SetText("post_date", DateDisplay.Format(p.DatePublished));
                sb.Append(m.Render(cardTpl));
            }
            return sb.ToString();
        }

        // Pseudo-random pastel gradient for a card thumb, derived from the slug.
        static string ThumbBackgroundFor(string slug)
        {
            int hash = 0;
            foreach (char c in (slug ?? "")) hash = ((hash << 5) - hash) + c;
            int h1 = ((hash % 360) + 360) % 360;
            int h2 = (h1 + 60) % 360;
            return $"linear-gradient(135deg, hsl({h1},70%,72%) 0%, hsl({h2},65%,82%) 100%)";
        }

    }
}
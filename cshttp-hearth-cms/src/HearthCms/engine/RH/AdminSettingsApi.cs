using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminSettingsApi
    {
        // Whitelist of settings keys we accept from the form.
        static readonly string[] AllowedKeys = new[] {
            "site_name", "site_tagline", "site_description",
            "favicon_source_url", "og_image_url", "logo_url", "logo_mode", "theme_color",
            "footer_text",
            "footer_col_count", "footer_col_1", "footer_col_2", "footer_col_3", "footer_col_4",
            "home_post_count", "latest_post_count", "categories_post_count",
            "category_post_count", "article_sidebar_post_count",
            "show_markdown_button",
            "cache_ram_enabled", "cache_file_enabled", "cache_ram_max_mb",
            "home_page_mode", "home_page_id",
            "date_format",
            "dev_mode",
            "analytics_enabled"
        };

        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "save": Save(); break;
                    case "clear-cache": ClearCache(); break;
                    case "generate-sitemap": GenerateSitemap(); break;
                    case "home-pages-select": HomePagesSelect(); break;
                    case "generate-api-token": GenerateApiToken(); break;
                    case "clear-api-token": ClearApiToken(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Save()
        {
            var req = HttpContext.Current.Request;

            // Admin URL slug — validated and saved separately from the generic
            // whitelist so we can reject reserved/invalid values and never clobber
            // a config.txt override. Ignored entirely when config.txt is pinning
            // the slug (the field is read-only in that case).
            string adminUrl = (req.Form["admin_url"] + "").Trim();
            if (!AdminSlug.IsLockedByConfig && adminUrl.Length > 0)
            {
                string normalized = adminUrl.ToLowerInvariant();
                if (!AdminSlug.IsValid(normalized))
                {
                    ApiHelper.WriteError(
                        "Invalid admin URL. Use letters, numbers, hyphens or underscores, " +
                        "and avoid reserved words (login, api, category, reset_app, etc.).");
                    return;
                }
                Db.SaveSetting(AdminSlug.SettingKey, normalized);
            }

            // Capture the favicon source BEFORE the save loop so we can detect a
            // change and (re)generate the icon set + manifest only when it moves.
            string prevFaviconSource = Settings.FaviconSourceUrl;

            foreach (string key in AllowedKeys)
            {
                string val = req.Form[key];
                if (val == null) continue;
                val = val.Trim();
                // Constrain home_page_mode to a known layout id (0=Default, 1=Page,
                // 2=Latest Post, 3=Categories+Latest). Anything else -> Default.
                if (key == "home_page_mode" &&
                    val != "0" && val != "1" && val != "2" && val != "3")
                {
                    val = "0";
                }
                // Logo display mode: text | image | image_text (default text).
                if (key == "logo_mode" && val != "text" && val != "image" && val != "image_text")
                {
                    val = "text";
                }
                // Footer column count: clamp to 0..4 (0 = no footer columns area).
                if (key == "footer_col_count")
                {
                    int n;
                    if (!int.TryParse(val, out n) || n < 0) n = 0;
                    if (n > 4) n = 4;
                    val = n.ToString();
                }
                // RAM cache budget (MB): 0 = unlimited (no eviction); otherwise
                // clamp to [5, 4096] so a typo can't set an unusable tiny/huge
                // limit. A non-numeric value falls back to the 250 MB default.
                if (key == "cache_ram_max_mb")
                {
                    int mb;
                    if (!int.TryParse(val, out mb)) mb = 250;
                    if (mb != 0)
                    {
                        if (mb < 5) mb = 5;
                        if (mb > 4096) mb = 4096;
                    }
                    val = mb.ToString();
                }
                // Date display format: must be built only from the allowed
                // day/month/year tokens and separators. Anything invalid (or
                // empty) falls back to the default so a display can never throw.
                if (key == "date_format")
                {
                    if (!DateDisplay.IsValid(val)) val = DateDisplay.DefaultFormat;
                }
                Db.SaveSetting(key, val);
            }

            // Favicon (re)generation: when the source image changed to a non-empty
            // value, build the full icon set + manifest from it. When it was
            // cleared, leave existing generated files alone (harmless) - the header
            // simply stops referencing them once HasGeneratedSet can't find the ico.
            // A fresh install with no source uploaded generates nothing.
            string newFaviconSource = Settings.FaviconSourceUrl;
            string faviconNote = "";
            if (!string.IsNullOrEmpty(newFaviconSource) &&
                !string.Equals(newFaviconSource, prevFaviconSource, StringComparison.OrdinalIgnoreCase))
            {
                string err;
                if (!FaviconGenerator.Generate(out err))
                    faviconNote = " (favicon set could not be generated: " + err + ")";
            }
            else if (!string.IsNullOrEmpty(newFaviconSource) &&
                     string.Equals(newFaviconSource, prevFaviconSource, StringComparison.OrdinalIgnoreCase) &&
                     !FaviconGenerator.HasGeneratedSet())
            {
                // Source unchanged but the generated files are missing (e.g. first
                // save after the feature shipped, or files were deleted): build them.
                string err;
                if (!FaviconGenerator.Generate(out err))
                    faviconNote = " (favicon set could not be generated: " + err + ")";
            }

            PublicPageCache.InvalidateAll();
            // Templates are cache-gated on cache_ram_enabled. Drop the template
            // cache on every settings save so a RAM toggle (or any change) takes
            // effect immediately: turning RAM off must stop serving cached
            // templates, and turning it back on should start from a clean slate.
            TemplateEngine.ClearCache();
            AppSession.SetFlash("Settings saved" + faviconNote);
            // Hand back the resolved admin base so the client can redirect to the
            // (possibly new) admin URL instead of reloading a now-hidden path.
            ApiHelper.WriteSuccess("Saved" + faviconNote, new { adminBase = "/" + AdminSlug.Current });
        }

        // Server-side renders the complete <select> of published pages for the
        // home-page picker. The client just dumps data.html into its container.
        static void HomePagesSelect()
        {
            int selectedId = Settings.HomePageId;

            var pages = new List<obPage>();
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    pages = s.GetObjectList<obPage>(
                        "SELECT id, title, slug FROM pages WHERE is_published=1 AND is_deleted=0 ORDER BY title ASC;");
                }
            }

            var sb = new StringBuilder();
            sb.Append("<select id='home_page_id' name='home_page_id' class='form-control'>");
            if (pages.Count == 0)
            {
                sb.Append("<option value='0'>(no published pages)</option>");
            }
            else
            {
                foreach (var p in pages)
                {
                    string sel = (p.Id == selectedId) ? " selected" : "";
                    sb.Append($"<option value='{p.Id}'{sel}>{HttpUtility.HtmlEncode(p.Title)}</option>");
                }
            }
            sb.Append("</select>");

            ApiHelper.WriteSuccess("OK", new { html = sb.ToString() });
        }

        // Manual "Regenerate now" for the sitemap. Forces an immediate rebuild
        // (same generator the live /sitemap.xml uses) and stamps the timestamp.
        static void GenerateSitemap()
        {
            int urls = SitemapGenerator.Generate();
            ApiHelper.WriteSuccess(
                "Sitemap regenerated (" + urls + " URL" + (urls == 1 ? "" : "s") + ")",
                new { urls, sitemapUrl = SitemapGenerator.SitemapUrl(), lastGenerated = SitemapGenerator.LastGenerated });
        }

        static void ClearCache()
        {
            PublicPageCache.InvalidateAll();
            TemplateEngine.ClearCache();
            AppSession.SetFlash("Page cache cleared");
            ApiHelper.WriteSuccess("Cache cleared");
        }

        // Generate a new random API token, store it, and return it so the admin
        // can copy it. Regenerating immediately invalidates any previous token.
        static void GenerateApiToken()
        {
            string token = NewToken();
            Db.SaveSetting("api_token", token);
            ApiHelper.WriteSuccess("API token generated", new { token });
        }

        // Disable token auth by clearing the stored secret.
        static void ClearApiToken()
        {
            Db.SaveSetting("api_token", "");
            ApiHelper.WriteSuccess("API token cleared");
        }

        // A URL-safe, ~43-char random token (256 bits, base64url without padding).
        static string NewToken()
        {
            byte[] buf = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(buf);
            return Convert.ToBase64String(buf)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}

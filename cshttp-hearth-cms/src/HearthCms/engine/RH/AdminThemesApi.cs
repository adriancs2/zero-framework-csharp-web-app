using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // POST/GET /api/admin/themes  (action = activate | delete | readfile | savefile)
    // Folder-based: operates on files under App_Data/themes/{slug}/ and
    // assets/themes/{slug}/. No database table.
    public static class AdminThemesApi
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "activate": Activate(); break;
                    case "delete": Delete(); break;
                    case "readfile": ReadFile(); break;
                    case "savefile": SaveFile(); break;
                    case "savehome": SaveHome(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Activate()
        {
            string slug = (HttpContext.Current.Request["slug"] + "").Trim();

            // Resolve a display name from either a folder theme or a C# theme.
            ThemeInfo t = ThemeManager.GetTheme(slug);
            string name = t != null ? t.Name : null;
            if (name == null)
            {
                var cs = CsTemplate.CsThemeRegistry.All()
                    .Find(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
                if (cs != null) name = cs.Name;
            }
            if (name == null) { ApiHelper.WriteError("Theme not found"); return; }

            if (!ThemeManager.Activate(slug)) { ApiHelper.WriteError("Could not activate theme"); return; }
            PublicPageCache.InvalidateAll();

            AppSession.SetFlash("Activated theme: " + name);
            ApiHelper.WriteSuccess("Activated");
        }

        static void Delete()
        {
            string slug = (HttpContext.Current.Request["slug"] + "").Trim();
            ThemeInfo t = ThemeManager.GetTheme(slug);
            if (t == null) { ApiHelper.WriteError("Theme not found"); return; }

            string err;
            if (!ThemeManager.DeleteTheme(slug, out err)) { ApiHelper.WriteError(err); return; }

            PublicPageCache.InvalidateAll();
            AppSession.SetFlash("Deleted theme: " + t.Name);
            ApiHelper.WriteSuccess("Deleted");
        }

        static void ReadFile()
        {
            var req = HttpContext.Current.Request;
            string slug = (req["slug"] + "").Trim();
            string area = (req["area"] + "").Trim();
            string path = (req["path"] + "").Trim();

            if (ThemeManager.GetTheme(slug) == null) { ApiHelper.WriteError("Theme not found"); return; }

            string content = ThemeManager.ReadThemeFile(slug, area, path);
            if (content == null) { ApiHelper.WriteError("File not found or not editable"); return; }

            ApiHelper.WriteJson(new { success = true, data = new { content } });
        }

        static void SaveFile()
        {
            var req = HttpContext.Current.Request;
            string slug = (req.Form["slug"] + "").Trim();
            string area = (req.Form["area"] + "").Trim();
            string path = (req.Form["path"] + "").Trim();
            string content = req.Form["content"] + "";

            if (ThemeManager.GetTheme(slug) == null) { ApiHelper.WriteError("Theme not found"); return; }

            string err;
            if (!ThemeManager.WriteThemeFile(slug, area, path, content, out err))
            {
                ApiHelper.WriteError(string.IsNullOrEmpty(err) ? "Could not save file" : err);
                return;
            }

            // Templates are cached; clear the cache so edits take effect
            // immediately, and clear the rendered-page cache.
            TemplateEngine.ClearCache();
            PublicPageCache.InvalidateAll();

            ApiHelper.WriteJson(new { success = true, message = "Saved" });
        }

        // Save the theme-specific home content (data-edit regions) to the theme
        // folder's home.values.json. Posts: slug, values (JSON map key->text).
        static void SaveHome()
        {
            var req = HttpContext.Current.Request;
            string slug = (req.Form["slug"] + "").Trim();
            string valuesJson = req.Form["values"] + "";

            if (ThemeManager.GetTheme(slug) == null) { ApiHelper.WriteError("Theme not found"); return; }

            var dict = new System.Collections.Generic.Dictionary<string, string>();
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(valuesJson);
                if (parsed != null)
                {
                    foreach (var kv in parsed)
                        if (IsSafeKey(kv.Key)) dict[kv.Key] = kv.Value ?? "";
                }
            }
            catch { ApiHelper.WriteError("Invalid data"); return; }

            string err;
            if (!ThemeManager.WriteHomeValues(slug, dict, out err))
            {
                ApiHelper.WriteError(string.IsNullOrEmpty(err) ? "Could not save" : err);
                return;
            }

            PublicPageCache.InvalidateAll();
            ApiHelper.WriteJson(new { success = true, message = "Saved", data = new { count = dict.Count } });
        }

        static bool IsSafeKey(string k)
        {
            if (string.IsNullOrEmpty(k) || k.Length > 64) return false;
            foreach (char c in k)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            return true;
        }
    }
}

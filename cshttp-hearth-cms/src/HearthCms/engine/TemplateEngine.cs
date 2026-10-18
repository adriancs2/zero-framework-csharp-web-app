using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // TemplateEngine - loads external HTML templates from
    //   ~/App_Data/themes/{slug}/...            (server-only; IIS blocks /App_Data)
    // and fills {{token}} placeholders.
    //
    // Design rules:
    //   * Templates contain NO logic - only {{token}} placeholders.
    //   * Escaping is decided by C# per-token via TemplateModel
    //     (SetText escapes, SetAttr attribute-escapes, SetRaw verbatim).
    //   * Loaded template text is cached, keyed by slug+name; an admin
    //     edit calls ClearCache() so it invalidates without restart.
    //   * A missing file in the active theme falls back to the built-in
    //     "site" theme, so a half-finished child theme never 500s.
    // ============================================================
    public static class TemplateEngine
    {
        static readonly ConcurrentDictionary<string, string> _cache =
            new ConcurrentDictionary<string, string>();

        // Load a template file (e.g. "_layout.html" or "components/post-card.html")
        // for the given theme slug. Falls back to the built-in theme, then to a
        // visible HTML comment so a missing template is obvious but harmless.
        public static string Load(string themeSlug, string name)
        {
            if (!ThemeManager.IsValidSlug(themeSlug)) themeSlug = ThemeManager.BuiltinSlug;

            // Template caching is governed by the same switch as the page RAM
            // cache (cache_ram_enabled). When RAM caching is OFF, templates are
            // always read fresh from disk - so hand-editing a template file on
            // disk takes effect immediately, at the cost of a small per-request
            // file read. When ON, parsed template text is cached in RAM and only
            // refreshed on an admin edit / theme switch (which call ClearCache).
            bool useCache = PublicPageCache.RamEnabled;

            string cacheKey = themeSlug + "|" + name;

            string cached;
            if (useCache && _cache.TryGetValue(cacheKey, out cached)) return cached;

            string text = ReadFile(themeSlug, name);
            if (text == null && themeSlug != ThemeManager.BuiltinSlug)
                text = ReadFile(ThemeManager.BuiltinSlug, name);
            if (text == null)
                text = "<!-- template not found: " + HttpUtility.HtmlEncode(name) + " -->";

            if (useCache) _cache[cacheKey] = text;
            return text;
        }

        // Drop every cached template. Called after an admin theme edit or a
        // theme switch so changes take effect without an app restart.
        public static void ClearCache()
        {
            _cache.Clear();
        }

        // Convenience: load a template and render it with a model in one call.
        public static string Render(string themeSlug, string name, TemplateModel model)
        {
            string tpl = Load(themeSlug, name);
            return model == null ? tpl : model.Render(tpl);
        }

        static string ThemesRoot
        {
            get { return HttpContext.Current.Server.MapPath("~/App_Data/themes/"); }
        }

        static string ReadFile(string slug, string name)
        {
            try
            {
                string rel = (name ?? "").Replace('/', Path.DirectorySeparatorChar);
                string themeDir = Path.Combine(ThemesRoot, slug);
                string path = Path.GetFullPath(Path.Combine(themeDir, rel));

                // Defensive: never read outside the theme's own folder.
                if (!path.StartsWith(Path.GetFullPath(themeDir), StringComparison.OrdinalIgnoreCase))
                    return null;
                if (!File.Exists(path)) return null;
                return File.ReadAllText(path);
            }
            catch { return null; }
        }
    }

    // ============================================================
    // TemplateModel - a token bag that carries ENCODING INTENT.
    // The handler declares what each value is; the model applies the
    // right escaping when it is set, so the template author never has
    // to think about escaping.
    // ============================================================
    public class TemplateModel
    {
        readonly Dictionary<string, string> _vals =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Plain text -> HTML-encoded (titles, names, excerpts...).
        public TemplateModel SetText(string key, string value)
        {
            _vals[key] = HttpUtility.HtmlEncode(value ?? "");
            return this;
        }

        // Value going into an attribute (href, src...) -> attribute-encoded.
        public TemplateModel SetAttr(string key, string value)
        {
            _vals[key] = HttpUtility.HtmlAttributeEncode(value ?? "");
            return this;
        }

        // Pre-built / trusted HTML -> injected verbatim (nav, card lists,
        // rendered markdown). Caller is responsible for its safety.
        public TemplateModel SetRaw(string key, string value)
        {
            _vals[key] = value ?? "";
            return this;
        }

        // Single-pass {{token}} substitution. Scans the template once, so HTML
        // injected via a token is NEVER re-scanned for further tokens (no
        // accidental double substitution, no regex). Unknown tokens render empty.
        public string Render(string template)
        {
            if (string.IsNullOrEmpty(template)) return "";

            var sb = new StringBuilder(template.Length + 256);
            int i = 0, n = template.Length;
            while (i < n)
            {
                int open = template.IndexOf("{{", i, StringComparison.Ordinal);
                if (open < 0) { sb.Append(template, i, n - i); break; }

                sb.Append(template, i, open - i);

                int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (close < 0) { sb.Append(template, open, n - open); break; }

                string key = template.Substring(open + 2, close - open - 2).Trim();
                string val;
                if (_vals.TryGetValue(key, out val)) sb.Append(val);
                // unknown token -> emit nothing

                i = close + 2;
            }
            return sb.ToString();
        }
    }
}

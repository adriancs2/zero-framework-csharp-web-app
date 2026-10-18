using CsHttp.Web;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace System.engine
{
    // ============================================================
    // ThemeManager - folder-based theme system.
    //
    // A theme is a folder under ~/App_Data/themes/{slug}/ holding the
    // template files (+ components/) and a config.txt metadata file,
    // paired with a public asset folder at ~/assets/themes/{slug}/
    // (its CSS/JS/images). There is NO themes database table; the
    // active theme is just the "active_theme" setting (a folder name).
    // ============================================================
    public static class ThemeManager
    {
        // The reference theme shipped with the CMS; used as the render
        // fallback and protected from deletion.
        //
        // NOTE: this is the *preferred* default slug. The actual fallback used
        // by GetActiveSlug() is whatever DefaultSlug() resolves to at runtime,
        // which is guaranteed to be a theme that actually exists (folder or C#).
        public const string BuiltinSlug = "hearth-cs";
        public const int MaxSlugLength = 64;

        // Filename-safe: latin letters, digits, dash, underscore. 1-64 chars.
        static readonly Regex SlugRx = new Regex("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

        // Text files the editor is allowed to open/save.
        static readonly HashSet<string> EditableExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".htm", ".css", ".js", ".txt", ".json", ".svg", ".xml", ".md"
        };

        public static bool IsValidSlug(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return false;
            return SlugRx.IsMatch(slug);
        }

        // ===== Paths =====
        public static string ThemesRoot
        {
            get { return HostingEnvironment.MapPath("~/App_Data/themes/"); }
        }

        public static string AssetsRoot
        {
            get { return HostingEnvironment.MapPath("~/assets/themes/"); }
        }

        public static string ThemeFolder(string slug) { return Path.Combine(ThemesRoot, slug ?? ""); }
        public static string AssetFolder(string slug) { return Path.Combine(AssetsRoot, slug ?? ""); }
        public static string AssetCssPath(string slug) { return Path.Combine(AssetFolder(slug), "style.css"); }
        public static string AssetJsPath(string slug) { return Path.Combine(AssetFolder(slug), "script.js"); }
        public static string CssPublicUrl(string slug) { return "/assets/themes/" + slug + "/style.css"; }
        public static string JsPublicUrl(string slug) { return "/assets/themes/" + slug + "/script.js"; }

        // Resolve the public URL of a folder theme's primary stylesheet. Folder
        // themes ship their CSS under their own name (e.g. almanac.css), not the
        // legacy "style.css", so the fixed CssPublicUrl() 404s for them - which is
        // why the admin Markdown preview rendered unstyled. Probe the theme's asset
        // folder in order of confidence:
        //   1. style.css            - the legacy/"site" convention (EnsureAssetFiles)
        //   2. {slug}.css           - the naming convention every shipped folder theme uses
        //   3. the first *.css file  - last-resort for a custom filename
        // Returns null when the theme folder has no stylesheet at all, so callers
        // can fall back to CssPublicUrl().
        public static string ResolveCssPublicUrl(string slug)
        {
            try
            {
                string folder = AssetFolder(slug);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

                if (File.Exists(Path.Combine(folder, "style.css")))
                    return "/assets/themes/" + slug + "/style.css";

                string conv = (slug ?? "").ToLowerInvariant() + ".css";
                string[] cssFiles = Directory.GetFiles(folder, "*.css");
                foreach (string f in cssFiles)
                {
                    if (string.Equals(Path.GetFileName(f), conv, StringComparison.OrdinalIgnoreCase))
                        return "/assets/themes/" + slug + "/" + Path.GetFileName(f);
                }

                if (cssFiles.Length > 0)
                    return "/assets/themes/" + slug + "/" + Path.GetFileName(cssFiles[0]);
            }
            catch { }
            return null;
        }

        public static bool HasThemeJs(string slug)
        {
            try { return File.Exists(AssetJsPath(slug)); }
            catch { return false; }
        }

        // Legacy location of the built-in "site" theme's shipped stylesheet,
        // published to /assets/themes/site/style.css by EnsureAssetFiles().
        public static string LegacyCssFilePath(string slug)
        {
            return Path.Combine(HostingEnvironment.MapPath("~/css/theme/"), slug + ".css");
        }

        // ===== Active theme =====
        // Returns the slug the public site should render with. This is the single
        // choke point every render path funnels through, so the guarantee made
        // here - "the returned slug always resolves to a theme that exists" -
        // protects the whole public pipeline from the blank-page failure that
        // happens when active_theme points at a missing/deleted theme.
        //
        // Resolution order:
        //   1. The configured active_theme, IF it is format-valid AND actually
        //      exists as a folder theme or a C# (code) theme.
        //   2. Otherwise DefaultSlug() - the CMS default, which is itself
        //      existence-checked so we never hand back a dead slug.
        public static string GetActiveSlug()
        {
            string slug = Db.GetSetting("active_theme", BuiltinSlug);
            if (IsValidSlug(slug) && ThemeExists(slug)) return slug;
            return DefaultSlug();
        }

        // True when `slug` resolves to an installed theme: either a folder theme
        // (a directory under App_Data/themes/) or a C# theme class in the
        // registry. Format must be valid first.
        public static bool ThemeExists(string slug)
        {
            if (!IsValidSlug(slug)) return false;
            try
            {
                string dir = ThemeFolder(slug);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return true;
            }
            catch { }
            try { return CsTemplate.CsThemeRegistry.Exists(slug); }
            catch { return false; }
        }

        // The CMS default theme slug, guaranteed to exist. Prefers the shipped
        // C# Hearth theme (BuiltinSlug); if that is somehow absent, falls back to
        // any C# theme, then any folder theme. Returns BuiltinSlug as a last
        // resort so callers always get a non-empty, format-valid slug even on a
        // misconfigured install (the template loader then surfaces a clear
        // "template not found" rather than a silent blank page).
        public static string DefaultSlug()
        {
            // 1. The preferred built-in (C# Hearth).
            if (ThemeExists(BuiltinSlug)) return BuiltinSlug;

            // 2. Any installed C# theme.
            try
            {
                var cs = CsTemplate.CsThemeRegistry.All();
                if (cs != null && cs.Count > 0 && !string.IsNullOrEmpty(cs[0].Slug))
                    return cs[0].Slug;
            }
            catch { }

            // 3. Any folder theme.
            try
            {
                string root = ThemesRoot;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string s = Path.GetFileName(dir);
                        if (IsValidSlug(s)) return s;
                    }
                }
            }
            catch { }

            // 4. Last resort.
            return BuiltinSlug;
        }

        public static bool Activate(string slug)
        {
            // A theme is activatable if it's a folder theme OR a C# (code) theme.
            bool isFolderTheme = GetTheme(slug) != null;
            bool isCsTheme = CsTemplate.CsThemeRegistry.Exists(slug);
            if (!isFolderTheme && !isCsTheme) return false;

            Db.SaveSetting("active_theme", slug);
            TemplateEngine.ClearCache();
            // Guard 1: refresh the C# theme slot so the switch takes effect now.
            try { CsTemplate.CsThemeRegistry.Refresh(); } catch { }
            return true;
        }

        // ===== Theme discovery (folders + config.txt) =====
        public static List<ThemeInfo> GetAllThemes()
        {
            var list = new List<ThemeInfo>();
            string root = ThemesRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;

            string active = GetActiveSlug();
            foreach (string dir in Directory.GetDirectories(root))
            {
                string slug = Path.GetFileName(dir);
                if (!IsValidSlug(slug)) continue;
                list.Add(BuildInfo(slug, dir, active));
            }
            list.Sort(delegate (ThemeInfo a, ThemeInfo b)
            {
                if (a.IsActive != b.IsActive) return a.IsActive ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        // Merge C# (code) themes into the library list so they show up and can be
        // activated alongside folder themes. Skips any whose slug coincides with a
        // folder theme already in the list (folder wins the listing slot).
        public static List<ThemeInfo> GetAllThemesIncludingCs()
        {
            var list = GetAllThemes();
            string active = GetActiveSlug();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in list) seen.Add(t.Slug);

            foreach (var cs in CsTemplate.CsThemeRegistry.All())
            {
                if (seen.Contains(cs.Slug)) continue;
                list.Add(new ThemeInfo
                {
                    Slug = cs.Slug,
                    Name = string.IsNullOrEmpty(cs.Name) ? cs.Slug : cs.Name,
                    Description = cs.Description,
                    Author = cs.Author,
                    Url = cs.Url,
                    Version = cs.Version,
                    IsActive = string.Equals(cs.Slug, active, StringComparison.OrdinalIgnoreCase),
                    IsBuiltin = false,
                    HasAssets = Directory.Exists(AssetFolder(cs.Slug)),
                    IsCsTemplate = true
                });
            }

            list.Sort(delegate (ThemeInfo a, ThemeInfo b)
            {
                if (a.IsActive != b.IsActive) return a.IsActive ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static ThemeInfo GetTheme(string slug)
        {
            if (!IsValidSlug(slug)) return null;
            string dir = ThemeFolder(slug);
            if (!Directory.Exists(dir)) return null;
            return BuildInfo(slug, dir, GetActiveSlug());
        }

        static ThemeInfo BuildInfo(string slug, string dir, string activeSlug)
        {
            var info = ParseConfig(dir);
            info.Slug = slug;
            if (string.IsNullOrEmpty(info.Name)) info.Name = slug;
            info.IsActive = string.Equals(slug, activeSlug, StringComparison.OrdinalIgnoreCase);
            info.IsBuiltin = string.Equals(slug, BuiltinSlug, StringComparison.OrdinalIgnoreCase);
            info.HasAssets = Directory.Exists(AssetFolder(slug));
            return info;
        }

        // Parse config.txt. Tolerant: ignores [sections]/#;-comments, accepts
        // both "key = value" and "key: value" separators (whichever comes first).
        static ThemeInfo ParseConfig(string dir)
        {
            var info = new ThemeInfo { Version = "1" };
            try
            {
                string cfg = Path.Combine(dir, "config.txt");
                if (!File.Exists(cfg)) return info;
                foreach (string raw in File.ReadAllLines(cfg))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line[0] == '[' || line[0] == '#' || line[0] == ';') continue;

                    int sep = -1;
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (line[i] == '=' || line[i] == ':') { sep = i; break; }
                    }
                    if (sep <= 0) continue;

                    string key = line.Substring(0, sep).Trim().ToLowerInvariant();
                    string val = line.Substring(sep + 1).Trim();
                    switch (key)
                    {
                        case "name": info.Name = val; break;
                        case "description": info.Description = val; break;
                        case "author": info.Author = val; break;
                        case "url": info.Url = val; break;
                        case "version": info.Version = val; break;
                        case "date": info.Date = val; break;
                    }
                }
            }
            catch { }
            return info;
        }

        // ===== Delete a theme (folder + assets) =====
        public static bool DeleteTheme(string slug, out string error)
        {
            error = "";
            if (!IsValidSlug(slug)) { error = "Invalid theme name."; return false; }
            if (string.Equals(slug, BuiltinSlug, StringComparison.OrdinalIgnoreCase))
            { error = "The default theme cannot be deleted."; return false; }
            if (string.Equals(slug, GetActiveSlug(), StringComparison.OrdinalIgnoreCase))
            { error = "This is the active theme. Activate another theme first."; return false; }
            try
            {
                string tdir = ThemeFolder(slug);
                if (Directory.Exists(tdir)) Directory.Delete(tdir, true);
                string adir = AssetFolder(slug);
                if (Directory.Exists(adir)) Directory.Delete(adir, true);
            }
            catch (Exception ex) { error = ex.Message; return false; }
            return true;
        }

        // ===== File browser (editor left panel) =====
        public static void ListThemeFiles(string slug,
            out List<ThemeFile> topLevel, out List<ThemeFile> components, out List<ThemeFile> assets)
        {
            topLevel = new List<ThemeFile>();
            components = new List<ThemeFile>();
            assets = new List<ThemeFile>();
            if (!IsValidSlug(slug)) return;

            string tdir = ThemeFolder(slug);
            if (Directory.Exists(tdir))
            {
                foreach (string f in Directory.GetFiles(tdir))
                {
                    string nm = Path.GetFileName(f);
                    if (IsEditable(nm)) topLevel.Add(new ThemeFile { Area = "tpl", Path = nm, Name = nm });
                }
                string cdir = Path.Combine(tdir, "components");
                if (Directory.Exists(cdir))
                {
                    foreach (string f in Directory.GetFiles(cdir))
                    {
                        string nm = Path.GetFileName(f);
                        if (IsEditable(nm)) components.Add(new ThemeFile { Area = "tpl", Path = "components/" + nm, Name = nm });
                    }
                }
            }

            string adir = AssetFolder(slug);
            if (Directory.Exists(adir))
            {
                CollectAssets(adir, adir, assets);
            }

            topLevel.Sort(CompareByName);
            components.Sort(CompareByName);
            assets.Sort(CompareByName);
        }

        static void CollectAssets(string rootDir, string curDir, List<ThemeFile> acc)
        {
            foreach (string f in Directory.GetFiles(curDir))
            {
                string nm = Path.GetFileName(f);
                if (!IsEditable(nm)) continue;
                string rel = f.Substring(rootDir.Length).Replace('\\', '/').TrimStart('/');
                acc.Add(new ThemeFile { Area = "asset", Path = rel, Name = rel });
            }
            // one level of sub-folders (e.g. css/, js/) is enough for theme assets
            foreach (string d in Directory.GetDirectories(curDir))
            {
                string dn = Path.GetFileName(d);
                if (string.Equals(dn, "img", StringComparison.OrdinalIgnoreCase)) continue; // skip image dirs
                foreach (string f in Directory.GetFiles(d))
                {
                    string nm = Path.GetFileName(f);
                    if (!IsEditable(nm)) continue;
                    string rel = f.Substring(rootDir.Length).Replace('\\', '/').TrimStart('/');
                    acc.Add(new ThemeFile { Area = "asset", Path = rel, Name = rel });
                }
            }
        }

        static int CompareByName(ThemeFile a, ThemeFile b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsEditable(string fileName)
        {
            string ext = Path.GetExtension(fileName ?? "");
            return EditableExts.Contains(ext);
        }

        // ===== File read / write (path-safe) =====
        static string Resolve(string slug, string area, string rel)
        {
            if (!IsValidSlug(slug)) return null;
            string root = string.Equals(area, "asset", StringComparison.OrdinalIgnoreCase)
                ? AssetFolder(slug) : ThemeFolder(slug);
            string relClean = (rel ?? "").Replace('\\', '/').TrimStart('/');
            if (relClean.Length == 0) return null;
            if (!IsEditable(relClean)) return null;

            string full = Path.GetFullPath(Path.Combine(root, relClean.Replace('/', Path.DirectorySeparatorChar)));
            string rootFull = Path.GetFullPath(root);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;
            return full;
        }

        public static string ReadThemeFile(string slug, string area, string rel)
        {
            string p = Resolve(slug, area, rel);
            if (p == null || !File.Exists(p)) return null;
            try { return File.ReadAllText(p); }
            catch { return null; }
        }

        public static bool WriteThemeFile(string slug, string area, string rel, string content, out string error)
        {
            error = "";
            string p = Resolve(slug, area, rel);
            if (p == null) { error = "Invalid file path or unsupported file type."; return false; }
            try
            {
                string dir = Path.GetDirectoryName(p);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, content ?? "");
            }
            catch (Exception ex) { error = ex.Message; return false; }
            return true;
        }

        public static bool ThemeFileExists(string slug, string rel)
        {
            string p = Resolve(slug, "tpl", rel);
            return p != null && File.Exists(p);
        }

        // ===== Per-theme home content (the theme-specific home editor) =====
        // Editable home regions are plain-text leaf elements the theme author
        // marks with data-edit="key" in home.html. The literal text is the
        // default; admin overrides live in the theme folder's home.values.json.
        public static Dictionary<string, string> ReadHomeValues(string slug)
        {
            var dict = new Dictionary<string, string>();
            try
            {
                if (!IsValidSlug(slug)) return dict;
                string p = Path.Combine(ThemeFolder(slug), "home.values.json");
                if (!File.Exists(p)) return dict;
                var parsed = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<Dictionary<string, string>>(File.ReadAllText(p));
                if (parsed != null) dict = parsed;
            }
            catch { }
            return dict;
        }

        public static bool WriteHomeValues(string slug, Dictionary<string, string> values, out string error)
        {
            error = "";
            if (!IsValidSlug(slug)) { error = "Invalid theme."; return false; }
            try
            {
                string p = Path.Combine(ThemeFolder(slug), "home.values.json");
                File.WriteAllText(p, Newtonsoft.Json.JsonConvert.SerializeObject(
                    values ?? new Dictionary<string, string>(), Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex) { error = ex.Message; return false; }
            return true;
        }

        // Replaces the inner text of each data-edit="key" leaf element in the
        // rendered home HTML with its saved value. Leaf elements only (inner
        // content is treated as a single text node).
        public static string ApplyHomeEdits(string html, Dictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(html) || values == null || values.Count == 0) return html;
            foreach (var kv in values)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                string key = kv.Key, val = kv.Value ?? "";
                // A key maps to exactly one marker; the others no-op.
                html = ReplaceEditable(html, key, val);                       // data-edit       -> inner text
                html = SetAttr(html, "data-edit-href", key, "href", val);     // data-edit-href  -> href
                html = SetAttr(html, "data-edit-src", key, "src", val);       // data-edit-src   -> src (<img>)
                html = SetAttr(html, "data-edit-icon", key, "class", val);    // data-edit-icon  -> class (Font Awesome)
                html = SetBg(html, key, val);                                 // data-edit-bg    -> style background-image
            }
            return html;
        }

        static string ReplaceEditable(string html, string key, string value)
        {
            int attr = IndexOfEditAttr(html, key);
            if (attr < 0) return html;

            int lt = html.LastIndexOf('<', attr);
            if (lt < 0) return html;

            int p = lt + 1, ns = p;
            while (p < html.Length && (char.IsLetterOrDigit(html[p]) || html[p] == '-' || html[p] == '_')) p++;
            if (p == ns) return html;
            string tag = html.Substring(ns, p - ns);

            int gt = html.IndexOf('>', attr);
            if (gt < 0 || gt == 0) return html;
            if (html[gt - 1] == '/') return html; // self-closing: no inner text

            int ce = html.IndexOf("</" + tag, gt + 1, StringComparison.OrdinalIgnoreCase);
            if (ce < 0) return html;

            return html.Substring(0, gt + 1)
                + System.Web.HttpUtility.HtmlEncode(value)
                + html.Substring(ce);
        }

        static int IndexOfEditAttr(string html, string key)
        {
            int i1 = html.IndexOf("data-edit='" + key + "'", StringComparison.Ordinal);
            int i2 = html.IndexOf("data-edit=\"" + key + "\"", StringComparison.Ordinal);
            if (i1 < 0) return i2;
            if (i2 < 0) return i1;
            return Math.Min(i1, i2);
        }

        // Sets (or injects) targetAttr's value on the element carrying marker="key".
        // Used for href, src and class (Font Awesome icon). Value is attribute-encoded.
        static string SetAttr(string html, string marker, string key, string targetAttr, string value)
        {
            int attr = IndexOfMarker(html, marker, key);
            if (attr < 0) return html;
            int lt = html.LastIndexOf('<', attr); if (lt < 0) return html;
            int gt = html.IndexOf('>', attr); if (gt < 0) return html;

            string enc = System.Web.HttpUtility.HtmlAttributeEncode(value ?? "");
            int ta = FindAttrInRange(html, targetAttr, lt, gt);
            if (ta >= 0)
            {
                int eq = html.IndexOf('=', ta); if (eq < 0 || eq > gt) return html;
                int q = eq + 1; while (q < gt && html[q] == ' ') q++;
                if (q >= gt || (html[q] != '\'' && html[q] != '"')) return html;
                char qc = html[q];
                int vs = q + 1;
                int ve = html.IndexOf(qc, vs); if (ve < 0 || ve > gt) return html;
                return html.Substring(0, vs) + enc + html.Substring(ve);
            }
            // attribute absent: inject it right after the tag name
            int p = lt + 1;
            while (p < gt && (char.IsLetterOrDigit(html[p]) || html[p] == '-' || html[p] == '_')) p++;
            return html.Substring(0, p) + " " + targetAttr + "='" + enc + "'" + html.Substring(p);
        }

        // Sets the background-image of the element carrying data-edit-bg="key",
        // merging into any existing inline style (other declarations are kept).
        static string SetBg(string html, string key, string value)
        {
            int attr = IndexOfMarker(html, "data-edit-bg", key);
            if (attr < 0) return html;
            int lt = html.LastIndexOf('<', attr); if (lt < 0) return html;
            int gt = html.IndexOf('>', attr); if (gt < 0) return html;

            string url = System.Web.HttpUtility.HtmlAttributeEncode(value ?? "");
            int st = FindAttrInRange(html, "style", lt, gt);
            if (st >= 0)
            {
                int eq = html.IndexOf('=', st); if (eq < 0 || eq > gt) return html;
                int q = eq + 1; while (q < gt && html[q] == ' ') q++;
                if (q >= gt || (html[q] != '\'' && html[q] != '"')) return html;
                char qc = html[q];
                int vs = q + 1;
                int ve = html.IndexOf(qc, vs); if (ve < 0 || ve > gt) return html;

                string styleVal = html.Substring(vs, ve - vs);
                styleVal = System.Text.RegularExpressions.Regex.Replace(styleVal,
                    "background-image\\s*:[^;]*;?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                string innerQuote = qc == '"' ? "'" : "\"";
                string decl = string.IsNullOrEmpty(value)
                    ? "background-image:none"
                    : "background-image:url(" + innerQuote + url + innerQuote + ")";
                string merged = decl + (styleVal.Length > 0 ? ";" + styleVal : "");
                return html.Substring(0, vs) + merged + html.Substring(ve);
            }
            // no style attribute yet: inject one
            int p = lt + 1;
            while (p < gt && (char.IsLetterOrDigit(html[p]) || html[p] == '-' || html[p] == '_')) p++;
            string d = string.IsNullOrEmpty(value) ? "background-image:none" : "background-image:url(\"" + url + "\")";
            return html.Substring(0, p) + " style='" + d + "'" + html.Substring(p);
        }

        static int IndexOfMarker(string html, string marker, string key)
        {
            int i1 = html.IndexOf(marker + "='" + key + "'", StringComparison.Ordinal);
            int i2 = html.IndexOf(marker + "=\"" + key + "\"", StringComparison.Ordinal);
            if (i1 < 0) return i2;
            if (i2 < 0) return i1;
            return Math.Min(i1, i2);
        }

        // Finds attribute `name` as a whole token within html[from..to).
        static int FindAttrInRange(string html, string name, int from, int to)
        {
            int i = from;
            while (i < to)
            {
                int idx = html.IndexOf(name, i, StringComparison.OrdinalIgnoreCase);
                if (idx < 0 || idx >= to) return -1;
                char before = idx > 0 ? html[idx - 1] : ' ';
                int after = idx + name.Length;
                char afterCh = after < html.Length ? html[after] : ' ';
                bool okBefore = before == ' ' || before == '\t' || before == '\n' || before == '\r';
                bool okAfter = afterCh == '=' || afterCh == ' ' || afterCh == '\t';
                if (okBefore && okAfter) return idx;
                i = idx + name.Length;
            }
            return -1;
        }

        // Publishes the legacy built-in "site" stylesheet to its asset folder so
        // any install still pointing active_theme at "site" keeps its styling.
        // Folder themes ship their own committed assets, so nothing else to do.
        public static void EnsureAssetFiles()
        {
            try
            {
                string legacy = LegacyCssFilePath("site");
                if (File.Exists(legacy))
                {
                    string folder = AssetFolder("site");
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    File.WriteAllText(AssetCssPath("site"), File.ReadAllText(legacy));
                }
            }
            catch { }
        }
    }

    // Lightweight theme metadata read from a theme folder's config.txt.
    public class ThemeInfo
    {
        public string Slug = "";
        public string Name = "";
        public string Description = "";
        public string Author = "";
        public string Url = "";
        public string Version = "";
        public string Date = "";
        public bool IsActive = false;
        public bool IsBuiltin = false;
        public bool HasAssets = false;
        public bool IsCsTemplate = false;
    }

    // One editable file in a theme. Area is "tpl" (App_Data/themes/{slug}/)
    // or "asset" (assets/themes/{slug}/). Path is relative to that area root.
    public class ThemeFile
    {
        public string Area = "tpl";
        public string Path = "";
        public string Name = "";
    }
}

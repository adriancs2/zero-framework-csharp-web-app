using System.Collections.Generic;

using System.Reflection;

namespace System.engine.CsTemplate
{
    // ============================================================
    // CsThemeRegistry - resolves the active C# theme.
    //
    // A C# theme is any non-abstract subclass of CsTemplate. This registry
    // finds the one whose Slug matches the active_theme setting and caches a
    // single instance (themes are stateless, so one shared instance is reused
    // across requests).
    //
    // C# theme classes are compiled into the main project assembly (into /bin
    // by Visual Studio / msbuild). The type scan covers that assembly plus every
    // assembly BuildManager reports, so additional plugin/host assemblies that
    // define CsTemplate subclasses are still discovered if present.
    //
    // Three guards keep the cached instance correct (all share ResolveActive,
    // which validates the cache against the LIVE active slug so switching
    // between two C# themes can't serve a stale one):
    //   1. On activation  - ThemeManager.Activate() calls Refresh().
    //   2. On startup      - Application_Start calls Refresh().
    //   3. Lazy            - the render hook calls Active; if the cache is
    //                        null/stale it is rebuilt on demand.
    //
    // Reflection over the assembly type lists happens only when the active
    // slug changes (theme switch or app recycle) - never per request. The hot
    // path is a slug comparison plus, at most, one reflected instantiation.
    // ============================================================
    public static class CsThemeRegistry
    {
        static readonly object _lock = new object();

        // The currently active C# theme instance, or null when the active theme
        // is a folder (HTML) theme.
        static CsTemplate _active;

        // The slug _active was built for, so we can detect a stale cache.
        static string _activeSlug;

        // True when the active theme is a C# theme. The render sites check this
        // first; when false the existing HTML-theme path runs unchanged.
        public static bool IsActiveCsTemplate
        {
            get { return Active != null; }
        }

        // The active C# theme (guard 3: lazy). Returns null for HTML themes.
        public static CsTemplate Active
        {
            get
            {
                string slug = ThemeManager.GetActiveSlug();
                // Fast path: cache already matches the live active slug.
                if (_active != null && string.Equals(_activeSlug, slug, StringComparison.OrdinalIgnoreCase))
                    return _active;
                // Also fast: we already determined this slug is NOT a C# theme.
                if (_active == null && string.Equals(_activeSlug, slug, StringComparison.OrdinalIgnoreCase))
                    return null;
                return Resolve(slug);
            }
        }

        // Guards 1 & 2: force a rebuild from the current active slug.
        public static void Refresh()
        {
            Resolve(ThemeManager.GetActiveSlug());
        }

        // Rebuild the single-slot cache for `slug`. Sets _active to the matching
        // theme instance, or null if no C# theme claims that slug.
        static CsTemplate Resolve(string slug)
        {
            lock (_lock)
            {
                _active = InstantiateBySlug(slug);
                _activeSlug = slug;
                return _active;
            }
        }

        // Reflection: find the non-abstract CsTemplate subclass whose Slug equals
        // `slug`, and instantiate it. Returns null if none match.
        static CsTemplate InstantiateBySlug(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return null;
            try
            {
                foreach (var t in CsTemplateTypes())
                {
                    CsTemplate inst;
                    try { inst = (CsTemplate)Activator.CreateInstance(t); }
                    catch { continue; }
                    if (inst != null && string.Equals(inst.Slug, slug, StringComparison.OrdinalIgnoreCase))
                        return inst;
                }
            }
            catch { }
            return null;
        }

        // The assemblies that may contain C# themes: the main project assembly
        // (which now holds the theme classes) plus everything BuildManager
        // references, so any additional host/plugin assembly is also covered.
        static IEnumerable<Assembly> CandidateAssemblies()
        {
            var list = new List<Assembly> { typeof(CsTemplate).Assembly };
            try
            {
                // cshttp build: no BuildManager - every assembly loaded into the
                // process (the app plus any plugin assembly) is a candidate.
                var refs = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly a in refs)
                {
                    if (a != null && !list.Contains(a)) list.Add(a);
                }
            }
            catch { /* not hosted (tests/tools): main assembly only */ }
            return list;
        }

        // All concrete CsTemplate subclasses across the candidate assemblies.
        static IEnumerable<Type> CsTemplateTypes()
        {
            var result = new List<Type>();
            foreach (var asm in CandidateAssemblies())
            {
                // System/framework assemblies can't contain themes; skip the
                // expensive GetTypes() for them.
                string name = "";
                try { name = asm.GetName().Name ?? ""; } catch { }
                if (name.StartsWith("System", StringComparison.OrdinalIgnoreCase) && asm != typeof(CsTemplate).Assembly) continue;
                if (name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                if (name == "mscorlib" || name == "netstandard" || name == "Newtonsoft.Json") continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }   // may contain nulls; the loop below skips them
                catch { continue; }

                foreach (var t in types)
                {
                    if (t != null && t.IsClass && !t.IsAbstract && typeof(CsTemplate).IsAssignableFrom(t))
                        result.Add(t);
                }
            }
            return result;
        }

        // Metadata for every installed C# theme, so the Themes library can list
        // them alongside folder themes. Instantiates each once (cheap, stateless)
        // purely to read its metadata; does not affect the active-slot cache.
        public static List<CsTemplate> All()
        {
            var list = new List<CsTemplate>();
            foreach (var t in CsTemplateTypes())
            {
                try
                {
                    var inst = (CsTemplate)Activator.CreateInstance(t);
                    if (inst != null) list.Add(inst);
                }
                catch { }
            }
            return list;
        }

        // True if `slug` is claimed by some C# theme (used by the Themes library
        // / activation to know a slug is a valid C# theme without folder files).
        public static bool Exists(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return false;
            foreach (var t in All())
            {
                if (t != null && string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}

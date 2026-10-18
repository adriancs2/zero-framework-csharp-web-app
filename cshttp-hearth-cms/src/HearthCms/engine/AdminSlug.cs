using System.IO;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // Admin URL slug resolution.
    //
    // The admin panel normally lives under "/admin". The user can rename that
    // first path segment to anything (e.g. "/backend", "/manage") so the panel
    // is served from a custom, less-guessable URL. Only the resolved slug works;
    // the literal "/admin" 404s once a different slug is active (unless the
    // resolved slug IS "admin").
    //
    // Resolution priority (highest wins):
    //   1. /App_Data/config.txt   ->  admin_url={slug}      (recovery / override)
    //   2. DB setting             ->  admin_url             (saved from Settings)
    //   3. hardcoded default      ->  "admin"
    //
    // config.txt is read ONCE at app start (and on demand via /reset_app), not on
    // every request, so a forgotten custom slug is recovered by editing the file
    // and hitting /reset_app — no code or DB access required.
    //
    // Login model: there is no separate "/login" route. The admin slug root is
    // the only entry point — an unauthenticated request to the slug root (or any
    // admin path) is shown the login page in place. Once authenticated, the slug
    // is no longer a barrier and every canonical "/admin/..." link works, so the
    // slug guards public discovery only.
    // ============================================================
    public static class AdminSlug
    {
        public const string DefaultSlug = "admin";
        public const string SettingKey = "admin_url";

        // The slug parsed from config.txt at the last load, or null if the file
        // is absent / has no admin_url line. config.txt overrides the DB value.
        static string _configOverride = null;
        static bool _configLoaded = false;
        static readonly object _lock = new object();

        // Optional dev_mode override parsed from the SAME config.txt read.
        //   null  -> no dev_mode line in config.txt (fall back to the DB setting)
        //   true  -> dev_mode=true|1|on|yes  (force Dev Mode ON,  file wins)
        //   false -> dev_mode=false|0|off|no (force Dev Mode OFF, file wins)
        // See Settings.IsDevMode for how this composes with the DB setting.
        static bool? _configDevMode = null;

        // The dev_mode override from config.txt, or null when the file doesn't
        // set it. Triggers a config load on first use, like Current.
        public static bool? ConfigDevMode
        {
            get
            {
                if (!_configLoaded) LoadConfigOverride();
                return _configDevMode;
            }
        }

        // ---------- Public resolution ----------

        // The effective admin slug, honoring config.txt > DB > default.
        public static string Current
        {
            get
            {
                if (!_configLoaded) LoadConfigOverride();

                if (IsValid(_configOverride)) return _configOverride;

                string fromDb = Db.GetSetting(SettingKey, "");
                if (IsValid(fromDb)) return fromDb.ToLowerInvariant();

                return DefaultSlug;
            }
        }

        // True when config.txt is currently pinning the slug (DB value ignored).
        public static bool IsLockedByConfig
        {
            get
            {
                if (!_configLoaded) LoadConfigOverride();
                return IsValid(_configOverride);
            }
        }

        // ---------- Routing helpers ----------

        // If `path` begins with the custom admin segment, return the equivalent
        // canonical path under "/admin" so the existing router can match it
        // unchanged. Returns null when `path` is not an admin-slug request.
        //
        // Examples (slug = "backend"):
        //   "/backend"            -> "/admin"
        //   "/backend/settings"   -> "/admin/settings"
        //   "/backend/themes/x"   -> "/admin/themes/x"
        //   "/admin/settings"     -> null   (literal /admin is hidden)
        //   "/posts"              -> null
        public static string ToCanonical(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string slug = Current;

            // When the slug IS "admin", the canonical and public paths coincide;
            // no rewrite needed and /admin must keep working normally.
            if (string.Equals(slug, DefaultSlug, StringComparison.Ordinal))
                return null;

            string prefix = "/" + slug;
            if (path.Equals(prefix, StringComparison.Ordinal))
                return "/admin";
            if (path.StartsWith(prefix + "/", StringComparison.Ordinal))
                return "/admin" + path.Substring(prefix.Length);

            return null;
        }

        // True if `path` targets the literal "/admin" tree while a DIFFERENT slug
        // is active — i.e. a path that must be hidden (404) from the public.
        public static bool IsHiddenDefaultPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (string.Equals(Current, DefaultSlug, StringComparison.Ordinal))
                return false;   // default slug active -> /admin is legitimate
            return path == "/admin" || path.StartsWith("/admin/", StringComparison.Ordinal);
        }

        // ---------- config.txt ----------

        // Path to the app-level config file. Distinct from per-theme config.txt.
        static string ConfigPath()
        {
            string dataDir = HttpContext.Current.Server.MapPath("~/App_Data/");
            return Path.Combine(dataDir, "config.txt");
        }

        // Read /App_Data/config.txt and cache any admin_url override in memory.
        // Safe to call repeatedly; only the parsed result is kept.
        public static void LoadConfigOverride()
        {
            lock (_lock)
            {
                _configOverride = null;
                _configDevMode = null;
                _configLoaded = true;
                try
                {
                    string path = ConfigPath();
                    if (!File.Exists(path)) return;

                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = (raw ?? "").Trim();
                        if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;

                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = line.Substring(eq + 1).Trim();
                        if (key == SettingKey && IsValid(val))
                        {
                            _configOverride = val.ToLowerInvariant();
                        }
                        else if (key == "dev_mode")
                        {
                            // Accept the common truthy/falsy spellings; anything
                            // unrecognized leaves the override unset (null).
                            switch (val.ToLowerInvariant())
                            {
                                case "true": case "1": case "on": case "yes":
                                    _configDevMode = true; break;
                                case "false": case "0": case "off": case "no":
                                    _configDevMode = false; break;
                            }
                        }
                    }
                }
                catch { _configOverride = null; _configDevMode = null; }
            }
        }

        // Forces a fresh read of config.txt (used by /reset_app).
        public static void Reload()
        {
            _configLoaded = false;
            LoadConfigOverride();
        }

        // ---------- Validation ----------

        // A valid slug is a single non-empty path segment of url-safe characters,
        // and must not collide with a reserved top-level route.
        public static bool IsValid(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return false;
            slug = slug.Trim();
            if (slug.Length == 0 || slug.Length > 40) return false;

            foreach (char c in slug)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }

            switch (slug.ToLowerInvariant())
            {
                case "home":
                case "latest-post":
                case "categories-latest-post":
                case "logout":
                case "api":
                case "category":
                case "reset_app":
                case "favicon.ico":
                case "robots.txt":
                    return false;
            }
            return true;
        }
    }
}

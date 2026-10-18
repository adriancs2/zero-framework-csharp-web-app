using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    public static class Db
    {
        public static readonly ConcurrentDictionary<string, string> SettingsCache =
            new ConcurrentDictionary<string, string>();

        // ===== Schema version registry =====
        // Each entry is an additive upgrade applied to an EXISTING database whose
        // stored "current_db_version" is below the entry's key. Keys are applied
        // in ascending numeric order; after the loop the highest key becomes the
        // new stored version. To ship a schema change, add the next integer key
        // with its SQL — never edit or renumber an existing entry, since installs
        // in the field have already run the old ones.
        //
        // A fresh install (RunInstall -> CreateSchema) already contains every
        // column, so a brand-new DB is stamped directly at the latest version and
        // skips the upgrade loop entirely.
        public static readonly Dictionary<int, string> dicSql = new Dictionary<int, string>
        {
            // Example (kept as documentation of the format; already present in
            // CreateSchema, guarded, so harmless if it ever runs on an old DB):
            // { 1, "ALTER TABLE posts ADD COLUMN category TEXT NOT NULL DEFAULT '';" },

            // 1-2: remember-me grace window. The previous validator hash plus the
            // time it was superseded let TryRestore tolerate the just-rotated token
            // for a few seconds, so concurrent requests from one client (a browser
            // restoring several admin tabs at once, all carrying the not-yet-rotated
            // cookie) are not misread as token theft. See RememberMe.TryRestore.
            { 1, "ALTER TABLE user_sessions ADD COLUMN prev_validator_hash TEXT NOT NULL DEFAULT '';" },
            { 2, "ALTER TABLE user_sessions ADD COLUMN rotated_unix INTEGER NOT NULL DEFAULT 0;" },
            { 3, "ALTER TABLE posts ADD COLUMN is_pinned INTEGER NOT NULL DEFAULT 0;" },
        };

        // The version a freshly-created database is stamped with: the largest key
        // in dicSql, or 0 when the registry is empty.
        public static int LatestDbVersion
        {
            get
            {
                int max = 0;
                foreach (int k in dicSql.Keys) if (k > max) max = k;
                return max;
            }
        }

        // Settings keys used by the upgrade machinery.
        public const string DbVersionKey = "current_db_version";
        public const string UpgradeStatusKey = "db_upgrade_log";

        public static void Initialize()
        {
            string path = Config.GetDbPath();

            // ===== Install model: "installed" == the database file exists =====
            // We do NOT create the file here. When it is absent the app is
            // uninstalled and the router sends every request to the setup page,
            // which calls RunInstall() to create + seed the DB on submit. Once the
            // file exists it is never treated as uninstalled again.
            if (!File.Exists(path)) return;

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    // Existing DB: ensure schema objects exist (idempotent), run
                    // any pending version upgrades, then load the settings cache.
                    CreateSchema(s);
                    LoadSettingsCache(s);   // load first so version read is cached
                    RunSchemaUpgrades(s);
                    LoadSettingsCache(s);   // reload in case an upgrade touched settings
                }
            }
        }

        // ===== Schema upgrade runner =====
        // Reads "current_db_version" from settings, applies every dicSql entry
        // whose key is greater (ascending), and stores the new highest version.
        // Each upgrade is logged to /App_Data/db_upgrade_log/{timestamp}.txt and a
        // human-readable status line is written to the settings table for the
        // dashboard to display.
        //
        // On failure: the loop stops at the failing version (it is NOT recorded as
        // applied, so a fixed build can retry from there), the error + stack is
        // written to the log file, and the status line records the failing version
        // and log filename.
        static void RunSchemaUpgrades(SQLiteExpress s)
        {
            int current = 0;
            int.TryParse(GetSetting(DbVersionKey, "0"), out current);

            int latest = LatestDbVersion;

            // Nothing registered, or already up to date: clear any stale error
            // status and make sure the version is stamped.
            if (latest <= current)
            {
                if (GetSetting(DbVersionKey, "") == "")
                    SaveSetting(DbVersionKey, current.ToString());
                return;
            }

            // Collect and sort the pending keys ascending (no LINQ).
            var pending = new List<int>();
            foreach (int k in dicSql.Keys)
                if (k > current) pending.Add(k);
            pending.Sort();

            int applied = current;
            foreach (int ver in pending)
            {
                string sql = dicSql[ver];
                try
                {
                    s.Execute(sql);
                    applied = ver;
                    SaveSetting(DbVersionKey, applied.ToString());
                    WriteUpgradeLog(ver, sql, null);
                }
                catch (Exception ex)
                {
                    string logFile = WriteUpgradeLog(ver, sql, ex);
                    SaveSetting(UpgradeStatusKey,
                        "DB Version " + ver + ". Upgrade error, refer log file " + logFile);
                    // Stop: do not advance past a failed upgrade.
                    return;
                }
            }

            // All pending upgrades succeeded: clear the status line.
            SaveSetting(UpgradeStatusKey, "");
        }

        // Append-only upgrade log. Creates /App_Data/db_upgrade_log/ on demand and
        // writes one file per upgrade attempt, named {yyyy-MM-dd_HHmmss}.txt.
        // Returns the bare file name (no path) for storage in the status line.
        // Best-effort: never throws (logging must not mask the original error).
        static string WriteUpgradeLog(int version, string sql, Exception error)
        {
            string fileName = DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".txt";
            try
            {
                string dir = HttpContext.Current.Server.MapPath("~/App_Data/db_upgrade_log/");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Timestamp : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("DB Version: " + version);
                sb.AppendLine("Result    : " + (error == null ? "SUCCESS" : "ERROR"));
                sb.AppendLine();
                sb.AppendLine("SQL:");
                sb.AppendLine(sql ?? "");
                if (error != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("Error:");
                    sb.AppendLine(error.Message);
                    sb.AppendLine();
                    sb.AppendLine("Stack trace:");
                    sb.AppendLine(error.StackTrace ?? "");
                }

                string full = Path.Combine(dir, fileName);
                File.WriteAllText(full, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { /* logging is best-effort */ }
            return fileName;
        }

        // The human-readable upgrade status for the dashboard. Empty when the last
        // run succeeded (dashboard shows nothing); a "DB Version x. Upgrade error,
        // refer log file ..." string when the last run failed.
        public static string UpgradeStatus
        {
            get { return GetSetting(UpgradeStatusKey, ""); }
        }

        // True once the site has been installed. Under the new model this is
        // simply: the database file exists on disk. Creating the file (RunInstall)
        // is therefore the one and only "installation"; there is no flag to flip
        // and no way to re-enter the installer afterwards.
        //
        // The value is cached so the per-request gate in Application_BeginRequest
        // does not hit the disk on every request. It is resolved once at app start
        // via RefreshInstalledFlag() and set to true immediately when RunInstall
        // creates the database. Because "uninstall" is not a runtime operation
        // (the file only ever appears, never disappears while running), a cached
        // flag is safe — nothing flips it back to false during a process lifetime.
        static bool _installed = false;

        public static bool IsInstalled
        {
            get { return _installed; }
        }

        // Re-resolve the cached install flag from disk. Called once at app start
        // (Application_Start) before the first request is routed. Safe to call
        // again at any time.
        public static void RefreshInstalledFlag()
        {
            try { _installed = File.Exists(Config.GetDbPath()); }
            catch { _installed = false; }
        }

        // ============================================================
        // ===== Unified installation =====
        // RunInstall is the ONE entry point for first-run installation. It is the
        // only place that creates the database file, and everything a fresh site
        // needs happens here, in order, in one shot:
        //
        //   1. Validate the submitted setup form.
        //   2. Create the database file (the site is "installed" from here on).
        //   3. CreateSchema           - full, current schema (no upgrades needed).
        //   4. SeedSettings           - all default settings + the submitted
        //                               site name, footer and admin slug.
        //   5. SeedAdminUser          - the first (and only) admin login.
        //   6. SeedStarterContent     - About/Contact pages + three sample posts,
        //                               via the InsertPage/InsertPost helpers.
        //   7. SeedNavigation         - the default nav_menu JSON.
        //   8. Stamp current_db_version at the latest version so the upgrade
        //      loop never re-applies migrations already baked into CreateSchema.
        //
        // Category seeding is intentionally NOT run. Returns false with an error
        // message on validation failure or if the DB already exists.
        // ============================================================
        public static bool RunInstall(string siteName, string username, string password,
            string adminPath, string email, out string error)
        {
            error = "";
            siteName = (siteName ?? "").Trim();
            username = (username ?? "").Trim();
            password = (password ?? "").Trim();
            adminPath = (adminPath ?? "").Trim();

            // ----- 1. Validation -----
            if (siteName.Length == 0) { error = "Site name is required."; return false; }
            if (username.Length == 0) { error = "Admin username is required."; return false; }
            if (password.Length == 0) { error = "Admin password is required."; return false; }

            // Admin path is optional; when supplied it must be a valid slug.
            if (adminPath.Length > 0 && !AdminSlug.IsValid(adminPath))
            {
                error = "Admin login path is not valid. Use letters, numbers, dashes or " +
                        "underscores, and avoid reserved words.";
                return false;
            }

            if (IsInstalled) { error = "This site is already installed."; return false; }

            // ----- 2. Create the database file -----
            string path = Config.GetDbPath();
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                SQLiteConnection.CreateFile(path);
            }
            catch (Exception ex)
            {
                error = "Could not create the database: " + ex.Message;
                return false;
            }

            // The file now exists on disk: the site is installed from here on.
            // Update the cached flag immediately so the post-install redirect and
            // every following request see the installed state without a disk read.
            _installed = true;

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    // ----- 3..7. Schema + all seeding, in one place -----
                    CreateSchema(s);
                    SeedSettings(s, siteName, adminPath);
                    SeedAdminUser(s, username, password, email);
                    SeedStarterContent(s);
                    SeedNavigation(s);

                    LoadSettingsCache(s);
                }
            }

            // ----- 8. Stamp the schema at the current latest version -----
            SaveSetting(DbVersionKey, LatestDbVersion.ToString());
            SaveSetting(UpgradeStatusKey, "");

            return true;
        }

        // ===== Admin credential reset (one-shot file) =====
        // Looks for /App_Data/reset_admin.txt. If present and it carries a valid
        // admin_username / admin_password pair, the admin login is reset to those
        // values and the file is consumed so the secret cannot be replayed.
        //
        // Safety ordering (the file is neutralised BEFORE the DB is touched):
        //   1. read the values
        //   2. attempt to DELETE the file
        //   3. if delete fails, attempt to BLANK the file's contents
        //   4. only if the file was successfully deleted or blanked, apply the
        //      reset to the database
        // If neither delete nor blank succeeds, nothing is applied — the secret
        // could otherwise be replayed on the next startup.
        //
        // Returns true if a reset was applied. Safe to call when the file is
        // absent (returns false). Called on startup and on /reset_app.
        public static bool TryConsumeAdminReset()
        {
            string path;
            try { path = Path.Combine(HttpContext.Current.Server.MapPath("~/App_Data/"), "reset_admin.txt"); }
            catch { return false; }

            try
            {
                if (!File.Exists(path)) return false;

                // No database yet (site not installed): there is no users table to
                // reset against, and opening a connection would create an empty DB
                // file that would falsely mark the site installed. Bail out.
                if (!File.Exists(Config.GetDbPath())) return false;

                // 1. Read the requested credentials.
                string username = null, password = null;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "admin_username") username = val;
                    else if (key == "admin_password") password = val;
                }

                // 2/3. Neutralise the file FIRST: delete, else blank. If we can do
                // neither, refuse to apply so the secret can't be replayed later.
                bool neutralised = false;
                try { File.Delete(path); neutralised = true; }
                catch
                {
                    try
                    {
                        File.WriteAllText(path, "");
                        neutralised = true;
                    }
                    catch { neutralised = false; }
                }
                if (!neutralised) return false;

                // Validate only after the file is safe.
                username = (username ?? "").Trim();
                password = (password ?? "").Trim();
                if (username.Length == 0 || password.Length == 0) return false;

                // 4. Apply: update the existing admin (or create one if none).
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var existing = s.GetObject<obUser>("SELECT * FROM users ORDER BY id LIMIT 1;");
                        if (existing != null && existing.Id > 0)
                        {
                            var d = new Dictionary<string, object>();
                            d["username"] = username;
                            d["password_hash"] = Auth.Hash(password);
                            s.Update("users", d, "id", existing.Id);
                        }
                        else
                        {
                            var u = new Dictionary<string, object>();
                            u["username"] = username;
                            u["email"] = "";
                            u["password_hash"] = Auth.Hash(password);
                            u["display_name"] = "Administrator";
                            u["role"] = "admin";
                            u["date_created"] = DateTime.UtcNow;
                            s.Insert("users", u);
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }

        static void CreateSchema(SQLiteExpress s)
        {
            s.Execute(@"
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL UNIQUE,
                    email TEXT NOT NULL DEFAULT '',
                    password_hash TEXT NOT NULL DEFAULT '',
                    display_name TEXT NOT NULL DEFAULT '',
                    role TEXT NOT NULL DEFAULT 'admin',
                    date_created DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00'
                );");

            s.Execute(@"
                CREATE TABLE IF NOT EXISTS user_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    selector TEXT NOT NULL UNIQUE,
                    validator_hash TEXT NOT NULL,
                    prev_validator_hash TEXT NOT NULL DEFAULT '',
                    rotated_unix INTEGER NOT NULL DEFAULT 0,
                    user_id INTEGER NOT NULL,
                    expires_at DATETIME NOT NULL,
                    date_created DATETIME NOT NULL
                );");

            s.Execute("CREATE INDEX IF NOT EXISTS idx_user_sessions_expires ON user_sessions(expires_at);");

            s.Execute(@"
                CREATE TABLE IF NOT EXISTS pages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    slug TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL DEFAULT '',
                    content TEXT NOT NULL DEFAULT '',
                    excerpt TEXT NOT NULL DEFAULT '',
                    is_published INTEGER NOT NULL DEFAULT 0,
                    show_in_nav INTEGER NOT NULL DEFAULT 0,
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    author_id INTEGER NOT NULL DEFAULT 0,
                    content_format TEXT NOT NULL DEFAULT 'html',
                    layout TEXT NOT NULL DEFAULT '',
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    date_deleted DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_created DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_modified DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_published DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00'
                );");

            s.Execute(@"
                CREATE TABLE IF NOT EXISTS posts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    slug TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL DEFAULT '',
                    content TEXT NOT NULL DEFAULT '',
                    excerpt TEXT NOT NULL DEFAULT '',
                    cover_image TEXT NOT NULL DEFAULT '',
                    is_published INTEGER NOT NULL DEFAULT 0,
                    is_pinned INTEGER NOT NULL DEFAULT 0,
                    author_id INTEGER NOT NULL DEFAULT 0,
                    content_format TEXT NOT NULL DEFAULT 'html',
                    category_id INTEGER NOT NULL DEFAULT 0,
                    layout TEXT NOT NULL DEFAULT '',
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    date_deleted DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_created DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_modified DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_published DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00'
                );");

            s.Execute(@"
                CREATE TABLE IF NOT EXISTS media (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_name TEXT NOT NULL DEFAULT '',
                    original_name TEXT NOT NULL DEFAULT '',
                    mime_type TEXT NOT NULL DEFAULT '',
                    file_size INTEGER NOT NULL DEFAULT 0,
                    width INTEGER NOT NULL DEFAULT 0,
                    height INTEGER NOT NULL DEFAULT 0,
                    uploader_id INTEGER NOT NULL DEFAULT 0,
                    date_created DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00'
                );");

            s.Execute(@"
                CREATE TABLE IF NOT EXISTS settings (
                    skey TEXT PRIMARY KEY,
                    svalue TEXT NOT NULL DEFAULT ''
                );");

            s.Execute(@"
                CREATE TABLE IF NOT EXISTS categories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    slug TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL DEFAULT '',
                    description TEXT NOT NULL DEFAULT '',
                    cover_image TEXT NOT NULL DEFAULT '',
                    sort_order INTEGER NOT NULL DEFAULT 0,
                    date_created DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00',
                    date_modified DATETIME NOT NULL DEFAULT '2026-01-01 00:00:00'
                );");

            s.Execute("CREATE INDEX IF NOT EXISTS idx_posts_published ON posts(is_published, date_published);");
            s.Execute("CREATE INDEX IF NOT EXISTS idx_pages_nav ON pages(show_in_nav, sort_order);");
            s.Execute("CREATE INDEX IF NOT EXISTS idx_posts_category ON posts(category_id);");
        }

        // ============================================================
        // ===== Install seeding (RunInstall only) =====
        // Everything below is private and called exclusively from RunInstall.
        // Themes own their own assets (fonts, highlight.js, cache busting) via
        // their template files — there is no C#-injected head/footer HTML and no
        // theme_version tracking any more.
        // ============================================================

        // All default settings for a fresh install, plus the values submitted on
        // the setup form (site name, footer, optional custom admin slug).
        static void SeedSettings(SQLiteExpress s, string siteName, string adminPath)
        {
            SetIfMissing(s, "site_name", siteName);
            SetIfMissing(s, "site_tagline", "A clean place to write.");
            SetIfMissing(s, "site_description", "Welcome to our minimalist content management system.");
            SetIfMissing(s, "footer_text", "© " + DateTime.Now.Year + " " + siteName);
            SetIfMissing(s, "footer_col_count", "1");
            SetIfMissing(s, "footer_col_1", "Welcome to " + siteName);
            SetIfMissing(s, "home_post_count", "6");
            SetIfMissing(s, "latest_post_count", "6");
            SetIfMissing(s, "categories_post_count", "6");
            SetIfMissing(s, "category_post_count", "6");
            SetIfMissing(s, "article_sidebar_post_count", "6");
            SetIfMissing(s, "active_theme", "hearth-cs");
            SetIfMissing(s, "cache_ram_enabled", "1");
            SetIfMissing(s, "cache_file_enabled", "0");
            SetIfMissing(s, "cache_ram_max_mb", "250");
            SetIfMissing(s, "logo_mode", "text");
            SetIfMissing(s, "logo_url", "");
            SetIfMissing(s, "favicon_source_url", "");
            SetIfMissing(s, "favicon_version", "1");
            SetIfMissing(s, "og_image_url", "");
            SetIfMissing(s, "theme_color", "");
            SetIfMissing(s, "show_markdown_button", "1");
            SetIfMissing(s, DateDisplay.SettingKey, DateDisplay.DefaultFormat);

            // Custom admin login path (only when supplied; blank keeps the default).
            if (!string.IsNullOrEmpty(adminPath))
                SetIfMissing(s, AdminSlug.SettingKey, adminPath.ToLowerInvariant());
        }

        // The first (and only) admin login. The table is empty on a fresh file;
        // DELETE is belt-and-braces.
        static void SeedAdminUser(SQLiteExpress s, string username, string password, string email)
        {
            s.Execute("DELETE FROM users;");
            var u = new Dictionary<string, object>();
            u["username"] = username;
            u["email"] = (email ?? "").Trim();
            u["password_hash"] = Auth.Hash(password);
            u["display_name"] = "Administrator";
            u["role"] = "admin";
            u["date_created"] = DateTime.UtcNow;
            s.Insert("users", u);
        }

        static void SetIfMissing(SQLiteExpress s, string key, string value)
        {
            var p = new Dictionary<string, object> { { "@k", key } };
            int n = s.ExecuteScalar<int>("SELECT COUNT(*) FROM settings WHERE skey=@k;", p);
            if (n > 0) return;

            var d = new Dictionary<string, object>();
            d["skey"] = key;
            d["svalue"] = value;
            s.Insert("settings", d);
        }

        // Starter content created during first-run install: an About and a
        // Contact page (both shown in nav) plus three generic published posts.
        static void SeedStarterContent(SQLiteExpress s)
        {
            int pageCount = s.ExecuteScalar<int>("SELECT COUNT(*) FROM pages;");
            if (pageCount == 0)
            {
                InsertPage(s, "about", "About", @"
<p>Welcome! This is the About page for your new site. Use it to introduce yourself or your organisation, explain what visitors can expect to find here, and share a little of the story behind the site.</p>
<p>You can edit or replace this text any time from the admin panel under <strong>Pages</strong>.</p>
", "A little about this site.", 1, 1, 10);

                InsertPage(s, "contact", "Contact", @"
<p>Get in touch — we'd love to hear from you.</p>
<p><strong>Email:</strong> hello@example.com</p>
<p>Replace these details with your own from the admin panel under <strong>Pages</strong>.</p>
", "How to reach us.", 1, 1, 20);
            }

            int postCount = s.ExecuteScalar<int>("SELECT COUNT(*) FROM posts;");
            if (postCount == 0)
            {
                InsertPost(s, "welcome", "Welcome to your new site", @"
<p>This is your first post. Your site is ready to go: create pages, write posts, upload media, and adjust your settings — all from the admin panel.</p>
<p>Edit or delete this post whenever you like and start publishing your own.</p>
", "Your site is ready — here's how to get started.");

                InsertPost(s, "getting-started", "Getting started", @"
<p>A few things you might want to do first:</p>
<ul><li>Update the <strong>About</strong> and <strong>Contact</strong> pages with your own details.</li>
<li>Write your first real post and organise posts into categories.</li>
<li>Pick a theme and tweak your site name, tagline and footer in <strong>Settings</strong>.</li></ul>
<p>Take your time — nothing here is permanent.</p>
", "A short checklist for setting things up.");

                InsertPost(s, "writing-your-first-post", "Writing your first post", @"
<p>Posts are where your blog or news content lives. Each post can have a title, an excerpt, a cover image and a category.</p>
<blockquote>Write something you'd want to read.</blockquote>
<p>When you're ready, head to <strong>Posts → New post</strong> and publish. This sample post can be safely deleted.</p>
", "How posts work, and how to publish your own.");
            }
        }

        // The default navigation menu seeded at install:
        // Home, About, Latest Post, Categories, Contact.
        static void SeedNavigation(SQLiteExpress s)
        {
            const string navJson = @"[
    {""label"":""Home"",""url"":""/"",""children"":[]},
    {""label"":""About"",""url"":""/about"",""children"":[]},
    {""label"":""Latest Post"",""url"":""/latest-post"",""children"":[]},
    {""label"":""Categories"",""url"":""/categories-latest-post"",""children"":[]},
    {""label"":""Contact"",""url"":""/contact"",""children"":[]}
]";
            SetIfMissing(s, "nav_menu", navJson);
        }

        // Install-only row helpers. Kept (not legacy): they keep SeedStarterContent
        // readable and are the single place the seed rows' defaults live.
        static void InsertPage(SQLiteExpress s, string slug, string title, string content,
            string excerpt, int published, int nav, int sort)
        {
            var d = new Dictionary<string, object>();
            d["slug"] = slug;
            d["title"] = title;
            d["content"] = content;
            d["excerpt"] = excerpt;
            d["is_published"] = published;
            d["show_in_nav"] = nav;
            d["sort_order"] = sort;
            d["author_id"] = 1;
            d["date_created"] = DateTime.UtcNow;
            d["date_modified"] = DateTime.UtcNow;
            s.Insert("pages", d);
        }

        static void InsertPost(SQLiteExpress s, string slug, string title, string content, string excerpt, int categoryId = 0)
        {
            var d = new Dictionary<string, object>();
            d["slug"] = slug;
            d["title"] = title;
            d["content"] = content;
            d["excerpt"] = excerpt;
            d["cover_image"] = "";
            d["category_id"] = categoryId;
            d["is_published"] = 1;
            d["author_id"] = 1;
            d["date_created"] = DateTime.UtcNow;
            d["date_modified"] = DateTime.UtcNow;
            d["date_published"] = DateTime.UtcNow;
            s.Insert("posts", d);
        }

        public static void LoadSettingsCache(SQLiteExpress s)
        {
            SettingsCache.Clear();
            var lst = s.GetObjectList<obSetting>("SELECT skey, svalue FROM settings;");
            foreach (var it in lst) SettingsCache[it.Skey] = it.Svalue;
        }

        // Reload the in-memory settings dictionary from the database, opening a
        // private connection. Use when settings may have changed out-of-process
        // (e.g. a direct DB edit) and the running app needs to pick them up
        // without a restart - this is what /reset_app calls. Safe (no-op) when
        // the site is not yet installed; errors are swallowed so a reload attempt
        // never takes the request down.
        public static void ReloadSettings()
        {
            try
            {
                if (!File.Exists(Config.GetDbPath())) return;
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        LoadSettingsCache(s);
                    }
                }
            }
            catch { /* best-effort: keep whatever is already cached */ }
        }

        public static string GetSetting(string key, string fallback = "")
        {
            string v;
            return SettingsCache.TryGetValue(key, out v) ? v : fallback;
        }

        public static void SaveSetting(string key, string value)
        {
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var d = new Dictionary<string, object>();
                    d["skey"] = key;
                    d["svalue"] = value ?? "";
                    s.InsertOrReplace("settings", d);
                }
            }
            SettingsCache[key] = value ?? "";
        }
    }
}

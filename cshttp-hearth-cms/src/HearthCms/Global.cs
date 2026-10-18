using System;
using System.engine;
using System.engine.RH;
using System.engine.CsTemplate;
using CsHttp.Web;
using HearthCms.Host;

namespace HearthCms
{
    // ============================================================
    // Global - the Global.asax.cs of the IIS build, ported line for line.
    //
    // WebServer calls Application_BeginRequest(ctx) for every parsed request
    // with HttpContext.Current already set. The routing table below is the
    // original one; the only differences from IIS are marked "cshttp:" -
    // static files are served here (there is no IIS StaticFileModule), and
    // only the public asset folders are servable.
    // ============================================================
    public static class Global
    {
        public static void Application_Start()
        {
            // Resolve the cached "installed" flag (does the DB file exist?) once,
            // before any request is routed, so the per-request setup gate reads a
            // cached bool instead of hitting the disk every time.
            try { Db.RefreshInstalledFlag(); } catch { }

            try { Db.Initialize(); } catch { /* DB will surface error on first request */ }
            
            // Publish every theme's CSS to /assets/themes/{slug}/style.css so the
            // public site (which links the static file) keeps its styling.
            try { ThemeManager.EnsureAssetFiles(); } catch { }
            
            // Read /App_Data/config.txt once so the admin URL slug (and any other
            // file overrides) are in memory before the first request is routed.
            try { AdminSlug.LoadConfigOverride(); } catch { }
            
            // Consume a one-shot /App_Data/reset_admin.txt if present: reset the
            // admin login to the credentials it carries, then delete the file.
            try { Db.TryConsumeAdminReset(); } catch { }
            
            // Guard 2: resolve the active C# theme (if any) at startup so the
            // first request after a recycle doesn't pay the lazy-resolve cost.
            try { CsThemeRegistry.Refresh(); } catch { }

            // Resolve the App_Data path for the analytics store NOW, on the startup
            // thread, and hand it to the Analytics engine. The analytics batch
            // writer runs on a background thread with no HttpContext/Server.MapPath,
            // so it must be given an absolute path up front rather than resolving
            // one per write.
            try { Analytics.ConfigureDataDir(HostingEnvironment.MapPath("~/App_Data/")); } catch { }
        }

        // cshttp: graceful shutdown hook (systemd stop / IIS process recycle).
        public static void Application_End()
        {
            try { Analytics.FlushOnShutdown(); } catch { }
        }

        public static void Application_BeginRequest(HttpContext context)
        {
            HttpRequest Request = context.Request;
            HttpResponse Response = context.Response;

            string rawPath = Request.Path ?? "";
            string path = rawPath.ToLowerInvariant().TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";

            // cshttp: static folders. Behind nginx these normally never reach the
            // process (nginx serves them from disk); under IIS + HttpPlatformHandler
            // or a direct run they are served here. A miss is a plain 404.
            if (IsStatic(path))
            {
                if (!StaticFiles.TryServe(rawPath)) WritePlain404(Response);
                return;
            }

            // ===== Setup gate (install == database file exists) =====
            // The site is "installed" once the SQLite database file exists on
            // disk. While it is absent, every request is routed to the setup page
            // (/api/install is its submit endpoint) which creates and seeds the
            // database. Static assets are already served above so the page can be
            // styled. Once the file exists this branch is never taken again —
            // there is no flag and no way to re-enter setup.

            // /reset_app is exempt: it must stay reachable even before install so
            // an operator who drops a database file onto a running, uninstalled
            // instance can hit /reset_app to re-resolve the cached flag without an
            // app restart.
            if (!Db.IsInstalled && path != "/reset_app")
            {
                if (path == "/api/install") { InstallApi.HandleRequest(); return; }
                InstallPage.HandleRequest();
                return;
            }

            // Installed: the setup page is closed off permanently.
            if (path == "/install" || path == "/api/install")
            {
                ApiHelper.Redirect("/" + AdminSlug.Current);
                return;
            }

            // ===== Recovery / config reload =====
            // Visiting /reset_app re-reads /App_Data/config.txt, reloads the
            // in-memory settings dictionary from the database, refreshes the
            // admin slug, re-resolves the cached "installed" flag from disk, and
            // drops the template cache. This is the escape hatch for a forgotten
            // custom slug (edit config.txt admin_url=... then hit /reset_app),
            // for picking up settings changed directly in the database, and for
            // a database file that was added or removed while the app was running.
            if (path == "/reset_app")
            {
                Db.ReloadSettings();        // re-read the settings dictionary from the DB
                AdminSlug.Reload();
                Db.RefreshInstalledFlag();
                System.engine.TemplateEngine.ClearCache();   // settings (e.g. RAM cache) may have changed
                // Self-heal the favicon set: if a source image is configured but
                // the generated files are missing (e.g. lost in a deploy), rebuild
                // them. No-op when no source is set or the set already exists.
                try
                {
                    string favSrc = (Db.GetSetting("favicon_source_url", "") ?? "").Trim();
                    if (!string.IsNullOrEmpty(favSrc) && !System.engine.FaviconGenerator.HasGeneratedSet())
                    {
                        string ferr;
                        System.engine.FaviconGenerator.Generate(out ferr);
                    }
                }
                catch { }
                bool resetApplied = false;
                try { resetApplied = Db.TryConsumeAdminReset(); } catch { }
                Response.ContentType = "text/plain; charset=utf-8";
                Response.Write("Config and settings reloaded. Admin panel is at /" + AdminSlug.Current);
                if (resetApplied)
                    Response.Write("\nAdmin login was reset from reset_admin.txt (the file has been consumed).");
                ApiHelper.EndResponse();
                return;
            }

            // Restore login from a "remember me" cookie if the in-memory session
            // bag has no LoginUser yet (fresh browser, server restart, expired
            // session). Must run before the cache fast-path so HasActiveLogin
            // returns the correct value for cache eligibility, and before the
            // admin-slug gate so an authenticated session is recognised.
            try { RememberMe.TryRestore(); } catch { }

            // ===== Admin URL slug =====
            // The admin panel is reachable at the resolved slug (config.txt > DB >
            // "admin"). A custom-slug request is rewritten to its canonical
            // "/admin/..." form so the rest of the router is untouched.
            //
            // When a custom slug is active the literal "/admin/..." tree is hidden
            // from the public (404). Already-authenticated sessions are allowed
            // through it, so every existing in-app "/admin/..." link keeps working
            // once a user is signed in via the secret slug — the slug is the gate,
            // not a per-link rename.
            string canonical = AdminSlug.ToCanonical(path);
            if (canonical != null)
            {
                path = canonical;   // continue routing as if it were /admin/...
            }
            else if (AdminSlug.IsHiddenDefaultPath(path) && !AppSession.IsLoggedIn)
            {
                Render404();
                return;
            }

            // ===== Cache fast-path =====
            // Eligibility (anonymous GET, public route, no QS, cache enabled) is
            // checked inside TryServe. On hit, response is written and request ends.
            if (PublicPageCache.RamEnabled || PublicPageCache.FileEnabled)
            {
                if (PublicPageCache.TryServe(path)) return;
            }

            // ===== C# theme dispatch =====
            // If the active theme is a code-rendered (C#) theme, let it own the
            // public content routes (home, single post/page, latest, etc.).
            // Admin and API routes are never themed. Any handler the theme didn't
            // override falls back to the HTML-theme path via CsTemplate defaults.
            if (CsThemeRegistry.IsActiveCsTemplate && IsThemeablePath(path))
            {
                if (TryDispatchCsTheme(path, rawPath)) return;
            }

            // ===== Public pages =====
            switch (path)
            {
                case "/":
                case "/home":
                    HomePage.HandleRequest(); return;
                case "/latest-post":
                    LatestPostPage.HandleRequest(); return;
                case "/categories-latest-post":
                    CategoriesLatestPostPage.HandleRequest(); return;
                case "/logout":
                    LogoutHandler.HandleRequest(); return;

                // ===== SEO: sitemap + robots (generated live) =====
                case "/sitemap.xml":
                    SitemapGenerator.Serve(); return;
                case "/robots.txt":
                    SitemapGenerator.ServeRobots(); return;

                // ===== APIs =====
                // Login API lives under the admin slug (rewritten to this
                // canonical path). It is reachable pre-auth only via the slug;
                // a literal /admin/api/login is gated like the rest of /admin.
                case "/admin/api/login":
                    LoginApi.HandleRequest(); return;
                case "/api/admin/pages":
                    AdminPagesApi.HandleRequest(); return;
                case "/api/admin/posts":
                    AdminPostsApi.HandleRequest(); return;
                case "/api/admin/media":
                    AdminMediaApi.HandleRequest(); return;
                case "/api/admin/settings":
                    AdminSettingsApi.HandleRequest(); return;
                case "/api/admin/users":
                    AdminUserApi.HandleRequest(); return;
                case "/api/admin/nav":
                    AdminNavBuilder.HandleApiRequest(); return;
                case "/api/admin/themes":
                    AdminThemesApi.HandleRequest(); return;
                case "/api/admin/categories":
                    AdminCategoriesApi.HandleRequest(); return;
                case "/api/admin/preview-markdown":
                    AdminPreviewApi.HandleRequest(); return;
                case "/api/migration-import":
                    MigrationImportApi.HandleRequest(); return;
                case "/api/get-article-markdown":
                    GetArticleMarkdownApi.HandleRequest(); return;

                // ===== Internal Content Analytics =====
                // Public (no auth): called by /js/analytics.js on public pages.
                case "/api/analytics/start":
                    AnalyticsApi.HandleStart(); return;
                case "/api/analytics/heartbeat":
                    AnalyticsApi.HandleHeartbeat(); return;
                case "/api/admin/analytics":
                    AdminAnalyticsApi.HandleRequest(); return;

                // ===== Admin pages =====
                case "/admin":
                case "/admin/dashboard":
                    AdminDashboard.HandleRequest(); return;
                case "/admin/pages":
                    AdminPages.HandleListRequest(); return;
                case "/admin/pages/new":
                    AdminPages.HandleEditRequest(0); return;
                case "/admin/posts":
                    AdminPosts.HandleListRequest(); return;
                case "/admin/posts/new":
                    AdminPosts.HandleEditRequest(0); return;
                case "/admin/categories":
                    AdminCategories.HandleListRequest(); return;
                case "/admin/categories/new":
                    AdminCategories.HandleEditRequest(0); return;
                case "/admin/media":
                    AdminMedia.HandleRequest(); return;
                case "/admin/themes":
                    AdminThemes.HandleListRequest(); return;
                case "/admin/themes/docs":
                    AdminThemeDocs.HandleRequest(); return;
                case "/admin/themes/docs-csharp":
                    AdminThemeDocsCSharp.HandleRequest(); return;
                case "/admin/settings":
                    AdminSettings.HandleRequest(); return;
                case "/admin/users":
                    AdminUser.HandleRequest(); return;
                case "/admin/nav":
                    AdminNavBuilder.HandleRequest(); return;
                case "/admin/analytics":
                    AdminAnalytics.HandleListRequest(); return;
                case "/admin/analytics/detail":
                    AdminAnalytics.HandleDetailRequest(); return;
                case "/admin/guidelines":
                    AdminGuidelines.HandleRequest(); return;
                case "/admin/markdown-docs":
                    AdminMarkdownDocs.HandleRequest(); return;

            }

            // ===== Parameterised routes =====
            // /category/{slug}
            if (path.StartsWith("/category/"))
            {
                string slug = path.Substring("/category/".Length);
                if (!string.IsNullOrEmpty(slug)) { CategoryPage.HandleRequest(slug); return; }
            }
            // /admin/pages/edit/{id}
            if (path.StartsWith("/admin/pages/edit/"))
            {
                int id = 0; int.TryParse(path.Substring("/admin/pages/edit/".Length), out id);
                AdminPages.HandleEditRequest(id); return;
            }
            // /admin/posts/edit/{id}
            if (path.StartsWith("/admin/posts/edit/"))
            {
                int id = 0; int.TryParse(path.Substring("/admin/posts/edit/".Length), out id);
                AdminPosts.HandleEditRequest(id); return;
            }
            // /admin/categories/edit/{id}
            if (path.StartsWith("/admin/categories/edit/"))
            {
                int id = 0; int.TryParse(path.Substring("/admin/categories/edit/".Length), out id);
                AdminCategories.HandleEditRequest(id); return;
            }
            // /admin/themes/preview/{slug} - server-rendered preview of any theme.
            // Uses rawPath to preserve folder-name casing (slugs are case-kept).
            if (path.StartsWith("/admin/themes/preview/"))
            {
                string slug = rawPath.Substring("/admin/themes/preview/".Length).Trim('/');
                AdminThemes.HandlePreviewRequest(slug); return;
            }
            // /admin/themes/edit/{slug}
            if (path.StartsWith("/admin/themes/edit/"))
            {
                string slug = rawPath.Substring("/admin/themes/edit/".Length).Trim('/');
                AdminTemplateEdit.HandleEditRequest(slug); return;
            }

            // ===== Root-level content (prefix-less) =====
            // A bare "/{slug}" resolves to a page or post. Pages win on a slug
            // collision (page lookup runs first). The slug must not collide with
            // a reserved top-level route, and must be a single path segment.
            if (IsRootSlugCandidate(path))
            {
                string slug = path.Substring(1);   // strip leading "/"
                if (PagePage.TryHandleRequest(slug)) return;
                if (PostPage.TryHandleRequest(slug)) return;
                // Neither matched: fall through to 404 below.
            }

            // Fallthrough: 404. cshttp: under IIS a path with a file extension was
            // left to the StaticFileModule; here only the public folders are
            // servable (handled at the top), so a stray "/x.ext" is a plain 404 -
            // App_Data, binaries and config files can never be downloaded.
            if (HasFileExtension(rawPath)) { WritePlain404(Response); return; }

            Render404();
        }

        // Render a 404 through the active C# theme's shell when one is active
        // (its HandleNotFound sets status 404, renders inside the theme, and ends
        // the response). Falls back to the folder-theme NotFoundPage otherwise.
        //
        // This MUST be used for every 404 site-wide: NotFoundPage renders via the
        // folder-template engine (_layout.html / 404.html), which a C# theme has
        // no files for - calling it directly under a C# theme yields a blank page.
        static void Render404()
        {
            try
            {
                if (CsThemeRegistry.IsActiveCsTemplate)
                {
                    var theme = CsThemeRegistry.Active;
                    if (theme != null) { theme.HandleNotFound(); return; }
                }
            }
            catch { /* fall back to the folder-theme 404 below */ }

            NotFoundPage.HandleRequest();
        }

        // True when `path` is a public content route a theme is allowed to own.
        // Admin, API, login/logout, install and reset routes are never themed.
        static bool IsThemeablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("/admin")) return false;
            if (path.StartsWith("/api/")) return false;
            switch (path)
            {
                case "/logout":
                case "/install":
                case "/reset_app":
                    return false;
            }
            // Home, latest-post, categories-latest-post, /category/{slug},
            // and bare "/{slug}" content all qualify.
            if (path == "/" || path == "/home") return true;
            if (path == "/latest-post" || path == "/categories-latest-post") return true;
            if (path.StartsWith("/category/")) return true;
            return IsRootSlugCandidate(path);
        }

        // Routes a public content path to the active C# theme's matching handler.
        // Returns true if the request was handled (response written). For single
        // post/page paths it tries page then post (pages win), matching the
        // normal root-content resolution; returns false to fall through to 404.
        static bool TryDispatchCsTheme(string path, string rawPath)
        {
            var theme = CsThemeRegistry.Active;
            if (theme == null) return false;

            switch (path)
            {
                case "/":
                case "/home":
                    theme.HandleHome(); return true;
                case "/latest-post":
                    theme.HandleLatestPost(); return true;
                case "/categories-latest-post":
                    theme.HandleCategoriesLatestPost(); return true;
            }

            if (path.StartsWith("/category/"))
            {
                string cat = path.Substring("/category/".Length);
                if (!string.IsNullOrEmpty(cat)) { theme.HandleCategory(cat); return true; }
                return false;
            }

            // Bare "/{slug}" -> page, then post (pages win on a slug collision).
            if (IsRootSlugCandidate(path))
            {
                string slug = path.Substring(1);
                if (theme.HandlePage(slug)) return true;
                if (theme.HandlePost(slug)) return true;
                // Neither matched: let the normal fallthrough produce the 404.
            }
            return false;
        }

        // A path is a candidate for root slug resolution when it is a single
        // segment ("/about", not "/a/b"), non-empty, and not a reserved route.
        // Reserved first-segments mirror the fixed switch + parameterised routes
        // above so content can never shadow (or be shadowed by) app routes.
        static bool IsRootSlugCandidate(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return false;
            if (path[0] != '/') return false;
            // Single segment only: no further "/" after the first char.
            if (path.IndexOf('/', 1) != -1) return false;

            string seg = path.Substring(1);
            switch (seg)
            {
                case "home":
                case "latest-post":
                case "categories-latest-post":
                case "login":
                case "logout":
                case "api":
                case "admin":
                case "category":
                case "favicon.ico":
                case "robots.txt":
                case "sitemap.xml":
                    return false;
            }
            return true;
        }

        static bool IsStatic(string path)
        {
            return path.StartsWith("/css/")
                || path.StartsWith("/js/")
                || path.StartsWith("/assets/")
                || path.StartsWith("/uploads/")
                || path.StartsWith("/fonts/")
                || path.StartsWith("/media/")
                || path == "/favicon.ico";
            // NOTE: /robots.txt and /sitemap.xml are intentionally NOT treated as
            // static — they are generated dynamically (see SitemapGenerator) so
            // robots.txt can advertise the live sitemap URL.
        }

        static void WritePlain404(HttpResponse response)
        {
            response.StatusCode = 404;
            response.ContentType = "text/plain";
            response.Write("404 Not Found");
            response.End();
        }

        static bool HasFileExtension(string path)
        {
            int slash = path.LastIndexOf('/');
            int dot = path.LastIndexOf('.');
            return dot > slash && dot < path.Length - 1;
        }
    }
}
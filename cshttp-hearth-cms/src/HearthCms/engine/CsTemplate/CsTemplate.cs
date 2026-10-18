using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;
using System.engine.RH;

namespace System.engine.CsTemplate
{
    // ============================================================
    // CsTemplate - base class for code-rendered (C#) themes.
    //
    // A C# theme is a compiled class deriving from CsTemplate. Unlike a
    // folder theme (HTML files + {{tokens}} under /App_Data/themes/{slug}),
    // a C# theme renders each page imperatively in code (for-loops, if/else,
    // StringBuilder + string.Format). It still keeps its CSS/JS/images in the
    // normal asset folder: /assets/themes/{slug}/.
    //
    // Theme files live in /engine/CsTemplate/Themes/ and are compiled into the
    // main assembly. Theme code keeps to a conservative C# style (string.Format
    // / concat, no interpolation) for consistency across the theme set.
    //
    // Discovery: any non-abstract subclass of CsTemplate is a C# theme.
    // Deriving from this class IS the marker - no flag/attribute to remember.
    // CsThemeRegistry finds and activates the one matching the active slug
    // (it scans the loaded assemblies for concrete CsTemplate subclasses).
    //
    // Contract: each Handle* method owns its whole request (queries data,
    // builds HTML, emits it). The DEFAULT of each handler falls back to the
    // existing HTML-theme handler, so a C# theme may override only the pages
    // it cares about; the rest render via the folder-theme engine.
    //
    // Output: emit via WriteCached(html) so the page participates in the
    // public page cache exactly like the HTML path. Writing straight to
    // ApiHelper bypasses the cache.
    //
    // Data access: theme code should NOT open SQLite connections directly.
    // Use the public helper methods below (GetSiteName, GetRecentPost,
    // GetCategoryRecentPost, GetAllCategoriesRecentPost, GetPostBySlug, ...)
    // - they own the connection lifecycle and the canonical queries.
    // ============================================================
    public abstract class CsTemplate
    {
        // ----- Identity & metadata (the C# parallel to config.txt) -----

        // Must match the active_theme slug that selects this theme.
        public abstract string Slug { get; }

        public virtual string Name { get { return Slug; } }
        public virtual string Description { get { return ""; } }
        public virtual string Author { get { return ""; } }
        public virtual string Url { get { return ""; } }
        public virtual string Version { get { return "1"; } }

        // ----- Page handlers -----
        // Each defaults to the existing HTML-theme handler (fallback), so a
        // partial C# theme transparently inherits folder-theme rendering for
        // any page it doesn't override.

        public virtual void HandleHome() { HomePage.HandleRequest(); }

        // Single post / page. Return value mirrors the RH handlers: false means
        // "not found, let the caller try the next thing / 404".
        public virtual bool HandlePost(string slug) { return PostPage.TryHandleRequest(slug); }
        public virtual bool HandlePage(string slug) { return PagePage.TryHandleRequest(slug); }

        public virtual void HandleLatestPost() { LatestPostPage.HandleRequest(); }
        public virtual void HandleCategoriesLatestPost() { CategoriesLatestPostPage.HandleRequest(); }
        public virtual void HandleCategory(string slug) { CategoryPage.HandleRequest(slug); }
        public virtual void HandleNotFound() { NotFoundPage.HandleRequest(); }

        // ----- Helpers for theme authors -----

        // Public URL of this theme's asset folder, e.g. "/assets/themes/hearth".
        // Build links like AssetBase + "/style.css".
        protected string AssetBase { get { return "/assets/themes/" + Slug; } }

        // Public URL of the stylesheet the live <head> links for this theme. The
        // admin Markdown preview uses this so its iframe is styled identically to
        // the published page. The default follows the folder-theme convention
        // ({AssetBase}/style.css); each C# theme overrides it with the bundled CSS
        // file its own Layout links (e.g. broadsheet-cs.css, hearth.css).
        public virtual string CssHref { get { return AssetBase + "/style.css"; } }

        // Emit the finished HTML through the public page cache (RAM/file tiers),
        // caching under the current request path - the sanctioned output path.
        protected void WriteCached(string html)
        {
            PublicPageCache.WriteAndCache(html);
        }

        // HTML-encode a user-supplied value for use in element text. ALWAYS use
        // this around post titles, excerpts, etc. - interpolating raw is an
        // injection risk. Trusted, already-rendered HTML is appended as-is.
        protected static string H(string value)
        {
            return HttpUtility.HtmlEncode(value ?? "");
        }

        // HTML-encode a value for use inside an attribute (href, src, ...).
        protected static string Attr(string value)
        {
            return HttpUtility.HtmlAttributeEncode(value ?? "");
        }

        // ============================================================
        // Data helpers - the theme-facing data layer.
        //
        // Each method opens (and disposes) its own SQLite connection, runs the
        // canonical engine query, and returns plain model objects. Theme code
        // calls these instead of new-ing SQLiteConnection itself, so the
        // connection-string / SQL details stay in the engine.
        // ============================================================

        // The shared "open a connection, run, return" plumbing.
        protected static T WithDb<T>(Func<SQLiteExpress, T> work)
        {
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    return work(new SQLiteExpress(cmd));
                }
            }
        }

        // ----- Settings -----

        // The configured site name (settings table, falling back to Config).
        public string GetSiteName()
        {
            return Settings.SiteName;
        }

        public string GetSiteTagline(string fallback = "")
        {
            return Db.GetSetting("site_tagline", fallback);
        }

        public string GetSiteDescription(string fallback = "")
        {
            return Db.GetSetting("site_description", fallback);
        }

        // Any settings value (cached; no query).
        public string GetSetting(string key, string fallback = "")
        {
            return Db.GetSetting(key, fallback);
        }

        // A per-template post-count setting (home_post_count, latest_post_count,
        // categories_post_count, category_post_count, article_sidebar_post_count),
        // clamped to the engine's sane 1..50 range.
        public int GetCountSetting(string key, int fallback = 6)
        {
            int n = 0;
            if (!int.TryParse(Db.GetSetting(key, fallback.ToString()), out n)) n = fallback;
            if (n < 1) n = 1;
            if (n > 50) n = 50;
            return n;
        }

        // ----- Posts & pages -----

        // The newest published posts, newest first.
        public List<obPost> GetRecentPost(int totalPost)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    var p = new Dictionary<string, object> { { "@lim", totalPost } };
                    return s.GetObjectList<obPost>(
                        "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p);
                });
            }
            catch { return new List<obPost>(); }
        }

        // The newest published posts in one category, newest first.
        public List<obPost> GetCategoryRecentPost(int categoryId, int totalPost)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    var p = new Dictionary<string, object> { { "@cat", categoryId }, { "@lim", totalPost } };
                    return s.GetObjectList<obPost>(
                        "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 AND category_id=@cat ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p);
                });
            }
            catch { return new List<obPost>(); }
        }

        // Every category that has at least one published post, each paired with
        // its latest `totalPost` posts (categories in display order). Categories
        // with no published posts are skipped.
        public List<obCategoryPost> GetAllCategoriesRecentPost(int totalPost)
        {
            var list = new List<obCategoryPost>();
            try
            {
                WithDb<object>(delegate (SQLiteExpress s)
                {
                    // loop all categories
                    foreach (var cat in CategoryManager.GetAll())
                    {
                        if (cat == null || cat.Id <= 0) continue;

                        // sub inner loop: the latest posts of the category
                        var p = new Dictionary<string, object> { { "@cat", cat.Id }, { "@lim", totalPost } };
                        var posts = s.GetObjectList<obPost>(
                            "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 AND category_id=@cat ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p);
                        if (posts == null || posts.Count == 0) continue;

                        var cp = new obCategoryPost();
                        cp.Category = cat;
                        cp.Posts = posts;
                        list.Add(cp);
                    }
                    return null;
                });
            }
            catch { }
            return list;
        }

        // Multi-key relevance search (title > excerpt > content > date).
        // categoryId > 0 scopes the search to that category.
        public List<obPost> SearchPosts(string q, int categoryId, int totalPost)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    string like = "%" + q + "%";
                    var p = new Dictionary<string, object> { { "@q", like }, { "@lim", totalPost } };
                    string scope = "";
                    if (categoryId > 0)
                    {
                        scope = " AND category_id=@cat";
                        p["@cat"] = categoryId;
                    }
                    string sql =
                        "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0" + scope + " AND (" +
                        "  title LIKE @q OR excerpt LIKE @q OR content LIKE @q" +
                        ") ORDER BY " +
                        "  CASE WHEN title    LIKE @q THEN 0 ELSE 1 END, " +
                        "  CASE WHEN excerpt  LIKE @q THEN 0 ELSE 1 END, " +
                        "  CASE WHEN content  LIKE @q THEN 0 ELSE 1 END, " +
                        "  date_published DESC LIMIT @lim;";
                    return s.GetObjectList<obPost>(sql, p);
                });
            }
            catch { return new List<obPost>(); }
        }

        // One published, non-deleted post by slug, or null.
        public obPost GetPostBySlug(string slug)
        {
            try
            {
                obPost post = WithDb(delegate (SQLiteExpress s)
                {
                    var p = new Dictionary<string, object> { { "@s", slug } };
                    return s.GetObject<obPost>(
                        "SELECT * FROM posts WHERE slug=@s AND is_published=1 AND is_deleted=0 LIMIT 1;", p);
                });
                return (post != null && post.Id > 0) ? post : null;
            }
            catch { return null; }
        }

        // One published, non-deleted page by slug, or null.
        public obPage GetPageBySlug(string slug)
        {
            try
            {
                obPage page = WithDb(delegate (SQLiteExpress s)
                {
                    var p = new Dictionary<string, object> { { "@s", slug } };
                    return s.GetObject<obPage>(
                        "SELECT * FROM pages WHERE slug=@s AND is_published=1 AND is_deleted=0 LIMIT 1;", p);
                });
                return (page != null && page.Id > 0) ? page : null;
            }
            catch { return null; }
        }

        // One published, non-deleted page by id, or null. Used for the
        // admin-selected "home page is a Page" mode.
        public obPage GetPageById(int pageId)
        {
            if (pageId <= 0) return null;
            try
            {
                obPage page = WithDb(delegate (SQLiteExpress s)
                {
                    var p = new Dictionary<string, object> { { "@id", pageId } };
                    return s.GetObject<obPage>(
                        "SELECT * FROM pages WHERE id=@id AND is_published=1 AND is_deleted=0 LIMIT 1;", p);
                });
                return (page != null && page.Id > 0) ? page : null;
            }
            catch { return null; }
        }

        // A user's display name, or "" when the user is missing/has none.
        public string GetUserDisplayName(int userId)
        {
            if (userId <= 0) return "";
            try
            {
                var u = WithDb(delegate (SQLiteExpress s)
                {
                    var p = new Dictionary<string, object> { { "@id", userId } };
                    return s.GetObject<obUser>("SELECT * FROM users WHERE id=@id LIMIT 1;", p);
                });
                return (u != null) ? (u.DisplayName ?? "") : "";
            }
            catch { return ""; }
        }

        // Related posts for an article aside: same category first, falling back
        // to the latest posts overall. Never includes `excludePostId`.
        public List<obPost> GetRelatedPosts(int excludePostId, int categoryId, int totalPost)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    var related = new List<obPost>();
                    if (categoryId > 0)
                    {
                        var p2 = new Dictionary<string, object>
                            { { "@id", excludePostId }, { "@cat", categoryId }, { "@lim", totalPost } };
                        related = s.GetObjectList<obPost>(
                            "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 AND id<>@id AND category_id=@cat ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p2);
                    }
                    if (related == null || related.Count == 0)
                    {
                        var p2 = new Dictionary<string, object> { { "@id", excludePostId }, { "@lim", totalPost } };
                        related = s.GetObjectList<obPost>(
                            "SELECT * FROM posts WHERE is_published=1 AND is_deleted=0 AND id<>@id ORDER BY is_pinned DESC, date_published DESC, id DESC LIMIT @lim;", p2);
                    }
                    return related ?? new List<obPost>();
                });
            }
            catch { return new List<obPost>(); }
        }

        // Plain-text excerpt of rendered/HTML content, trimmed to maxLength.
        public string ToExcerpt(string content, int maxLength)
        {
            return HomePage.ToPlainText(content, maxLength);
        }

        // A post's excerpt, falling back to a plain-text trim of its content.
        public string PostExcerpt(obPost p, int maxLength)
        {
            if (p == null) return "";
            return string.IsNullOrEmpty(p.Excerpt) ? ToExcerpt(p.Content, maxLength) : p.Excerpt;
        }

        // ============================================================
        // Render helpers - shared list/section markup.
        //
        // These wrap the engine's internal HomeLayoutShared renderers, exposing
        // them to theme code as a stable, intention-revealing API.
        // ============================================================

        // The GET search box used by the listing pages.
        public string RenderSearchBar(string actionPath, string q)
        {
            return HomeLayoutShared.SearchBar(actionPath, q);
        }

        // Layout 1: flat row list [thumb | title / excerpt / date / category].
        public string RenderRowList(List<obPost> posts, bool showCategory)
        {
            return HomeLayoutShared.RowList(posts, showCategory,
                showCategory ? CategoryManager.GetMap() : null);
        }

        // Layout 2: one category section (featured post + two mini columns).
        public string RenderCategorySection(obCategory cat, List<obPost> posts)
        {
            return HomeLayoutShared.CategorySection(cat, posts);
        }

        // ============================================================
        // Pagination - page through the full published-post set instead of
        // capping at N. The per-listing count setting (e.g. latest_post_count)
        // becomes the page size; ?page=N selects the slice. These wrap the same
        // internal helpers the HTML-theme path uses, so both engines page
        // identically. RenderPagination builds the control as code (the C#
        // parallel to the HTML theme's pagination-block components) using the
        // same CSS class hooks, so one stylesheet block covers both paths.
        // ============================================================

        // Current 1-based page from ?page=N (defaults to 1).
        public int PageParam()
        {
            return HomeLayoutShared.PageParam();
        }

        // Page count for a row total at a given page size (always >= 1).
        public int TotalPages(int total, int perPage)
        {
            return HomeLayoutShared.TotalPages(total, perPage);
        }

        // Total published, non-deleted posts (the pagination denominator).
        public int GetPublishedPostCount()
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    return HomeLayoutShared.CountAll(s);
                });
            }
            catch { return 0; }
        }

        // Total posts matching a search (categoryId > 0 scopes it).
        public int SearchPostsCount(string q, int categoryId)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    return HomeLayoutShared.CountSearch(s, q, categoryId);
                });
            }
            catch { return 0; }
        }

        // One page of the latest posts (page size = perPage, slice = offset).
        public List<obPost> GetRecentPostPaged(int perPage, int offset)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    return HomeLayoutShared.LatestPaged(s, perPage, offset);
                });
            }
            catch { return new List<obPost>(); }
        }

        // One page of search results (same relevance ordering as SearchPosts).
        public List<obPost> SearchPostsPaged(string q, int categoryId, int perPage, int offset)
        {
            try
            {
                return WithDb(delegate (SQLiteExpress s)
                {
                    return HomeLayoutShared.SearchPaged(s, q, categoryId, perPage, offset);
                });
            }
            catch { return new List<obPost>(); }
        }

        // The pagination control as code-built HTML. Same class hooks as the
        // HTML theme's pagination-block (.pagination, .pagination-prev / -next /
        // -page, .pagination-gap, plus is-active / is-disabled suffixes), so a
        // single CSS block styles both paths. basePath is the listing route
        // (e.g. "/latest-post"); q preserves an active search across pages.
        // Returns "" for a single page.
        public string RenderPagination(string basePath, string q, int currentPage, int totalPages)
        {
            if (totalPages <= 1) return "";

            var sb = new StringBuilder();
            sb.Append("<nav class='pagination' aria-label='Pagination'>");

            int prev = currentPage <= 1 ? 1 : currentPage - 1;
            sb.Append("<a class='pagination-prev" + (currentPage <= 1 ? " is-disabled" : "") +
                "' href='" + Attr(HomeLayoutShared.PageUrl(basePath, q, prev)) + "' rel='prev'>" +
                "<i class='fa-solid fa-arrow-left'></i> Newer</a>");

            sb.Append("<span class='pagination-pages'>");
            foreach (int pg in HomeLayoutShared.PageWindow(currentPage, totalPages))
            {
                if (pg == 0)
                {
                    sb.Append("<span class='pagination-gap'>&hellip;</span>");
                    continue;
                }
                sb.Append("<a class='pagination-page" + (pg == currentPage ? " is-active" : "") +
                    "' href='" + Attr(HomeLayoutShared.PageUrl(basePath, q, pg)) + "'>" + pg + "</a>");
            }
            sb.Append("</span>");

            int next = currentPage >= totalPages ? totalPages : currentPage + 1;
            sb.Append("<a class='pagination-next" + (currentPage >= totalPages ? " is-disabled" : "") +
                "' href='" + Attr(HomeLayoutShared.PageUrl(basePath, q, next)) + "' rel='next'>" +
                "Older <i class='fa-solid fa-arrow-right'></i></a>");

            sb.Append("</nav>");
            return sb.ToString();
        }
    }
}

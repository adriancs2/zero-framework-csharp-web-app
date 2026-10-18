using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.engine;
using System.engine.RH;

using System.Text;
using System.Web;
using CsHttp.Web;

// ============================================================
// Phase 3 - Home layouts & category page
//
//   /latest-post            -> LatestPostPage     (layout 1: flat rows)
//   /categories-latest-post -> CategoriesLatestPostPage (layout 2)
//   /category/{slug}         -> CategoryPage       (layout 1, scoped)
//
// All three share the helpers in HomeLayoutShared below.
// Search is a plain GET form (?q=...). Because query strings
// bypass the page cache, search results always render fresh.
// ============================================================

namespace System.engine.RH
{
    // ===== /category/{slug} : layout 1, scoped to one category + scoped search =====
    public static class CategoryPage
    {
        public static void HandleRequest(string slug)
        {
            string q = (HttpContext.Current.Request.QueryString["q"] + "").Trim();
            int limit = HomeLayoutShared.CountFor("category_post_count");

            obCategory matchedCat = null;
            List<obPost> posts = new List<obPost>();
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);

                        // Resolve the slug directly to a managed category.
                        matchedCat = CategoryManager.GetBySlug(slug);

                        if (matchedCat != null && matchedCat.Id > 0)
                        {
                            posts = string.IsNullOrEmpty(q)
                                ? HomeLayoutShared.LatestInCategory(s, matchedCat.Id, limit)
                                : HomeLayoutShared.Search(s, q, matchedCat.Id, limit);
                        }
                    }
                }
            }
            catch { }

            if (matchedCat == null || matchedCat.Id <= 0)
            {
                NotFoundPage.HandleRequest();
                return;
            }

            string matchedCategory = matchedCat.Name;

            var pt = new PublicTemplate
            {
                Title = matchedCategory,
                BodyClass = "page-category"
            };
            string themeSlug = ThemeManager.GetActiveSlug();

            string crumbs = $@"<nav class='breadcrumbs' aria-label='Breadcrumb'>
                <a href='/'>Home</a><span class='sep'>/</span>
                <a href='/categories-latest-post'>Categories</a><span class='sep'>/</span>
                <span class='crumb-current'>{HttpUtility.HtmlEncode(matchedCategory)}</span>
            </nav>";

            var m = new TemplateModel();
            m.SetRaw("breadcrumbs", crumbs);
            m.SetText("page_heading", matchedCategory);
            m.SetRaw("page_subheading", "<p class='list-sub'>Posts in this category.</p>");
            m.SetRaw("search_bar", HomeLayoutShared.SearchBar("/category/" + HttpUtility.UrlEncode(slug), q));
            m.SetRaw("search_meta", string.IsNullOrEmpty(q)
                ? ""
                : $"<p class='search-meta'>{posts.Count} result(s) for “{HttpUtility.HtmlEncode(q)}” in {HttpUtility.HtmlEncode(matchedCategory)} · <a href='/category/{HttpUtility.HtmlAttributeEncode(slug)}'>Clear</a></p>");
            m.SetRaw("row_list", HomeLayoutShared.RowListTemplated(themeSlug, posts, false));

            string body = TemplateEngine.Render(themeSlug, "category.html", m);
            PublicPageCache.WriteAndCache(pt.RenderPage(body));
        }
    }
}
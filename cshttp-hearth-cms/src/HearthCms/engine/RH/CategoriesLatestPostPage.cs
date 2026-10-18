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
    // ===== /categories-latest-post : layout 2 (per-category sections) =====
    public static class CategoriesLatestPostPage
    {
        public static void HandleRequest()
        {
            int perCat = HomeLayoutShared.CountFor("categories_post_count");

            var sectionsHtml = new StringBuilder();
            int sectionCount = 0;
            try
            {
                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        string themeSlugLoop = ThemeManager.GetActiveSlug();
                        var cats = HomeLayoutShared.DistinctCategories(s);
                        foreach (var cat in cats)
                        {
                            var posts = HomeLayoutShared.LatestInCategory(s, cat.Id, perCat);
                            if (posts.Count == 0) continue;
                            sectionsHtml.Append(HomeLayoutShared.CategorySectionTemplated(themeSlugLoop, cat, posts));
                            sectionCount++;
                        }
                    }
                }
            }
            catch { }

            var pt = new PublicTemplate { Title = "Browse by category", BodyClass = "page-categories" };
            string themeSlug = ThemeManager.GetActiveSlug();

            string sections = sectionCount == 0
                ? "<div class='container'><p class='empty-state'>No categorised posts yet.</p></div>"
                : sectionsHtml.ToString();

            var m = new TemplateModel();
            m.SetText("page_heading", "Browse by category");
            m.SetText("page_subheading", "The latest from every category.");
            m.SetRaw("category_section_list", sections);

            string body = TemplateEngine.Render(themeSlug, "categories-latest-post.html", m);
            PublicPageCache.WriteAndCache(pt.RenderPage(body));
        }
    }
}
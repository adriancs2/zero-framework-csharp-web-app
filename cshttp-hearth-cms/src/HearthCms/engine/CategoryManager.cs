using System.Collections.Generic;
using System.Data.SQLite;

namespace System.engine
{
    // ============================================================
    // CategoryManager - CRUD-read helpers for the managed categories
    // table. Mirrors ThemeManager's shape. Posts reference a category by
    // its numeric id (posts.category_id); 0 means uncategorized. This
    // class resolves ids to the display name / slug used by the public
    // pages, and offers a cached id->category map so listing pages don't
    // issue one query per row.
    // ============================================================
    public static class CategoryManager
    {
        // Slug rules are identical to themes: latin letters, digits, dash,
        // underscore, 1-64 chars. Reuse the existing validator.
        public static bool IsValidSlug(string slug)
        {
            return ThemeManager.IsValidSlug(slug);
        }

        // All categories keyed by id, for cheap repeated lookups while rendering
        // a list of posts. Built fresh per call (no long-lived cache) so it always
        // reflects the current categories; callers building one page should reuse
        // the returned dictionary rather than calling NameById in a loop.
        public static Dictionary<int, obCategory> GetMap()
        {
            var map = new Dictionary<int, obCategory>();
            foreach (var c in GetAll())
                if (c != null && c.Id > 0) map[c.Id] = c;
            return map;
        }

        // Display name for a category id, or "" when uncategorized / unknown.
        public static string NameById(int id)
        {
            if (id <= 0) return "";
            var c = GetById(id);
            return c == null ? "" : c.Name;
        }

        // URL slug for a category id, or "" when uncategorized / unknown.
        public static string SlugById(int id)
        {
            if (id <= 0) return "";
            var c = GetById(id);
            return c == null ? "" : c.Slug;
        }

        public static List<obCategory> GetAll()
        {
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    return s.GetObjectList<obCategory>(
                        "SELECT * FROM categories ORDER BY sort_order ASC, name COLLATE NOCASE ASC;");
                }
            }
        }

        public static obCategory GetById(int id)
        {
            if (id <= 0) return null;
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var p = new Dictionary<string, object> { { "@id", id } };
                    return s.GetObject<obCategory>("SELECT * FROM categories WHERE id=@id LIMIT 1;", p);
                }
            }
        }

        public static obCategory GetBySlug(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return null;
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var p = new Dictionary<string, object> { { "@s", slug } };
                    return s.GetObject<obCategory>("SELECT * FROM categories WHERE slug=@s LIMIT 1;", p);
                }
            }
        }
    }
}

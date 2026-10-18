using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminCategoriesApi
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            var req = HttpContext.Current.Request;
            string action = (req["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "list": List(); break;
                    case "save": Save(); break;
                    case "delete": Delete(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        // GET-style read: every category with the fields a migration/automation
        // client needs to resolve a post's category_id (especially id + slug +
        // name), plus a published-post count per category. Ordered the same way
        // the admin list and public pages order them (sort_order, then name).
        static void List()
        {
            List<obCategory> cats = CategoryManager.GetAll();

            // Published, non-deleted post counts per category id (one query).
            var counts = new Dictionary<int, int>();
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var rows = s.GetObjectList<obSetting>(
                        "SELECT category_id AS skey, COUNT(*) AS svalue FROM posts WHERE category_id <> 0 AND is_deleted=0 GROUP BY category_id;");
                    foreach (var r in rows)
                    {
                        int cid = 0; int.TryParse(r.Skey + "", out cid);
                        int c = 0; int.TryParse(r.Svalue + "", out c);
                        if (cid > 0) counts[cid] = c;
                    }
                }
            }

            var list = new List<object>();
            foreach (var c in cats)
            {
                list.Add(new
                {
                    id = c.Id,
                    name = c.Name,
                    slug = c.Slug,
                    description = c.Description,
                    sort_order = c.SortOrder,
                    post_count = counts.ContainsKey(c.Id) ? counts[c.Id] : 0
                });
            }

            ApiHelper.WriteSuccess("OK", new { count = list.Count, categories = list });
        }

        static void Save()
        {
            var req = HttpContext.Current.Request;

            int id = 0; int.TryParse(req.Form["id"] + "", out id);
            string name = (req.Form["name"] + "").Trim();
            string slug = (req.Form["slug"] + "").Trim().ToLowerInvariant();
            string description = (req.Form["description"] + "").Trim();
            string cover = (req.Form["cover_image"] + "").Trim();
            int sortOrder = 0; int.TryParse(req.Form["sort_order"] + "", out sortOrder);

            if (string.IsNullOrEmpty(name)) { ApiHelper.WriteError("Name is required"); return; }
            if (string.IsNullOrEmpty(slug)) slug = Auth.Slugify(name);
            if (!CategoryManager.IsValidSlug(slug))
            {
                ApiHelper.WriteError("Slug must use only letters, digits, dashes and underscores (1-64 chars).");
                return;
            }

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    // Slug uniqueness (excluding self). On conflict, return the
                    // existing category's id so an importer can reuse it rather
                    // than failing — this makes a re-run idempotent. Still a 400
                    // (the save didn't happen), but the body carries existing_id.
                    var pp = new Dictionary<string, object> { { "@s", slug }, { "@id", id } };
                    var clash = s.GetObject<obCategory>(
                        "SELECT * FROM categories WHERE slug=@s AND id<>@id LIMIT 1;", pp);
                    if (clash != null && clash.Id > 0)
                    {
                        ApiHelper.WriteJson(new
                        {
                            success = false,
                            message = "Another category already uses that slug",
                            existing_id = clash.Id,
                            existing_slug = clash.Slug,
                            existing_name = clash.Name
                        });
                        return;
                    }

                    var d = new Dictionary<string, object>();
                    d["slug"] = slug;
                    d["name"] = name;
                    d["description"] = description;
                    d["cover_image"] = cover;
                    d["sort_order"] = sortOrder;
                    d["date_modified"] = DateTime.UtcNow;

                    if (id <= 0)
                    {
                        d["date_created"] = DateTime.UtcNow;
                        s.Insert("categories", d);
                        id = (int)s.LastInsertId;
                        AppSession.SetFlash("Category created");
                    }
                    else
                    {
                        // Posts reference the category by id, which never changes on
                        // edit, so a rename or slug change needs no propagation.
                        var pCheck = new Dictionary<string, object> { { "@id", id } };
                        var existing = s.GetObject<obCategory>(
                            "SELECT * FROM categories WHERE id=@id LIMIT 1;", pCheck);
                        if (existing == null || existing.Id == 0) { ApiHelper.WriteError("Category not found"); return; }

                        s.Update("categories", d, "id", id);
                        AppSession.SetFlash("Category updated");
                    }
                }
            }

            // Category changes affect category/home listings site-wide.
            PublicPageCache.InvalidateAll();
            ApiHelper.WriteSuccess("Saved", new { id, slug });
        }

        static void Delete()
        {
            var req = HttpContext.Current.Request;
            int id = 0; int.TryParse(req.Form["id"] + "", out id);
            if (id <= 0) { ApiHelper.WriteError("Invalid category id"); return; }

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var p = new Dictionary<string, object> { { "@id", id } };
                    // Reset any posts in this category back to uncategorized (0),
                    // then remove the managed entry. Posts are never left pointing
                    // at a category id that no longer exists.
                    s.Execute("UPDATE posts SET category_id=0 WHERE category_id=@id;", p);
                    s.Execute("DELETE FROM categories WHERE id=@id;", p);
                }
            }

            PublicPageCache.InvalidateAll();
            AppSession.SetFlash("Category deleted");
            ApiHelper.WriteSuccess("Deleted");
        }
    }
}
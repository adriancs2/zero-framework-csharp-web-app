using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminPagesApi
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
                    case "save": Save(); break;
                    case "delete": SetDeleted(1); break;       // soft delete
                    case "undelete": SetDeleted(0); break;     // restore
                    case "purge": Purge(); break;              // permanent delete
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Save()
        {
            var req = HttpContext.Current.Request;

            int id = 0; int.TryParse(req.Form["id"] + "", out id);
            string title = (req.Form["title"] + "").Trim();
            string slug = (req.Form["slug"] + "").Trim().ToLowerInvariant();
            string content = req.Form["content"] + "";
            string contentFormat = (req.Form["content_format"] + "").Trim().ToLowerInvariant();
            if (contentFormat != "markdown") contentFormat = "html";
            string excerpt = (req.Form["excerpt"] + "").Trim();
            string layout = (req.Form["layout"] + "").Trim().ToLowerInvariant();
            if (layout != "split" && layout != "stack") layout = "stack";   // page default
            int isPublished = (req.Form["is_published"] + "") == "1" ? 1 : 0;
            int showInNav = (req.Form["show_in_nav"] + "") == "1" ? 1 : 0;
            int sortOrder = 0; int.TryParse(req.Form["sort_order"] + "", out sortOrder);
            DateTime datePublished = DateTime.MinValue;
            DateTime.TryParse(req.Form["date_published"] + "", out datePublished);

            if (string.IsNullOrEmpty(title)) { ApiHelper.WriteError("Title is required"); return; }
            if (string.IsNullOrEmpty(slug)) slug = Auth.Slugify(title);
            if (string.IsNullOrEmpty(slug)) { ApiHelper.WriteError("Could not generate a slug"); return; }

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    // slug uniqueness check
                    var pp = new Dictionary<string, object> { { "@s", slug }, { "@id", id } };
                    int dup = s.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM pages WHERE slug=@s AND id<>@id;", pp);
                    if (dup > 0) { ApiHelper.WriteError("Another page already uses that slug"); return; }

                    var d = new Dictionary<string, object>();
                    d["slug"] = slug;
                    d["title"] = title;
                    d["content"] = content;
                    d["content_format"] = contentFormat;
                    d["excerpt"] = excerpt;
                    d["layout"] = layout;
                    d["is_published"] = isPublished;
                    d["show_in_nav"] = showInNav;
                    d["sort_order"] = sortOrder;
                    d["date_modified"] = DateTime.UtcNow;
                    // Honor explicit date if user picked one; otherwise leave existing/default.
                    if (datePublished != DateTime.MinValue) d["date_published"] = datePublished;

                    if (id <= 0)
                    {
                        d["author_id"] = AppSession.LoginUser != null ? AppSession.LoginUser.Id : 0;
                        d["date_created"] = DateTime.UtcNow;
                        if (datePublished == DateTime.MinValue) d["date_published"] = DateTime.UtcNow;
                        s.Insert("pages", d);
                        id = (int)s.LastInsertId;   // surface the new id so the editor can switch to edit mode
                        AppSession.SetFlash("Page created");
                    }
                    else
                    {
                        s.Update("pages", d, "id", id);
                        AppSession.SetFlash("Page updated");
                    }
                    PublicPageCache.InvalidateSlug("/", slug);
                }
            }
            // Return the id (new or existing) so the editor can stay on the page
            // and, for a freshly-created page, switch into edit mode instead of
            // creating a duplicate on the next save.
            ApiHelper.WriteSuccess("Saved", new { id });
        }

        // Parse the target id(s) from either a single "id" field or a
        // comma-separated "ids" field (bulk actions). Invalid/zero ids dropped.
        static List<int> ReadIds()
        {
            var req = HttpContext.Current.Request;
            var ids = new List<int>();

            int single = 0; int.TryParse(req.Form["id"] + "", out single);
            if (single > 0) ids.Add(single);

            string bulk = req.Form["ids"] + "";
            if (!string.IsNullOrEmpty(bulk))
            {
                foreach (string part in bulk.Split(','))
                {
                    int v = 0;
                    if (int.TryParse(part.Trim(), out v) && v > 0 && !ids.Contains(v))
                        ids.Add(v);
                }
            }
            return ids;
        }

        // Soft delete (deleted=1) or restore (deleted=0) one or more pages.
        static void SetDeleted(int deleted)
        {
            var ids = ReadIds();
            if (ids.Count == 0) { ApiHelper.WriteError("No page selected"); return; }

            var slugs = new List<string>();
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    foreach (int id in ids)
                    {
                        var p = new Dictionary<string, object> { { "@id", id } };
                        var existing = s.GetObject<obPage>(
                            "SELECT id, slug FROM pages WHERE id=@id LIMIT 1;", p);
                        if (existing == null || existing.Id == 0) continue;

                        var d = new Dictionary<string, object>();
                        d["is_deleted"] = deleted;
                        if (deleted == 1) d["date_deleted"] = DateTime.UtcNow;
                        s.Update("pages", d, "id", id);
                        if (!string.IsNullOrEmpty(existing.Slug)) slugs.Add(existing.Slug);
                    }
                }
            }
            foreach (string slug in slugs) PublicPageCache.InvalidateSlug("/", slug);

            string verb = deleted == 1 ? "deleted" : "restored";
            AppSession.SetFlash(ids.Count > 1 ? ids.Count + " pages " + verb : "Page " + verb);
            ApiHelper.WriteSuccess(deleted == 1 ? "Deleted" : "Restored");
        }

        // Permanent delete - removes the row(s) for good. Used from the
        // "Deleted" filter view only.
        static void Purge()
        {
            var ids = ReadIds();
            if (ids.Count == 0) { ApiHelper.WriteError("No page selected"); return; }

            var slugs = new List<string>();
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    foreach (int id in ids)
                    {
                        var p = new Dictionary<string, object> { { "@id", id } };
                        var existing = s.GetObject<obPage>(
                            "SELECT id, slug FROM pages WHERE id=@id LIMIT 1;", p);
                        s.Execute("DELETE FROM pages WHERE id=@id;", p);
                        if (existing != null && !string.IsNullOrEmpty(existing.Slug))
                            slugs.Add(existing.Slug);
                    }
                }
            }
            foreach (string slug in slugs) PublicPageCache.InvalidateSlug("/", slug);
            AppSession.SetFlash(ids.Count > 1 ? ids.Count + " pages permanently deleted" : "Page permanently deleted");
            ApiHelper.WriteSuccess("Permanently deleted");
        }
    }
}
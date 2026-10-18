using System.Collections.Generic;
using System.Data.SQLite;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminPostsApi
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
            // The excerpt is a plain-text, single-line description. Strip any
            // Markdown/HTML the author may have pasted so listings never show
            // raw syntax (e.g. **bold**, `code`). Stored already-escaped-safe.
            string excerpt = HomePage.ToPlainText(req.Form["excerpt"] + "", int.MaxValue).Trim();
            string cover = (req.Form["cover_image"] + "").Trim();
            // Category is referenced by numeric id; 0 = uncategorized. An id that
            // doesn't resolve to an existing category is treated as uncategorized.
            int categoryId = 0;
            int.TryParse(req.Form["category_id"] + "", out categoryId);
            if (categoryId < 0) categoryId = 0;
            if (categoryId > 0 && CategoryManager.GetById(categoryId) == null) categoryId = 0;
            string layout = (req.Form["layout"] + "").Trim().ToLowerInvariant();
            if (layout != "split" && layout != "stack") layout = "split";   // post default
            int isPublished = (req.Form["is_published"] + "") == "1" ? 1 : 0;
            int isPinned = (req.Form["is_pinned"] + "") == "1" ? 1 : 0;
            DateTime datePublishedExplicit = DateTime.MinValue;
            DateTime.TryParse(req.Form["date_published"] + "", out datePublishedExplicit);

            if (string.IsNullOrEmpty(title)) { ApiHelper.WriteError("Title is required"); return; }
            if (string.IsNullOrEmpty(slug)) slug = Auth.Slugify(title);
            if (string.IsNullOrEmpty(slug)) { ApiHelper.WriteError("Could not generate a slug"); return; }

            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);

                    var pp = new Dictionary<string, object> { { "@s", slug }, { "@id", id } };
                    int dup = s.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM posts WHERE slug=@s AND id<>@id;", pp);
                    if (dup > 0) { ApiHelper.WriteError("Another post already uses that slug"); return; }

                    var d = new Dictionary<string, object>();
                    d["slug"] = slug;
                    d["title"] = title;
                    d["content"] = content;
                    d["content_format"] = contentFormat;
                    d["excerpt"] = excerpt;
                    d["cover_image"] = cover;
                    d["category_id"] = categoryId;
                    d["layout"] = layout;
                    d["is_published"] = isPublished;
                    d["is_pinned"] = isPinned;
                    d["date_modified"] = DateTime.UtcNow;

                    // If user explicitly picked a date, honor it.
                    if (datePublishedExplicit != DateTime.MinValue)
                        d["date_published"] = datePublishedExplicit;

                    if (id <= 0)
                    {
                        d["author_id"] = AppSession.LoginUser != null ? AppSession.LoginUser.Id : 0;
                        d["date_created"] = DateTime.UtcNow;
                        if (datePublishedExplicit == DateTime.MinValue)
                            d["date_published"] = isPublished == 1 ? DateTime.UtcNow : DateTime.MinValue;
                        s.Insert("posts", d);
                        id = (int)s.LastInsertId;   // surface the new id so the editor can switch to edit mode
                        AppSession.SetFlash("Post created");
                    }
                    else
                    {
                        // If no explicit date and the post is being published for the first time, auto-stamp.
                        if (datePublishedExplicit == DateTime.MinValue && isPublished == 1)
                        {
                            var pCheck = new Dictionary<string, object> { { "@id", id } };
                            var existing = s.GetObject<obPost>(
                                "SELECT id, is_published, date_published FROM posts WHERE id=@id LIMIT 1;", pCheck);
                            if (existing != null && (existing.IsPublished == 0 || existing.DatePublished == DateTime.MinValue))
                            {
                                d["date_published"] = DateTime.UtcNow;
                            }
                        }
                        s.Update("posts", d, "id", id);
                        AppSession.SetFlash("Post updated");
                    }
                    PublicPageCache.InvalidateSlug("/", slug);
                    if (id > 0) PublicPageCache.InvalidateMarkdown(id);   // drop cached raw markdown
                }
            }

            // After saving, refresh the cover image's thumbnail mirror. Only
            // local images are processed; external hot-links are left alone.
            // Runs as a fire-and-forget background task so the save returns fast.
            ImageThumb.QueueGenerate(cover);

            // Return the id (new or existing) so the editor can stay on the page
            // and, for a freshly-created post, switch into edit mode instead of
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

        // Soft delete (deleted=1) or restore (deleted=0) one or more posts.
        static void SetDeleted(int deleted)
        {
            var ids = ReadIds();
            if (ids.Count == 0) { ApiHelper.WriteError("No post selected"); return; }

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
                        var existing = s.GetObject<obPost>(
                            "SELECT id, slug FROM posts WHERE id=@id LIMIT 1;", p);
                        if (existing == null || existing.Id == 0) continue;

                        var d = new Dictionary<string, object>();
                        d["is_deleted"] = deleted;
                        if (deleted == 1) d["date_deleted"] = DateTime.UtcNow;
                        s.Update("posts", d, "id", id);
                        if (!string.IsNullOrEmpty(existing.Slug)) slugs.Add(existing.Slug);
                    }
                }
            }
            foreach (string slug in slugs) PublicPageCache.InvalidateSlug("/", slug);
            foreach (int mid in ids) PublicPageCache.InvalidateMarkdown(mid);

            string verb = deleted == 1 ? "deleted" : "restored";
            AppSession.SetFlash(ids.Count > 1 ? ids.Count + " posts " + verb : "Post " + verb);
            ApiHelper.WriteSuccess(deleted == 1 ? "Deleted" : "Restored");
        }

        // Permanent delete - removes the row(s) for good. Used from the
        // "Deleted" filter view only.
        static void Purge()
        {
            var ids = ReadIds();
            if (ids.Count == 0) { ApiHelper.WriteError("No post selected"); return; }

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
                        var existing = s.GetObject<obPost>(
                            "SELECT id, slug FROM posts WHERE id=@id LIMIT 1;", p);
                        s.Execute("DELETE FROM posts WHERE id=@id;", p);
                        if (existing != null && !string.IsNullOrEmpty(existing.Slug))
                            slugs.Add(existing.Slug);
                    }
                }
            }
            foreach (string slug in slugs) PublicPageCache.InvalidateSlug("/", slug);
            foreach (int mid in ids) PublicPageCache.InvalidateMarkdown(mid);
            AppSession.SetFlash(ids.Count > 1 ? ids.Count + " posts permanently deleted" : "Post permanently deleted");
            ApiHelper.WriteSuccess("Permanently deleted");
        }
    }
}
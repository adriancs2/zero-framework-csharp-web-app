using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminMediaApi
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
                    case "upload": Upload(); break;
                    case "delete": Delete(); break;
                    case "list": List(); break;
                    default: ApiHelper.WriteError("Unknown action: " + action); break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Upload()
        {
            var req = HttpContext.Current.Request;
            if (req.Files.Count == 0) { ApiHelper.WriteError("No file uploaded"); return; }

            // New default upload location: /media/{year}/{month}. The full
            // root-relative path is stored in the DB (file_name); thumbnails
            // mirror it under /media/thumbnail/{year}/{month} (not in the DB).
            DateTime now = DateTime.UtcNow;
            string relDir = "/media/" + now.ToString("yyyy") + "/" + now.ToString("MM");
            string folder = HttpContext.Current.Server.MapPath("~" + relDir);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var uploaded = new List<object>();

            for (int i = 0; i < req.Files.Count; i++)
            {
                HttpPostedFile file = req.Files[i];
                if (file.ContentLength == 0) continue;

                string ext = Path.GetExtension(file.FileName);
                string safeBase = SanitizeFileName(Path.GetFileNameWithoutExtension(file.FileName));

                // Keep the original (sanitized) filename. On collision within the
                // same /media/{year}/{month} folder, append "-2", "-3", ... until
                // the name is free. No timestamp is added.
                string fileName = UniqueFileName(folder, safeBase, ext);
                string savePath = Path.Combine(folder, fileName);
                string relUrl = relDir + "/" + fileName;

                file.SaveAs(savePath);

                using (var conn = new SQLiteConnection(Config.GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        var s = new SQLiteExpress(cmd);
                        var d = new Dictionary<string, object>();
                        d["file_name"] = relUrl;   // full root-relative path
                        d["original_name"] = Path.GetFileName(file.FileName);
                        d["mime_type"] = file.ContentType ?? "";
                        d["file_size"] = file.ContentLength;
                        d["width"] = 0;
                        d["height"] = 0;
                        d["uploader_id"] = AppSession.LoginUser != null ? AppSession.LoginUser.Id : 0;
                        d["date_created"] = DateTime.UtcNow;
                        s.Insert("media", d);
                    }
                }

                // Mirror a resized thumbnail so the media browser and post
                // listings can display the lighter image. Fire and forget.
                ImageThumb.QueueGenerate(relUrl);

                uploaded.Add(new
                {
                    success = true,
                    fileName,
                    url = relUrl
                });
            }

            ApiHelper.WriteJson(new { success = true, message = "Uploaded", files = uploaded });
        }

        static void Delete()
        {
            var req = HttpContext.Current.Request;
            int id = 0; int.TryParse(req.Form["id"] + "", out id);
            if (id <= 0) { ApiHelper.WriteError("Invalid id"); return; }

            string fileName = "";
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var p = new Dictionary<string, object> { { "@id", id } };
                    fileName = s.ExecuteScalar<string>("SELECT file_name FROM media WHERE id=@id;", p);
                    s.Execute("DELETE FROM media WHERE id=@id;", p);
                }
            }
            if (!string.IsNullOrEmpty(fileName))
            {
                // file_name may be a full root-relative path ("/media/...") for
                // new uploads, or a bare name for legacy rows under /uploads/.
                string relUrl = fileName[0] == '/'
                    ? fileName
                    : (fileName.IndexOf('/') >= 0 ? "/" + fileName : "/uploads/" + fileName);
                try
                {
                    string p = HttpContext.Current.Server.MapPath("~" + relUrl);
                    if (File.Exists(p)) File.Delete(p);
                }
                catch { }
                // Remove the thumbnail mirror if one was generated.
                try
                {
                    string thumbUrl = ImageThumb.ThumbUrl(relUrl);
                    if (thumbUrl != relUrl)
                    {
                        string tp = HttpContext.Current.Server.MapPath("~" + thumbUrl);
                        if (File.Exists(tp)) File.Delete(tp);
                    }
                }
                catch { }
            }
            ApiHelper.WriteSuccess("Deleted");
        }

        static void List()
        {
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var lst = s.GetObjectList<obMedia>("SELECT * FROM media ORDER BY date_created DESC;");
                    string html = AdminMedia.RenderPickerTilesHtml(lst);
                    ApiHelper.WriteJson(new { success = true, html });
                }
            }
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "file";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == '-' || c == '_') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            return string.IsNullOrEmpty(s) ? "file" : s;
        }

        // Public passthroughs so other intake endpoints (e.g. MigrationImportApi)
        // can reuse the exact same filename sanitisation and collision rules
        // without duplicating the logic.
        public static string SanitizeFileNamePublic(string name)
        {
            return SanitizeFileName(name);
        }

        public static string UniqueFileNamePublic(string folder, string safeBase, string ext)
        {
            return UniqueFileName(folder, safeBase, ext);
        }

        // Return a filename that does not yet exist in 'folder'. Tries
        // "{base}{ext}" first; on collision appends "-2", "-3", ... until free.
        // The thumbnail mirror is checked too, so a name is only accepted when
        // both the original and its thumbnail slot are free — this keeps the
        // original/thumbnail pair in lockstep and avoids a new upload silently
        // adopting a stale thumbnail from a previously deleted file.
        static string UniqueFileName(string folder, string safeBase, string ext)
        {
            string candidate = safeBase + ext;
            if (!NameTaken(folder, candidate)) return candidate;

            int n = 2;
            while (true)
            {
                candidate = safeBase + "-" + n + ext;
                if (!NameTaken(folder, candidate)) return candidate;
                n++;
            }
        }

        // True if 'fileName' is occupied in 'folder', either by the original
        // file or by its thumbnail mirror under /media/thumbnail/...
        static bool NameTaken(string folder, string fileName)
        {
            if (File.Exists(Path.Combine(folder, fileName))) return true;

            try
            {
                // Map the would-be public URL to its thumbnail and check that too.
                string root = HttpContext.Current.Server.MapPath("~/");
                string full = Path.Combine(folder, fileName);
                // Build the root-relative URL for this candidate, then its thumb URL.
                string relUrl = "/" + full.Substring(root.Length).Replace('\\', '/').TrimStart('/');
                string thumbUrl = ImageThumb.ThumbUrl(relUrl);
                if (thumbUrl != relUrl)
                {
                    string thumbPhys = HttpContext.Current.Server.MapPath("~" + thumbUrl);
                    if (File.Exists(thumbPhys)) return true;
                }
            }
            catch { /* thumbnail check is best-effort */ }

            return false;
        }
    }
}

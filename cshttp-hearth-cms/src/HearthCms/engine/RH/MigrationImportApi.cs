using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // MigrationImportApi - image intake for the WordPress migration
    // tool. Unlike AdminMediaApi.Upload (which files everything under
    // the CURRENT year/month), this endpoint takes the year and month
    // as parameters so the resulting path is predictable and can be
    // computed by the migration tool up front:
    //
    //     /media/{year}/{month}/{filename}
    //
    // That lets the tool rewrite the article's image URLs to their final
    // Hearth paths before (or independently of) the actual upload.
    //
    // Auth: same as the rest of the admin APIs. When Settings.IsDevMode is
    // true, AdminGuard.RequireLoginApi() auto-logs the first admin, so the
    // migration tool needs no credentials in a local/dev run.
    //
    // Endpoint: POST /api/migration-import  (multipart/form-data)
    //
    // Form fields (single image):
    //   filename         desired file name (extension respected/derived)
    //   filebytes        the file part (multipart file)
    //   year             4-digit year   e.g. 2026
    //   month            1-2 digit month e.g. 6 or 06 (normalised to 2 digits)
    //   build_thumbnail  "true"/"1" to queue a background thumbnail
    //
    // Multi-image: send N file parts. year/month/build_thumbnail apply to
    // all of them. A per-file "filename" can be supplied via repeated
    // "filename" fields (index-aligned with the files); when absent the
    // uploaded part's own file name is used.
    //
    // Filenames follow the same rule as AdminMediaApi: sanitized original
    // name, with "-2", "-3", ... appended on collision within the folder.
    // ============================================================
    public static class MigrationImportApi
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLoginApi()) return;

            try
            {
                Import();
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError(ex.Message, 500);
            }
            ApiHelper.EndResponse();
        }

        static void Import()
        {
            var req = HttpContext.Current.Request;

            if (req.Files.Count == 0) { ApiHelper.WriteError("No file uploaded"); return; }

            // ----- resolve and validate year / month -----
            int year = 0, month = 0;
            int.TryParse((req.Form["year"] + "").Trim(), out year);
            int.TryParse((req.Form["month"] + "").Trim(), out month);

            if (year < 1970 || year > 9999) { ApiHelper.WriteError("Invalid year"); return; }
            if (month < 1 || month > 12) { ApiHelper.WriteError("Invalid month"); return; }

            string yyyy = year.ToString("D4");
            string mm = month.ToString("D2");

            bool buildThumb = ParseBool(req.Form["build_thumbnail"]);

            // ----- target folder: /media/{year}/{month} -----
            string relDir = "/media/" + yyyy + "/" + mm;
            string folder = HttpContext.Current.Server.MapPath("~" + relDir);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            // Per-file desired names, index-aligned with req.Files (optional).
            string[] desiredNames = req.Form.GetValues("filename");

            var uploaded = new List<object>();

            for (int i = 0; i < req.Files.Count; i++)
            {
                HttpPostedFile file = req.Files[i];
                if (file == null || file.ContentLength == 0) continue;

                // Choose the source name: explicit "filename" field for this
                // index if present, else the uploaded part's own name.
                string srcName = file.FileName;
                if (desiredNames != null && i < desiredNames.Length &&
                    !string.IsNullOrEmpty((desiredNames[i] + "").Trim()))
                {
                    srcName = desiredNames[i].Trim();
                }

                string ext = Path.GetExtension(srcName);
                if (string.IsNullOrEmpty(ext)) ext = Path.GetExtension(file.FileName);

                // Extension whitelist. This endpoint writes directly into the
                // web-served /media tree, so we must never accept executable or
                // script types (e.g. .aspx, .ashx, .config, .php). Restrict to
                // the image types the migration actually moves. Combined with
                // Settings.IsDevMode auto-login, an un-whitelisted endpoint on a
                // public instance would let any visitor drop arbitrary files.
                if (!IsAllowedImageExt(ext))
                {
                    uploaded.Add(new
                    {
                        success = false,
                        fileName = Path.GetFileName(srcName),
                        error = "Disallowed file type: " + ext
                    });
                    continue;
                }

                string safeBase = AdminMediaApi.SanitizeFileNamePublic(
                    Path.GetFileNameWithoutExtension(srcName));

                // Original filename, with "-2", "-3", ... on collision.
                string fileName = AdminMediaApi.UniqueFileNamePublic(folder, safeBase, ext);
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
                        d["original_name"] = Path.GetFileName(srcName);
                        d["mime_type"] = file.ContentType ?? "";
                        d["file_size"] = file.ContentLength;
                        d["width"] = 0;
                        d["height"] = 0;
                        d["uploader_id"] = AppSession.LoginUser != null ? AppSession.LoginUser.Id : 0;
                        d["date_created"] = DateTime.UtcNow;
                        s.Insert("media", d);
                    }
                }

                // Optional background thumbnail. Fire-and-forget: the migration
                // does not depend on the thumbnail existing, so we never block
                // the response on it.
                if (buildThumb) ImageThumb.QueueGenerate(relUrl);

                uploaded.Add(new
                {
                    success = true,
                    fileName,
                    url = relUrl
                });
            }

            if (uploaded.Count == 0) { ApiHelper.WriteError("No usable files in request"); return; }

            ApiHelper.WriteJson(new { success = true, message = "Imported", files = uploaded });
        }

        static bool ParseBool(string v)
        {
            v = (v + "").Trim().ToLowerInvariant();
            return v == "true" || v == "1" || v == "yes" || v == "on";
        }

        // Image types the migration intake accepts. Mirrors the content types
        // the migration tool advertises (jpg/png/gif/webp/svg). Anything else —
        // notably server-executable extensions — is rejected.
        static readonly HashSet<string> AllowedImageExts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp", ".ico"
            };

        static bool IsAllowedImageExt(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            return AllowedImageExts.Contains(ext.Trim());
        }
    }
}

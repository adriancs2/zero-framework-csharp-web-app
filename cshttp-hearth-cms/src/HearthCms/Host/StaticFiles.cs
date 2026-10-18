using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsHttp.Web;

namespace HearthCms.Host
{
    // ============================================================
    // StaticFiles - serves the public asset folders from the content root.
    //
    // Under IIS the original app let IIS's StaticFileModule serve /css, /js,
    // /assets, /media ... In this build the reverse proxy (nginx) normally
    // serves them straight from disk; this handler is the fallback for direct
    // runs and for IIS + HttpPlatformHandler, where every request reaches the
    // process. Global decides WHICH paths may be served (public folders only -
    // App_Data and the binaries are never reachable); this class only
    // resolves the file safely and writes it with proper caching headers.
    // ============================================================
    public static class StaticFiles
    {
        const long MaxStaticFileBytes = 200L * 1024 * 1024;

        static readonly Dictionary<string, string> _mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".htm", "text/html; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" },
            { ".mjs", "text/javascript; charset=utf-8" }, { ".map", "application/json; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" }, { ".webmanifest", "application/manifest+json; charset=utf-8" },
            { ".txt", "text/plain; charset=utf-8" }, { ".md", "text/plain; charset=utf-8" },
            { ".xml", "application/xml; charset=utf-8" }, { ".csv", "text/csv; charset=utf-8" },
            { ".svg", "image/svg+xml" }, { ".png", "image/png" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" }, { ".webp", "image/webp" }, { ".avif", "image/avif" }, { ".bmp", "image/bmp" },
            { ".ico", "image/x-icon" },
            { ".woff", "font/woff" }, { ".woff2", "font/woff2" }, { ".ttf", "font/ttf" }, { ".otf", "font/otf" },
            { ".eot", "application/vnd.ms-fontobject" },
            { ".mp4", "video/mp4" }, { ".webm", "video/webm" }, { ".ogg", "audio/ogg" }, { ".mp3", "audio/mpeg" },
            { ".wav", "audio/wav" }, { ".pdf", "application/pdf" }, { ".zip", "application/zip" },
            { ".doc", "application/msword" }, { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            { ".xls", "application/vnd.ms-excel" }, { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { ".ppt", "application/vnd.ms-powerpoint" }, { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        };

        // Extensions that must never be served even from a public folder
        // (e.g. a stray upload of server-side code or a database).
        static readonly HashSet<string> _blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".config", ".dll", ".exe", ".pdb", ".sqlite", ".db", ".asax", ".aspx", ".ashx", ".csproj", ".sh", ".ps1",
        };

        /// <summary>Tries to serve <paramref name="decodedPath"/> from the content root. False if no file matches.</summary>
        public static bool TryServe(string decodedPath)
        {
            HttpContext ctx = HttpContext.Current;
            if (string.IsNullOrEmpty(decodedPath) || decodedPath.IndexOf('\0') >= 0) return false;

            // Reject hidden files/dirs (".git", ".env") and backslash / drive tricks.
            string[] segments = decodedPath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string s = segments[i];
                if (s.Length > 0 && s[0] == '.') return false;
                if (s.IndexOf('\\') >= 0 || s.IndexOf(':') >= 0) return false;
            }

            string root = HostingEnvironment.ApplicationPhysicalPath;
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, decodedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))); }
            catch { return false; }
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;   // traversal guard
            full = HostingEnvironment.ResolveCase(full);

            var info = new FileInfo(full);
            if (!info.Exists) return false;
            if (_blocked.Contains(info.Extension)) return false;

            string method = ctx.Request.HttpMethod;
            if (method != "GET" && method != "HEAD")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.AddHeader("Allow", "GET, HEAD");
                return true;
            }
            if (info.Length > MaxStaticFileBytes) { ctx.Response.StatusCode = 413; return true; }

            string contentType;
            if (!_mime.TryGetValue(info.Extension, out contentType)) contentType = "application/octet-stream";

            DateTime lm = info.LastWriteTimeUtc;
            lm = new DateTime(lm.Year, lm.Month, lm.Day, lm.Hour, lm.Minute, lm.Second, DateTimeKind.Utc);
            string etag = "\"" + info.Length.ToString("x") + "-" + lm.Ticks.ToString("x") + "\"";

            // Versioned URLs (?v=...) are immutable; everything else revalidates cheaply.
            bool versioned = ctx.Request.QueryString["v"] != null;
            ctx.Response.AddHeader("Cache-Control", versioned ? "public, max-age=31536000, immutable" : "public, max-age=0, must-revalidate");
            ctx.Response.AddHeader("ETag", etag);
            ctx.Response.AddHeader("Last-Modified", lm.ToString("R", CultureInfo.InvariantCulture));
            ctx.Response.AddHeader("X-Content-Type-Options", "nosniff");
            ctx.Response.AddHeader("Accept-Ranges", "bytes");

            if (IsNotModified(ctx, etag, lm))
            {
                ctx.Response.StatusCode = 304;
                return true;
            }

            ctx.Response.ContentType = contentType;
            ctx.Response.Charset = null;

            // Single byte-range support (video seeking in Safari/iOS needs it).
            string range = ctx.Request.Headers["Range"];
            long start, end;
            if (range != null && TryParseRange(range, info.Length, out start, out end))
            {
                int len = (int)(end - start + 1);
                byte[] part = new byte[len];
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Seek(start, SeekOrigin.Begin);
                    int read = 0;
                    while (read < len)
                    {
                        int n = fs.Read(part, read, len - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }
                ctx.Response.StatusCode = 206;
                ctx.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + info.Length);
                ctx.Response.BinaryWrite(part);
                return true;
            }
            if (range != null && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) && info.Length > 0 && !range.Contains(","))
            {
                ctx.Response.StatusCode = 416;
                ctx.Response.AddHeader("Content-Range", "bytes */" + info.Length);
                return true;
            }

            ctx.Response.BinaryWrite(ReadShared(full));
            return true;
        }

        static byte[] ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] data = new byte[fs.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = fs.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return data;
            }
        }

        // "bytes=0-499", "bytes=500-", "bytes=-500" (single range only).
        static bool TryParseRange(string header, long length, out long start, out long end)
        {
            start = end = 0;
            if (length <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
            string spec = header.Substring(6).Trim();
            if (spec.IndexOf(',') >= 0) return false;
            int dash = spec.IndexOf('-');
            if (dash < 0) return false;
            string a = spec.Substring(0, dash).Trim(), b = spec.Substring(dash + 1).Trim();
            long x, y;
            if (a.Length == 0)
            {
                if (!long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out y) || y <= 0) return false;
                start = Math.Max(0, length - y); end = length - 1;
            }
            else
            {
                if (!long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out x) || x >= length) return false;
                start = x;
                end = b.Length == 0 ? length - 1
                    : (long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out y) ? Math.Min(y, length - 1) : -1);
                if (end < start) return false;
            }
            return end - start + 1 <= 64L * 1024 * 1024;
        }

        static bool IsNotModified(HttpContext ctx, string etag, DateTime lastModified)
        {
            string inm = ctx.Request.Headers["If-None-Match"];
            if (inm != null)
            {
                foreach (string raw in inm.Split(','))
                {
                    string t = raw.Trim();
                    if (t == "*" || t == etag || t == "W/" + etag) return true;
                }
                return false;
            }
            string ims = ctx.Request.Headers["If-Modified-Since"];
            DateTime since;
            if (ims != null && DateTime.TryParseExact(ims, "R", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out since))
                return lastModified <= since;
            return false;
        }
    }
}

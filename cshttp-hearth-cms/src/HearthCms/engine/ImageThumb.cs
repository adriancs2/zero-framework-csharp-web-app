using SkiaSharp;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // ImageThumb - cover-image / media thumbnail generation.
    //
    // Thumbnails are "resize to fit": the longer side is scaled to a
    // fixed maximum (400px) and the shorter side follows the aspect
    // ratio. Images already smaller than the max are never upscaled.
    //
    //   Original 1200x800  -> 400x267
    //   Original  800x1200 -> 267x400
    //   Original 1000x1000 -> 400x400
    //
    // The thumbnail is a *mirror* of the original path under
    // /media/thumbnail/...  It is NOT tracked in the database; it is a
    // pure on-disk derivative regenerated whenever a post is saved.
    // ============================================================
    public static class ImageThumb
    {
        public const int MaxSide = 400;
        public const string ThumbRootUrl = "/media/thumbnail";

        // True for a root-relative local URL (e.g. "/media/2026/06/x.jpg").
        // External hot-links ("http://...", "//cdn...") are not local.
        public static bool IsLocal(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url[0] != '/') return false;
            if (url.StartsWith("//")) return false;
            if (url.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        static bool IsSupportedImage(string url)
        {
            string ext = Path.GetExtension(url ?? "").ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png"
                || ext == ".gif" || ext == ".bmp";
        }

        // Map an original root-relative URL to its thumbnail URL.
        //   /media/2026/06/x.jpg  -> /media/thumbnail/2026/06/x.jpg
        //   /uploads/x.jpg        -> /media/thumbnail/uploads/x.jpg
        public static string ThumbUrl(string url)
        {
            if (!IsLocal(url)) return url;
            if (url.StartsWith(ThumbRootUrl + "/", StringComparison.OrdinalIgnoreCase)) return url;
            if (url.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
                return ThumbRootUrl + url.Substring("/media".Length);
            return ThumbRootUrl + url;
        }

        // Resolve a root-relative URL to a physical path. Must be called on
        // a request thread (Server.MapPath needs HttpContext).
        static string MapPhysical(string rootRelativeUrl)
        {
            string vpath = "~" + rootRelativeUrl.Replace('/', '/');
            return HttpContext.Current.Server.MapPath(vpath);
        }

        // Render-time helper: return the thumbnail URL when the mirror file
        // actually exists on disk, otherwise the original (so old content and
        // hot-linked images still display).
        public static string DisplayUrl(string url)
        {
            try
            {
                if (!IsLocal(url) || !IsSupportedImage(url)) return url;
                string thumbUrl = ThumbUrl(url);
                if (thumbUrl == url) return url;
                string phys = MapPhysical(thumbUrl);
                if (File.Exists(phys)) return thumbUrl;
            }
            catch { }
            return url;
        }

        // Queue a fire-and-forget thumbnail generation for a local image URL.
        // Physical paths are resolved here (on the request thread); the actual
        // resize runs on a thread-pool thread where HttpContext is unavailable.
        public static void QueueGenerate(string coverUrl)
        {
            if (!IsLocal(coverUrl) || !IsSupportedImage(coverUrl)) return;

            string thumbUrl = ThumbUrl(coverUrl);
            if (thumbUrl == coverUrl) return;

            string srcAbs, dstAbs;
            try
            {
                srcAbs = MapPhysical(coverUrl);
                dstAbs = MapPhysical(thumbUrl);
            }
            catch { return; }

            // fire and forget - never surface to the request
            _ = Task.Run(() =>   // No need for async lambda if the method is synchronous
            {
                try
                {
                    GenerateFile(srcAbs, dstAbs, MaxSide);
                }
                catch { }
            });
        }

        // Resize-to-fit srcAbs into dstAbs. Creates the destination directory.
        // cshttp build: SkiaSharp replaces System.Drawing (GDI+ is Windows-only
        // on modern .NET) - same algorithm, high-quality cubic resampling.
        static void GenerateFile(string srcAbs, string dstAbs, int maxSide)
        {
            if (string.IsNullOrEmpty(srcAbs) || !File.Exists(srcAbs)) return;

            string dstDir = Path.GetDirectoryName(dstAbs);
            if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);

            using (var src = SKBitmap.Decode(srcAbs))
            {
                if (src == null) return;
                int w = src.Width, h = src.Height;
                if (w <= 0 || h <= 0) return;

                double scale = (double)maxSide / Math.Max(w, h);
                if (scale > 1.0) scale = 1.0;   // never upscale
                int nw = Math.Max(1, (int)Math.Round(w * scale));
                int nh = Math.Max(1, (int)Math.Round(h * scale));

                using (var bmp = src.Resize(new SKImageInfo(nw, nh, SKColorType.Rgba8888, SKAlphaType.Premul),
                                            new SKSamplingOptions(SKCubicResampler.Mitchell)))
                {
                    if (bmp == null) return;
                    SaveByExtension(bmp, dstAbs);
                }
            }
        }

        static void SaveByExtension(SKBitmap bmp, string dstAbs)
        {
            string ext = Path.GetExtension(dstAbs).ToLowerInvariant();

            // Skia has no GIF/BMP encoder: write PNG data (browsers sniff image
            // content, so the mirrored file name keeps working).
            SKEncodedImageFormat fmt = SKEncodedImageFormat.Jpeg;
            int quality = 85;
            if (ext == ".png" || ext == ".gif" || ext == ".bmp") { fmt = SKEncodedImageFormat.Png; quality = 100; }

            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(fmt, quality))
            {
                if (data == null) return;
                string tmp = dstAbs + ".tmp";
                using (var fs = File.Create(tmp)) data.SaveTo(fs);
                File.Move(tmp, dstAbs, true);
            }
        }
    }
}
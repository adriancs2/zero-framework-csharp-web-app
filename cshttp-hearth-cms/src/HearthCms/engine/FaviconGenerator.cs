using System.Collections.Generic;
using SkiaSharp;
using System.IO;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // FaviconGenerator - builds a complete favicon set + web manifest
    // from a single master image the user uploads in admin settings.
    //
    // cshttp build: uses SkiaSharp (cross-platform) instead of System.Drawing.
    //
    // Outputs (all OVERWRITTEN on each regeneration):
    //   /favicon.ico                      (multi-size ICO: 16,32,48)
    //   /media/favicon/favicon-16x16.png
    //   /media/favicon/favicon-32x32.png
    //   /media/favicon/favicon-48x48.png
    //   /media/favicon/favicon-180x180.png   (apple-touch-icon)
    //   /media/favicon/favicon-192x192.png   (manifest, maskable)
    //   /media/favicon/favicon-512x512.png   (manifest, maskable)
    //   /media/favicon/manifest.json
    //
    // The master URL lives in the `favicon_source_url` setting. When it is
    // empty NOTHING is generated and NOTHING is emitted in <head> - a fresh
    // install simply has no favicon until the user uploads one.
    //
    // PNG frames are square: the master is scaled to fit and centered on a
    // transparent canvas so non-square masters are not distorted.
    // ============================================================
    public static class FaviconGenerator
    {
        public const string OutputDirUrl = "/media/favicon";
        public const string IcoUrl = "/favicon.ico";
        public const string ManifestUrl = "/media/favicon/manifest.json";

        // PNG sizes written to /media/favicon/favicon-{n}x{n}.png
        static readonly int[] PngSizes = { 16, 32, 48, 180, 192, 512 };
        // Sizes packed into the multi-image /favicon.ico
        static readonly int[] IcoSizes = { 16, 32, 48 };

        // ===== Cache-buster version =====
        // `favicon_version` is a monotonically increasing integer bumped on every
        // successful (re)generation. It is appended as a "?v=N" query string to
        // every emitted favicon/manifest URL so a re-upload busts browser caches
        // without renaming files on disk. Seeded at "1"; a missing/invalid value
        // reads as 1. (The legacy /favicon.ico is intentionally NOT busted - old
        // browsers that fall back to it are rare and the PNG links override it.)
        public static int Version
        {
            get
            {
                return Settings.FaviconVersion;
            }
        }

        // Append the cache-buster query string to a favicon URL.
        public static string Bust(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            string sep = url.IndexOf('?') >= 0 ? "&" : "?";
            return url + sep + "v=" + Version;
        }

        // Advance the cache-buster version by one and persist it.
        static void BumpVersion()
        {
            int v = Version;            // current (>= 1)
            Db.SaveSetting("favicon_version", (v + 1).ToString());
        }

        // Raw (un-busted) on-disk URLs.
        public static string RawUrl16 { get { return OutputDirUrl + "/favicon-16x16.png"; } }
        public static string RawUrl32 { get { return OutputDirUrl + "/favicon-32x32.png"; } }
        public static string RawUrl48 { get { return OutputDirUrl + "/favicon-48x48.png"; } }
        public static string RawUrl180 { get { return OutputDirUrl + "/favicon-180x180.png"; } }
        public static string RawUrl192 { get { return OutputDirUrl + "/favicon-192x192.png"; } }
        public static string RawUrl512 { get { return OutputDirUrl + "/favicon-512x512.png"; } }

        // Cache-busted URLs used in the rendered <head> and manifest.
        public static string Url16 { get { return Bust(RawUrl16); } }
        public static string Url32 { get { return Bust(RawUrl32); } }
        public static string Url48 { get { return Bust(RawUrl48); } }
        public static string Url180 { get { return Bust(RawUrl180); } }
        public static string Url192 { get { return Bust(RawUrl192); } }
        public static string Url512 { get { return Bust(RawUrl512); } }
        public static string ManifestBustedUrl { get { return Bust(ManifestUrl); } }

        // Regenerate the full set from the current `favicon_source_url` setting.
        // Returns true on success. Safe to call on any request thread (needs
        // HttpContext for MapPath). Errors are swallowed and reported via the
        // return value so the caller (settings save) can surface a message.
        public static bool Generate(out string error)
        {
            error = "";
            string srcUrl = Settings.FaviconSourceUrl;
            if (string.IsNullOrEmpty(srcUrl)) { error = "No favicon image set."; return false; }
            if (!IsLocal(srcUrl)) { error = "Favicon image must be an uploaded local image."; return false; }

            string srcAbs;
            try { srcAbs = HttpContext.Current.Server.MapPath("~" + srcUrl); }
            catch (Exception ex) { error = ex.Message; return false; }
            if (!File.Exists(srcAbs)) { error = "Favicon source file not found on disk."; return false; }

            string outDirAbs = HttpContext.Current.Server.MapPath("~" + OutputDirUrl);
            string icoAbs = HttpContext.Current.Server.MapPath("~" + IcoUrl);

            try
            {
                if (!Directory.Exists(outDirAbs)) Directory.CreateDirectory(outDirAbs);

                using (var master = LoadImageCopy(srcAbs))
                {
                    // PNG frames
                    var frames = new Dictionary<int, byte[]>();
                    foreach (int n in PngSizes)
                    {
                        byte[] png = RenderSquarePng(master, n);
                        frames[n] = png;
                        File.WriteAllBytes(Path.Combine(outDirAbs, "favicon-" + n + "x" + n + ".png"), png);
                    }

                    // Multi-image ICO assembled from the PNG frames we just made.
                    var icoFrames = new List<byte[]>();
                    foreach (int n in IcoSizes)
                        if (frames.ContainsKey(n)) icoFrames.Add(frames[n]);
                    byte[] ico = BuildIco(icoFrames, IcoSizes);
                    File.WriteAllBytes(icoAbs, ico);
                }

                // Bump the cache-buster version BEFORE writing the manifest so the
                // manifest's icon src URLs carry the new ?v=N too. Every successful
                // regeneration advances the version exactly once.
                BumpVersion();

                WriteManifest(outDirAbs);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // Load the master fully into memory (the file handle is released
        // immediately). cshttp build: SkiaSharp replaces System.Drawing.
        static SKBitmap LoadImageCopy(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var bmp = SKBitmap.Decode(bytes);
            if (bmp == null) throw new InvalidOperationException("Unsupported or corrupt image file.");
            return bmp;
        }

        // Scale the master to fit an n x n box (no distortion) and center it on
        // a transparent square canvas. Returns PNG bytes.
        static byte[] RenderSquarePng(SKBitmap master, int n)
        {
            var info = new SKImageInfo(n, n, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var surface = SKSurface.Create(info))
            {
                SKCanvas g = surface.Canvas;
                g.Clear(SKColors.Transparent);

                int w = master.Width, h = master.Height;
                double scale = (double)n / Math.Max(w, h);
                int nw = Math.Max(1, (int)Math.Round(w * scale));
                int nh = Math.Max(1, (int)Math.Round(h * scale));
                int x = (n - nw) / 2;
                int y = (n - nh) / 2;

                using (var img = SKImage.FromBitmap(master))
                using (var paint = new SKPaint { IsAntialias = true })
                    g.DrawImage(img, new SKRect(x, y, x + nw, y + nh), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);

                using (var snap = surface.Snapshot())
                using (var data = snap.Encode(SKEncodedImageFormat.Png, 100))
                    return data.ToArray();
            }
        }
        // Assemble a valid .ico file from a set of PNG-encoded frames. Modern
        // browsers accept PNG-compressed frames inside an ICO container, which
        // lets us store 16/32/48 in one file without BMP/AND-mask plumbing.
        //
        // ICO layout:
        //   ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes each) + image data.
        static byte[] BuildIco(List<byte[]> pngFrames, int[] sizes)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                short count = (short)pngFrames.Count;

                // ICONDIR
                bw.Write((short)0);     // reserved
                bw.Write((short)1);     // type 1 = icon
                bw.Write(count);        // image count

                // Offset to the first image: header + all directory entries.
                int offset = 6 + (16 * count);

                // ICONDIRENTRY for each frame
                for (int i = 0; i < pngFrames.Count; i++)
                {
                    int dim = sizes[i];
                    byte b = (byte)(dim >= 256 ? 0 : dim);   // 0 means 256
                    bw.Write(b);                 // width
                    bw.Write(b);                 // height
                    bw.Write((byte)0);           // color palette
                    bw.Write((byte)0);           // reserved
                    bw.Write((short)1);          // color planes
                    bw.Write((short)32);         // bits per pixel
                    bw.Write(pngFrames[i].Length);  // size of image data
                    bw.Write(offset);            // offset of image data
                    offset += pngFrames[i].Length;
                }

                // Image data (raw PNG bytes)
                foreach (var f in pngFrames) bw.Write(f);

                bw.Flush();
                return ms.ToArray();
            }
        }

        // Write /media/favicon/manifest.json using the site name + theme color.
        static void WriteManifest(string outDirAbs)
        {
            string name = (Settings.SiteName ?? "").Trim();
            if (string.IsNullOrEmpty(name)) name = "Website";
            string shortName = name.Length > 12 ? name.Substring(0, 12) : name;
            string themeColor = Settings.ThemeColor;
            string bg = string.IsNullOrEmpty(themeColor) ? "#ffffff" : themeColor;
            string tc = string.IsNullOrEmpty(themeColor) ? "#ffffff" : themeColor;

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"name\": " + JsonString(name) + ",\n");
            sb.Append("  \"short_name\": " + JsonString(shortName) + ",\n");
            sb.Append("  \"start_url\": \"/\",\n");
            sb.Append("  \"display\": \"standalone\",\n");
            sb.Append("  \"background_color\": " + JsonString(bg) + ",\n");
            sb.Append("  \"theme_color\": " + JsonString(tc) + ",\n");
            sb.Append("  \"icons\": [\n");
            sb.Append("    {\n");
            sb.Append("      \"src\": \"" + Url192 + "\",\n");
            sb.Append("      \"sizes\": \"192x192\",\n");
            sb.Append("      \"type\": \"image/png\",\n");
            sb.Append("      \"purpose\": \"any maskable\"\n");
            sb.Append("    },\n");
            sb.Append("    {\n");
            sb.Append("      \"src\": \"" + Url512 + "\",\n");
            sb.Append("      \"sizes\": \"512x512\",\n");
            sb.Append("      \"type\": \"image/png\",\n");
            sb.Append("      \"purpose\": \"any maskable\"\n");
            sb.Append("    }\n");
            sb.Append("  ]\n");
            sb.Append("}\n");

            File.WriteAllText(Path.Combine(outDirAbs, "manifest.json"), sb.ToString(), new UTF8Encoding(false));
        }

        // Minimal JSON string escaper for the few values we emit.
        static string JsonString(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        static bool IsLocal(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (url[0] != '/') return false;
            if (url.StartsWith("//")) return false;
            if (url.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        // True when the generated favicon set exists on disk (the .ico is the
        // sentinel). Used by the header builder to decide whether to emit tags.
        public static bool HasGeneratedSet()
        {
            try
            {
                string icoAbs = HttpContext.Current.Server.MapPath("~" + IcoUrl);
                return File.Exists(icoAbs);
            }
            catch { return false; }
        }
    }
}

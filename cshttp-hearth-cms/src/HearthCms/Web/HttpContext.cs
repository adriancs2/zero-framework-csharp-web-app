using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CsHttp;

namespace CsHttp.Web
{
    // ============================================================
    // System.Web-shaped facade over cshttp.
    //
    // Hearth CMS was written as Pageless ASP.NET: thin static handlers that
    // read HttpContext.Current.Request and write HttpContext.Current.Response.
    // These classes reproduce exactly the subset of System.Web those handlers
    // use, implemented on top of a cshttp HttpRequestMessage and cshttp's
    // HttpResponse builder. No System.Web.dll, no IIS pipeline.
    //
    // Handlers are synchronous, so the context is a [ThreadStatic] that the
    // server sets for the duration of one handler call - just like IIS, it is
    // null on background threads (Task.Run, QueueBackgroundWorkItem).
    // ============================================================
    public sealed class HttpContext
    {
        [ThreadStatic] static HttpContext _current;

        public static HttpContext Current
        {
            get { return _current; }
            internal set { _current = value; }
        }

        public HttpRequest Request { get; private set; }
        public HttpResponse Response { get; private set; }
        public HttpServerUtility Server { get { return HttpServerUtility.Instance; } }
        public HttpApplication ApplicationInstance { get; private set; }
        public IDictionary Items { get; private set; }
        public DateTime Timestamp { get; private set; }

        internal HttpContext(HttpRequest request)
        {
            Request = request;
            Response = new HttpResponse();
            ApplicationInstance = new HttpApplication(this);
            Items = new Hashtable();
            Timestamp = DateTime.Now;
        }

        // A request-less context used while running Application_Start, so
        // startup code can call HttpContext.Current.Server.MapPath (as it could
        // in IIS). Request is null, as code such as CookieScope already expects.
        internal static HttpContext CreateStartupContext()
        {
            return new HttpContext(null);
        }

        internal static HttpContext Create(HttpRequestMessage message, IPEndPoint remote, bool trustProxy)
        {
            return new HttpContext(new HttpRequest(message, remote, trustProxy));
        }
    }

    // Only CompleteRequest() is used: it ends the response.
    public sealed class HttpApplication
    {
        readonly HttpContext _ctx;
        internal HttpApplication(HttpContext ctx) { _ctx = ctx; }
        public void CompleteRequest() { _ctx.Response.End(); }
    }

    public sealed class HttpServerUtility
    {
        internal static readonly HttpServerUtility Instance = new HttpServerUtility();
        public string MapPath(string virtualPath) { return HostingEnvironment.MapPath(virtualPath); }
        public string HtmlEncode(string s) { return System.Web.HttpUtility.HtmlEncode(s); }
        public string HtmlDecode(string s) { return System.Web.HttpUtility.HtmlDecode(s); }
        public string UrlEncode(string s) { return System.Web.HttpUtility.UrlEncode(s); }
        public string UrlDecode(string s) { return System.Web.HttpUtility.UrlDecode(s); }
    }

    // ============================================================
    // HostingEnvironment - application root + virtual path mapping.
    //
    // "~/" is the content root: the folder holding css/, js/, media/,
    // App_Data/ ... (the published output directory by default).
    // ============================================================
    public static class HostingEnvironment
    {
        static string _root = Normalize(AppContext.BaseDirectory);
        static readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        static int _backgroundItems;

        static readonly bool CaseSensitiveFs = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string ApplicationPhysicalPath { get { return _root; } }
        public static string ApplicationVirtualPath { get { return "/"; } }
        public static CancellationToken ShutdownToken { get { return _shutdown.Token; } }
        public static int PendingBackgroundWorkItems { get { return Volatile.Read(ref _backgroundItems); } }

        internal static void SetRoot(string path) { _root = Normalize(Path.GetFullPath(path)); }

        static string Normalize(string p)
        {
            return p.EndsWith(Path.DirectorySeparatorChar.ToString()) ? p : p + Path.DirectorySeparatorChar;
        }

        // Maps "~/App_Data/x", "/media/a.jpg" or "media/a.jpg" to a physical
        // path under the content root. Paths escaping the root are rejected,
        // exactly as Server.MapPath rejects "~/../x".
        public static string MapPath(string virtualPath)
        {
            string p = (virtualPath ?? "").Replace('\\', '/');
            if (p.StartsWith("~")) p = p.Substring(1);
            p = p.TrimStart('/');
            int q = p.IndexOf('?');
            if (q >= 0) p = p.Substring(0, q);

            string full = Path.GetFullPath(Path.Combine(_root, p.Replace('/', Path.DirectorySeparatorChar)));
            string rootNoSep = _root.TrimEnd(Path.DirectorySeparatorChar);
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), rootNoSep, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot use a leading .. to exit above the top directory: " + virtualPath);

            if (CaseSensitiveFs) full = ResolveCase(full);
            return full;
        }

        // The CMS was developed on Windows (case-insensitive) and refers to some
        // folders with a different case than on disk (e.g. "broadsheet-cs" vs
        // "Broadsheet-cs"). On Linux, resolve each missing segment
        // case-insensitively so behaviour matches IIS. Segments that do not
        // exist at all are kept as-is (the caller may be about to create them).
        public static string ResolveCase(string full)
        {
            if (File.Exists(full) || Directory.Exists(full)) return full;
            if (!full.StartsWith(_root, StringComparison.Ordinal)) return full;

            bool trailing = full.EndsWith(Path.DirectorySeparatorChar.ToString());
            string rel = full.Substring(_root.Length).TrimEnd(Path.DirectorySeparatorChar);
            if (rel.Length == 0) return full;

            string[] parts = rel.Split(Path.DirectorySeparatorChar);
            string current = _root.TrimEnd(Path.DirectorySeparatorChar);
            for (int i = 0; i < parts.Length; i++)
            {
                string exact = current + Path.DirectorySeparatorChar + parts[i];
                if (Directory.Exists(exact) || File.Exists(exact)) { current = exact; continue; }

                string match = null;
                try
                {
                    if (Directory.Exists(current))
                    {
                        foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                        {
                            if (string.Equals(Path.GetFileName(entry), parts[i], StringComparison.OrdinalIgnoreCase))
                            { match = entry; break; }
                        }
                    }
                }
                catch { }

                if (match == null)
                {
                    // Nothing on disk: keep the remainder verbatim.
                    for (int j = i; j < parts.Length; j++) current += Path.DirectorySeparatorChar + parts[j];
                    return trailing ? current + Path.DirectorySeparatorChar : current;
                }
                current = match;
            }
            return trailing ? current + Path.DirectorySeparatorChar : current;
        }

        // Fire-and-forget background work that the server waits for (bounded)
        // during graceful shutdown - the IIS QueueBackgroundWorkItem contract.
        public static void QueueBackgroundWorkItem(Action<CancellationToken> work)
        {
            if (work == null) throw new ArgumentNullException("work");
            Interlocked.Increment(ref _backgroundItems);
            Task.Run(() =>
            {
                try { work(_shutdown.Token); }
                catch (Exception ex) { HearthCms.Host.ServerLog.Error("background work item failed", ex); }
                finally { Interlocked.Decrement(ref _backgroundItems); }
            });
        }

        internal static void InitiateShutdown()
        {
            try { _shutdown.Cancel(); } catch { }
        }
    }
}

using System;
using System.IO;
using System.Net;
using CsHttp;

namespace CsHttp.Web
{
    // ============================================================
    // HttpRequest - System.Web.HttpRequest surface over a parsed cshttp
    // HttpRequestMessage. Query string, form, cookies and multipart files
    // stay lazily parsed by cshttp: a handler that only reads Path pays
    // nothing for the rest.
    //
    // Proxy awareness: when the TCP peer is loopback (nginx, or IIS via
    // HttpPlatformHandler on the same host) X-Forwarded-For / -Proto / -Host
    // are honoured, so Url, IsSecureConnection and UserHostAddress describe
    // the real client, as they did under IIS.
    // ============================================================
    public sealed class HttpRequest
    {
        readonly HttpRequestMessage _msg;
        HttpCookieCollection _cookies;
        HttpFileCollection _files;
        Uri _url;

        public HttpRequestMessage Message { get { return _msg; } }

        public string HttpMethod { get { return _msg.Method; } }
        public string RequestType { get { return _msg.Method; } }

        /// <summary>Decoded path without the query string ("/admin/posts").</summary>
        public string Path { get { return _msg.Path ?? "/"; } }

        /// <summary>The raw request-target as sent ("/admin/posts?page=2").</summary>
        public string RawUrl { get { return _msg.RequestTarget; } }

        public HttpContentCollection QueryString { get { return _msg.QueryString ?? HttpContentCollection.Empty; } }
        public HttpContentCollection Form { get { return _msg.Form ?? HttpContentCollection.Empty; } }
        public HttpHeaderCollection Headers { get { return _msg.Headers; } }

        public HttpFileCollection Files
        {
            get { return _files ?? (_files = new HttpFileCollection(_msg.Files ?? HttpPostedFileCollection.Empty)); }
        }

        public HttpCookieCollection Cookies
        {
            get
            {
                if (_cookies == null)
                {
                    var c = new HttpCookieCollection();
                    HttpContentCollection parsed = _msg.Cookies;
                    if (parsed != null)
                    {
                        // First occurrence wins (most specific path, RFC 6265 5.4).
                        foreach (var kv in parsed)
                            if (c[kv.Key] == null) c.Add(new HttpCookie(kv.Key, kv.Value));
                    }
                    _cookies = c;
                }
                return _cookies;
            }
        }

        /// <summary>QueryString, then Form, then Cookies, then Headers (System.Web order).</summary>
        public string this[string key] { get { return _msg[key]; } }

        public bool IsSecureConnection { get; private set; }
        public string UserHostAddress { get; private set; }
        public string UserHostName { get { return UserHostAddress; } }
        public string Host { get; private set; }

        public string UserAgent { get { return _msg.Headers["User-Agent"]; } }
        public string ContentType { get { return _msg.Headers["Content-Type"] ?? ""; } }
        public int ContentLength { get { return _msg.Body != null ? _msg.Body.Length : 0; } }
        public Stream InputStream { get { return new MemoryStream(_msg.Body ?? new byte[0], false); } }

        public Uri UrlReferrer
        {
            get
            {
                Uri u;
                string r = _msg.Headers["Referer"];
                return !string.IsNullOrEmpty(r) && Uri.TryCreate(r, UriKind.Absolute, out u) ? u : null;
            }
        }

        public Uri Url
        {
            get
            {
                if (_url == null)
                {
                    string scheme = IsSecureConnection ? "https" : "http";
                    string host = string.IsNullOrEmpty(Host) ? "localhost" : Host;
                    string target = _msg.RequestTarget ?? "/";
                    if (target.Length == 0 || target[0] != '/') target = "/";
                    Uri u;
                    if (!Uri.TryCreate(scheme + "://" + host + target, UriKind.Absolute, out u))
                        u = new Uri(scheme + "://localhost/");
                    _url = u;
                }
                return _url;
            }
        }

        internal HttpRequest(HttpRequestMessage msg, IPEndPoint remote, bool trustProxy)
        {
            _msg = msg;
            UserHostAddress = remote != null ? remote.Address.ToString() : "";
            Host = (msg.Headers["Host"] ?? "").Trim();
            IsSecureConnection = false;

            if (trustProxy)
            {
                // nginx: proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                // IIS HttpPlatformHandler adds X-Forwarded-For (with :port) too.
                // The right-most entry is the address the proxy itself saw.
                string xff = msg.Headers["X-Forwarded-For"];
                if (!string.IsNullOrEmpty(xff))
                {
                    int comma = xff.LastIndexOf(',');
                    string last = (comma >= 0 ? xff.Substring(comma + 1) : xff).Trim();
                    last = StripPort(last);
                    if (last.Length > 0) UserHostAddress = last;
                }

                string proto = msg.Headers["X-Forwarded-Proto"];
                if (proto != null && proto.Trim().Equals("https", StringComparison.OrdinalIgnoreCase))
                    IsSecureConnection = true;

                // IIS HttpPlatformHandler marks TLS-terminated requests this way.
                string arrSsl = msg.Headers["X-ARR-SSL"];
                if (!string.IsNullOrEmpty(arrSsl)) IsSecureConnection = true;

                string fwdHost = msg.Headers["X-Forwarded-Host"];
                if (!string.IsNullOrEmpty(fwdHost))
                {
                    int comma = fwdHost.IndexOf(',');
                    Host = (comma >= 0 ? fwdHost.Substring(0, comma) : fwdHost).Trim();
                }
            }
        }

        // "203.0.113.5:51234" -> "203.0.113.5", "[2001:db8::1]:443" -> "2001:db8::1".
        static string StripPort(string s)
        {
            if (s.StartsWith("["))
            {
                int end = s.IndexOf(']');
                return end > 0 ? s.Substring(1, end - 1) : s;
            }
            int colon = s.IndexOf(':');
            if (colon > 0 && s.IndexOf(':', colon + 1) < 0) return s.Substring(0, colon);
            return s;
        }
    }

    // ============================================================
    // Uploaded files: System.Web.HttpPostedFile surface over cshttp's
    // in-memory multipart parts.
    // ============================================================
    public sealed class HttpPostedFile
    {
        readonly CsHttp.HttpPostedFile _f;
        internal HttpPostedFile(CsHttp.HttpPostedFile f) { _f = f; }

        public string FieldName { get { return _f.Name; } }
        public string FileName { get { return _f.FileName ?? ""; } }
        public string ContentType { get { return _f.ContentType ?? "application/octet-stream"; } }
        public int ContentLength { get { return _f.Size; } }
        public Stream InputStream { get { return new MemoryStream(_f.Bytes ?? new byte[0], false); } }
        public byte[] Bytes { get { return _f.Bytes ?? new byte[0]; } }

        public void SaveAs(string filename)
        {
            File.WriteAllBytes(filename, _f.Bytes ?? new byte[0]);
        }
    }

    public sealed class HttpFileCollection
    {
        readonly HttpPostedFile[] _files;
        readonly CsHttp.HttpPostedFileCollection _src;

        internal HttpFileCollection(CsHttp.HttpPostedFileCollection src)
        {
            _src = src;
            _files = new HttpPostedFile[src.Count];
            for (int i = 0; i < src.Count; i++) _files[i] = new HttpPostedFile(src[i]);
        }

        public int Count { get { return _files.Length; } }
        public HttpPostedFile this[int index] { get { return _files[index]; } }

        public HttpPostedFile this[string name]
        {
            get
            {
                for (int i = 0; i < _files.Length; i++)
                    if (string.Equals(_files[i].FieldName, name, StringComparison.OrdinalIgnoreCase)) return _files[i];
                return null;
            }
        }

        public string[] AllKeys { get { return _src.AllKeys; } }
        public HttpPostedFile Get(int index) { return _files[index]; }
        public HttpPostedFile Get(string name) { return this[name]; }
    }
}

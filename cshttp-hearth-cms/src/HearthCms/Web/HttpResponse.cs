using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CsHttp.Web
{
    public enum SameSiteMode
    {
        Unspecified = -1,
        None = 0,
        Lax = 1,
        Strict = 2,
    }

    public sealed class HttpCookie
    {
        public HttpCookie(string name) : this(name, "") { }
        public HttpCookie(string name, string value)
        {
            Name = name;
            Value = value ?? "";
            Path = "/";
            SameSite = SameSiteMode.Unspecified;
        }

        public string Name { get; set; }
        public string Value { get; set; }
        public string Path { get; set; }
        public string Domain { get; set; }
        public DateTime Expires { get; set; }
        public bool Secure { get; set; }
        public bool HttpOnly { get; set; }
        public SameSiteMode SameSite { get; set; }

        // Serialises to a Set-Cookie header value. Values are validated (not
        // encoded) - the CMS only stores hex/base64-url tokens and small ints.
        internal string ToSetCookieHeader()
        {
            var sb = new StringBuilder();
            sb.Append(Name).Append('=').Append(Value ?? "");
            if (!string.IsNullOrEmpty(Path)) sb.Append("; Path=").Append(Path);
            if (!string.IsNullOrEmpty(Domain)) sb.Append("; Domain=").Append(Domain);
            if (Expires != DateTime.MinValue)
            {
                DateTime utc = Expires.Kind == DateTimeKind.Utc ? Expires : Expires.ToUniversalTime();
                if (utc.Year < 1971) utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                sb.Append("; Expires=").Append(utc.ToString("R", CultureInfo.InvariantCulture));
            }
            if (Secure) sb.Append("; Secure");
            if (HttpOnly) sb.Append("; HttpOnly");
            switch (SameSite)
            {
                case SameSiteMode.Lax: sb.Append("; SameSite=Lax"); break;
                case SameSiteMode.Strict: sb.Append("; SameSite=Strict"); break;
                case SameSiteMode.None: sb.Append("; SameSite=None"); break;
            }
            return sb.ToString();
        }
    }

    public sealed class HttpCookieCollection
    {
        readonly List<HttpCookie> _list = new List<HttpCookie>();

        public int Count { get { return _list.Count; } }

        public HttpCookie this[string name] { get { return Get(name); } }
        public HttpCookie this[int index] { get { return _list[index]; } }

        public HttpCookie Get(string name)
        {
            for (int i = 0; i < _list.Count; i++)
                if (string.Equals(_list[i].Name, name, StringComparison.Ordinal)) return _list[i];
            return null;
        }

        public void Add(HttpCookie cookie) { _list.Add(cookie); }

        /// <summary>Adds or replaces the cookie with the same name.</summary>
        public void Set(HttpCookie cookie)
        {
            for (int i = 0; i < _list.Count; i++)
            {
                if (string.Equals(_list[i].Name, cookie.Name, StringComparison.Ordinal)) { _list[i] = cookie; return; }
            }
            _list.Add(cookie);
        }

        public void Remove(string name)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
                if (string.Equals(_list[i].Name, name, StringComparison.Ordinal)) _list.RemoveAt(i);
        }

        public void Clear() { _list.Clear(); }

        public string[] AllKeys
        {
            get
            {
                var keys = new string[_list.Count];
                for (int i = 0; i < keys.Length; i++) keys[i] = _list[i].Name;
                return keys;
            }
        }
    }

    // ============================================================
    // HttpResponse - buffered System.Web.HttpResponse surface.
    //
    // Everything is buffered and serialised once by cshttp's HttpResponse
    // builder after the handler returns. End()/CompleteRequest()/
    // SuppressContent mirror the IIS behaviour the Pageless code relies on:
    // after the response is ended, further writes are discarded.
    // ============================================================
    public sealed class HttpResponse
    {
        readonly List<KeyValuePair<string, string>> _headers = new List<KeyValuePair<string, string>>();
        MemoryStream _body = new MemoryStream();
        bool _suppress;

        public HttpResponse()
        {
            StatusCode = 200;
            ContentType = "text/html";
            Charset = "utf-8";
            Cookies = new HttpCookieCollection();
        }

        public int StatusCode { get; set; }
        public string StatusDescription { get; set; }
        public string ContentType { get; set; }
        public string Charset { get; set; }
        public HttpCookieCollection Cookies { get; private set; }
        public bool TrySkipIisCustomErrors { get; set; }
        public bool IsClientConnected { get { return true; } }
        public bool IsEnded { get; private set; }
        public string RedirectLocation { get; private set; }
        public Encoding ContentEncoding { get { return Encoding.UTF8; } set { } }
        public long BodyLength { get { return _body.Length; } }

        public bool SuppressContent
        {
            get { return _suppress; }
            set { _suppress = value; }
        }

        bool Writable { get { return !IsEnded && !_suppress; } }

        public void Write(string s)
        {
            if (!Writable || string.IsNullOrEmpty(s)) return;
            byte[] b = Encoding.UTF8.GetBytes(s);
            _body.Write(b, 0, b.Length);
        }

        public void Write(char c) { Write(c.ToString()); }
        public void Write(object o) { if (o != null) Write(o.ToString()); }

        public void BinaryWrite(byte[] data)
        {
            if (!Writable || data == null || data.Length == 0) return;
            _body.Write(data, 0, data.Length);
        }

        public void WriteFile(string path)
        {
            BinaryWrite(File.ReadAllBytes(path));
        }

        public void AddHeader(string name, string value) { AppendHeader(name, value); }

        public void AppendHeader(string name, string value)
        {
            if (!IsToken(name)) throw new ArgumentException("Invalid header name: " + name);
            value = value ?? "";
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\r' || c == '\n' || c == '\0')
                    throw new ArgumentException("Header value for '" + name + "' contains a forbidden character.");
            }
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) { ContentType = value; Charset = null; return; }
            _headers.Add(new KeyValuePair<string, string>(name, value));
        }

        public void Clear() { ClearContent(); ClearHeaders(); }
        public void ClearContent() { _body = new MemoryStream(); }
        public void ClearHeaders() { _headers.Clear(); Cookies.Clear(); }

        public void Flush() { /* buffered: the whole response is sent once the handler returns */ }

        public void End() { IsEnded = true; }

        public void Redirect(string url) { Redirect(url, true); }

        public void Redirect(string url, bool endResponse)
        {
            SetRedirect(url, 302);
            if (endResponse) End();
        }

        public void RedirectPermanent(string url) { SetRedirect(url, 301); End(); }
        public void RedirectPermanent(string url, bool endResponse) { SetRedirect(url, 301); if (endResponse) End(); }

        void SetRedirect(string url, int status)
        {
            if (string.IsNullOrEmpty(url)) url = "/";
            if (url.StartsWith("~/")) url = url.Substring(1);
            for (int i = 0; i < url.Length; i++)
                if (url[i] == '\r' || url[i] == '\n') { url = "/"; break; }
            _body = new MemoryStream();
            StatusCode = status;
            RedirectLocation = url;
            string enc = System.Web.HttpUtility.HtmlAttributeEncode(url);
            ContentType = "text/html";
            Charset = "utf-8";
            byte[] b = Encoding.UTF8.GetBytes("<html><head><title>Object moved</title></head><body><h2>Object moved to <a href=\"" + enc + "\">here</a>.</h2></body></html>");
            _body.Write(b, 0, b.Length);
        }

        string EffectiveContentType()
        {
            string ct = ContentType;
            if (string.IsNullOrEmpty(ct)) return null;
            if (!string.IsNullOrEmpty(Charset) && ct.IndexOf("charset", StringComparison.OrdinalIgnoreCase) < 0
                && (ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                    || ct.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
                    || ct.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0
                    || ct.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0))
                ct += "; charset=" + Charset;
            return ct;
        }

        /// <summary>Serialises the buffered response with cshttp's HttpResponse builder.</summary>
        internal byte[] BuildBytes(bool isHeadRequest, bool keepAlive, string serverName)
        {
            var r = new CsHttp.HttpResponse(StatusCode);
            r.Header("Date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
            if (serverName != null) r.Header("Server", serverName);

            bool bodyAllowed = !(StatusCode < 200 || StatusCode == 204 || StatusCode == 304);
            string ct = EffectiveContentType();
            if (bodyAllowed && _body.Length > 0 && ct != null)
                r.Header("Content-Type", ct);

            if (RedirectLocation != null) r.Header("Location", RedirectLocation);

            for (int i = 0; i < _headers.Count; i++)
                r.Header(_headers[i].Key, _headers[i].Value);

            for (int i = 0; i < Cookies.Count; i++)
                r.Header("Set-Cookie", Cookies[i].ToSetCookieHeader());

            r.Header("Connection", keepAlive ? "keep-alive" : "close");

            byte[] body = _body.ToArray();
            if (bodyAllowed) r.Body(body); // always emits Content-Length (keep-alive safe)

            byte[] bytes = r.ToBytes();
            if (isHeadRequest && bodyAllowed && body.Length > 0)
            {
                int headerLength = bytes.Length - body.Length;
                byte[] head = new byte[headerLength];
                Buffer.BlockCopy(bytes, 0, head, 0, headerLength);
                return head;
            }
            return bytes;
        }

        internal static byte[] BuildError(int statusCode, string message, string serverName)
        {
            var w = new HttpResponse();
            w.StatusCode = statusCode;
            w.ContentType = "text/plain";
            w.Write(statusCode + " " + CsHttp.HttpResponse.GetDefaultReasonPhrase(statusCode) +
                    (string.IsNullOrEmpty(message) ? "" : "\n" + message) + "\n");
            return w.BuildBytes(false, false, serverName);
        }

        static bool IsToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0;
                if (!ok) return false;
            }
            return true;
        }
    }
}

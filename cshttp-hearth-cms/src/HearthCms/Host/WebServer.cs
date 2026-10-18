using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsHttp;
using CsHttp.Web;

namespace HearthCms.Host
{
    public sealed class WebServerOptions
    {
        public IPAddress BindAddress { get; set; } = IPAddress.Loopback;
        public int Port { get; set; } = 5080;
        public int MaxConnections { get; set; } = 1000;
        public int MaxHeaderBytes { get; set; } = 64 * 1024;
        public int MaxBodyBytes { get; set; } = 50 * 1024 * 1024;   // Web.config maxAllowedContentLength
        public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(120);  // large media uploads
        public TimeSpan ShutdownGrace { get; set; } = TimeSpan.FromSeconds(10);
        public string ServerName { get; set; } = "hearth-cshttp";
        public bool LogRequests { get; set; } = true;

        /// <summary>Honour X-Forwarded-* headers when the TCP peer is loopback (nginx on the same host).</summary>
        public bool TrustLoopbackProxy { get; set; } = true;
    }

    /// <summary>
    /// The part Kestrel normally provides: a TCP listener, an async per-connection
    /// read loop, HTTP/1.1 keep-alive, timeouts, limits and graceful shutdown.
    ///
    /// HTTP parsing is delegated to cshttp (HttpParser.ParseRequest). The loop
    /// pre-checks message framing itself so that it only calls the parser when a
    /// complete message can be present:
    ///   1. wait until the header terminator (CRLF CRLF) has arrived
    ///   2. if Content-Length is known, wait until the whole body has arrived
    ///   3. only then parse (chunked bodies are re-parsed until complete)
    /// This also avoids the O(n^2) cost of re-parsing a large body on every read.
    /// </summary>
    public sealed class WebServer
    {
        public delegate void RequestHandler(HttpContext context);

        private readonly WebServerOptions _options;
        private readonly RequestHandler _handler;
        private readonly ParserOptions _parserOptions;

        private int _activeConnections;
        private long _totalConnections;
        private long _totalRequests;
        private CancellationToken _stopping;

        public static readonly DateTime StartedUtc = DateTime.UtcNow;

        public int ActiveConnections { get { return Volatile.Read(ref _activeConnections); } }
        public long TotalConnections { get { return Interlocked.Read(ref _totalConnections); } }
        public long TotalRequests { get { return Interlocked.Read(ref _totalRequests); } }

        public static WebServer Instance { get; private set; }

        public WebServer(WebServerOptions options, RequestHandler handler)
        {
            _options = options;
            _handler = handler;
            _parserOptions = new ParserOptions
            {
                MaxHeaderSectionSize = options.MaxHeaderBytes,
                MaxBodySize = options.MaxBodyBytes,
                // Keep chunk sizes far below int.MaxValue (cshttp overflows on 0x7FFFFFFF).
                MaxChunkSize = Math.Min(options.MaxBodyBytes, 16 * 1024 * 1024),
            };
            Instance = this;
        }

        public async Task RunAsync(CancellationToken stopping)
        {
            _stopping = stopping;
            var listener = new TcpListener(_options.BindAddress, _options.Port);
            listener.Start(512);
            var slots = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);

            Log("listening on http://" + FormatEndPoint(_options.BindAddress, _options.Port) + "/");

            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    Socket socket;
                    try
                    {
                        socket = await listener.AcceptSocketAsync(stopping).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException ex)
                    {
                        Log("accept failed: " + ex.SocketErrorCode);
                        continue;
                    }

                    if (!slots.Wait(0))
                    {
                        // Over capacity: refuse instead of queueing unbounded work.
                        CloseSocket(socket);
                        continue;
                    }

                    socket.NoDelay = true;
                    Interlocked.Increment(ref _activeConnections);
                    Interlocked.Increment(ref _totalConnections);

                    _ = Task.Run(async () =>
                    {
                        try { await HandleConnectionAsync(socket).ConfigureAwait(false); }
                        catch (Exception ex) { Log("connection error: " + ex.GetType().Name + ": " + ex.Message); }
                        finally
                        {
                            CloseSocket(socket);
                            Interlocked.Decrement(ref _activeConnections);
                            slots.Release();
                        }
                    });
                }
            }
            finally
            {
                listener.Stop();
            }

            // Graceful drain: idle keep-alive connections are cancelled via the stopping
            // token; in-flight requests are allowed to finish.
            Log("stopping, waiting for " + ActiveConnections + " connection(s)...");
            var sw = Stopwatch.StartNew();
            while ((ActiveConnections > 0 || HostingEnvironment.PendingBackgroundWorkItems > 0) && sw.Elapsed < _options.ShutdownGrace)
                await Task.Delay(100).ConfigureAwait(false);
            Log("stopped.");
        }

        // ─── Per-connection loop ────────────────────────────────────────

        private async Task HandleConnectionAsync(Socket socket)
        {
            var remote = socket.RemoteEndPoint as IPEndPoint;
            bool trustProxy = _options.TrustLoopbackProxy && remote != null && IPAddress.IsLoopback(remote.Address);

            byte[] buffer = new byte[8192];
            int count = 0;

            while (true)
            {
                // ---- 1. Receive one complete request ------------------------
                ParseResult parsed = null;
                bool sentContinue = false;
                CancellationTokenSource requestCts = null;

                try
                {
                    while (parsed == null)
                    {
                        if (count > 0)
                        {
                            if (requestCts == null)
                            {
                                requestCts = new CancellationTokenSource(_options.RequestTimeout);
                            }

                            FrameCheck check = CheckFrame(buffer, count, out parsed, out int errorStatus, out bool wantsContinue, out int expectedTotal);
                            if (check == FrameCheck.Error)
                            {
                                await SendErrorAndCloseAsync(socket, errorStatus, null).ConfigureAwait(false);
                                return;
                            }
                            if (check == FrameCheck.Ready)
                                break;

                            if (wantsContinue && !sentContinue)
                            {
                                sentContinue = true;
                                await SendAsync(socket, Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), requestCts.Token).ConfigureAwait(false);
                            }

                            if (count == buffer.Length)
                            {
                                int limit = _options.MaxHeaderBytes + _options.MaxBodyBytes;
                                if (buffer.Length >= limit)
                                {
                                    await SendErrorAndCloseAsync(socket, 413, null).ConfigureAwait(false);
                                    return;
                                }
                                int newSize = Math.Max(buffer.Length * 2, expectedTotal);
                                if (newSize > limit) newSize = limit;
                                Array.Resize(ref buffer, newSize);
                            }
                        }

                        // Between requests: keep-alive timeout, cancelled immediately on shutdown.
                        // Inside a request: request timeout (protects against slow senders).
                        int n;
                        if (count == 0)
                        {
                            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(_stopping))
                            {
                                idle.CancelAfter(_options.KeepAliveTimeout);
                                n = await socket.ReceiveAsync(new Memory<byte>(buffer, count, buffer.Length - count), SocketFlags.None, idle.Token).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            n = await socket.ReceiveAsync(new Memory<byte>(buffer, count, buffer.Length - count), SocketFlags.None, requestCts.Token).ConfigureAwait(false);
                        }

                        if (n == 0)
                            return; // peer closed
                        count += n;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (count > 0 && !_stopping.IsCancellationRequested)
                    {
                        try { await SendErrorAndCloseAsync(socket, 408, null).ConfigureAwait(false); }
                        catch (Exception) { }
                    }
                    return;
                }
                catch (SocketException) { return; }
                finally
                {
                    if (requestCts != null) requestCts.Dispose();
                }

                // ---- 2. Dispatch ------------------------------------------------
                HttpRequestMessage request = parsed.Request;
                int consumed = parsed.BytesConsumed; // offset is always 0, so absolute == relative
                Interlocked.Increment(ref _totalRequests);

                // RFC 9112 3.2: a server MUST respond 400 to an HTTP/1.1 request without exactly one Host.
                if (request.VersionMajor == 1 && request.VersionMinor >= 1 && request.Headers.GetValues("Host").Length != 1)
                {
                    await SendErrorAndCloseAsync(socket, 400, "Missing or duplicate Host header.").ConfigureAwait(false);
                    return;
                }

                bool keepAlive = WantsKeepAlive(request) && !_stopping.IsCancellationRequested;
                var context = HttpContext.Create(request, remote, trustProxy);
                var sw = Stopwatch.StartNew();

                HttpContext.Current = context;
                try
                {
                    _handler(context);
                }
                catch (Exception ex)
                {
                    ServerLog.Error("unhandled exception in " + request.Method + " " + request.RequestTarget, ex);
                    context.Response.Clear();
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "text/plain";
                    context.Response.Write("500 Internal Server Error\n");
                }
                finally
                {
                    HttpContext.Current = null;
                }

                byte[] output = context.Response.BuildBytes(request.Method == "HEAD", keepAlive, _options.ServerName);

                try
                {
                    using (var sendCts = new CancellationTokenSource(_options.RequestTimeout))
                        await SendAsync(socket, output, sendCts.Token).ConfigureAwait(false);
                }
                catch (Exception) { return; }

                LogRequest(context.Request.UserHostAddress + " " + request.Method + " " + request.RequestTarget + " " +
                    context.Response.StatusCode + " " + output.Length + "B " + sw.ElapsedMilliseconds + "ms");

                // ---- 3. Keep pipelined leftovers, then loop or close --------------
                int leftover = count - consumed;
                if (leftover > 0)
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, leftover);
                count = leftover;
                if (buffer.Length > 64 * 1024 && count <= 8192)
                    Array.Resize(ref buffer, 8192);

                if (!keepAlive)
                    return;
            }
        }

        // ─── Framing pre-check ──────────────────────────────────────────

        private enum FrameCheck { NeedMore, Ready, Error }

        private FrameCheck CheckFrame(byte[] buf, int count, out ParseResult parsed, out int errorStatus,
            out bool wantsContinue, out int expectedTotal)
        {
            parsed = null;
            errorStatus = 0;
            wantsContinue = false;
            expectedTotal = 0;

            int headerEnd = FindHeaderEnd(buf, count);
            if (headerEnd < 0)
            {
                if (count >= _options.MaxHeaderBytes)
                {
                    errorStatus = 431;
                    return FrameCheck.Error;
                }
                return FrameCheck.NeedMore;
            }

            long contentLength;
            bool chunked;
            ScanFraming(buf, headerEnd, out contentLength, out chunked, out wantsContinue);

            if (contentLength > _options.MaxBodyBytes)
            {
                errorStatus = 413;
                return FrameCheck.Error;
            }

            if (!chunked && contentLength > 0)
            {
                expectedTotal = (int)(headerEnd + contentLength);
                if (count < expectedTotal)
                    return FrameCheck.NeedMore; // don't re-parse until the body is complete
            }

            ParseResult result;
            try
            {
                result = HttpParser.ParseRequest(buf, 0, count, _parserOptions);
            }
            catch (Exception)
            {
                // The parser must never throw, but malformed numbers can trigger exceptions inside it.
                errorStatus = 400;
                return FrameCheck.Error;
            }

            if (result.Success)
            {
                parsed = result;
                wantsContinue = false;
                return FrameCheck.Ready;
            }

            switch (result.Error.Kind)
            {
                case ParseErrorKind.Incomplete:
                    return FrameCheck.NeedMore;
                case ParseErrorKind.BodyTooLarge:
                case ParseErrorKind.ChunkTooLarge:
                    errorStatus = 413; break;
                case ParseErrorKind.StartLineTooLong:
                    errorStatus = 414; break;
                case ParseErrorKind.HeaderLineTooLong:
                case ParseErrorKind.HeaderSectionTooLarge:
                case ParseErrorKind.TooManyHeaders:
                    errorStatus = 431; break;
                case ParseErrorKind.InvalidTransferEncoding:
                    errorStatus = 501; break;
                default:
                    errorStatus = 400; break;
            }
            return FrameCheck.Error;
        }

        /// <summary>Index just after the blank line ending the header section (CRLFCRLF or LFLF), or -1.</summary>
        private static int FindHeaderEnd(byte[] buf, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                if (i + 1 < count && buf[i + 1] == (byte)'\n') return i + 2;
                if (i + 2 < count && buf[i + 1] == (byte)'\r' && buf[i + 2] == (byte)'\n') return i + 3;
            }
            return -1;
        }

        /// <summary>
        /// Lightweight header scan used only for flow control (how much to read, whether to
        /// send 100 Continue). The authoritative parse is always done by cshttp.
        /// </summary>
        private static void ScanFraming(byte[] buf, int headerEnd, out long contentLength, out bool chunked, out bool expectContinue)
        {
            contentLength = -1;
            chunked = false;
            expectContinue = false;

            int lineStart = 0;
            bool firstLine = true;
            for (int i = 0; i < headerEnd; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                int lineEnd = i;
                if (lineEnd > lineStart && buf[lineEnd - 1] == (byte)'\r') lineEnd--;

                if (!firstLine && lineEnd > lineStart)
                {
                    string line = Encoding.ASCII.GetString(buf, lineStart, lineEnd - lineStart);
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string name = line.Substring(0, colon).Trim();
                        string value = line.Substring(colon + 1).Trim();
                        if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            long v;
                            if (long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v))
                                contentLength = v;
                        }
                        else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        {
                            chunked = true;
                        }
                        else if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase)
                                 && value.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                        {
                            expectContinue = true;
                        }
                    }
                }
                firstLine = false;
                lineStart = i + 1;
            }
        }

        private static bool WantsKeepAlive(HttpRequestMessage request)
        {
            string connection = request.Headers["Connection"] ?? "";
            if (connection.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (request.VersionMajor == 1 && request.VersionMinor == 0)
                return connection.IndexOf("keep-alive", StringComparison.OrdinalIgnoreCase) >= 0;
            return true;
        }

        // ─── Socket helpers ─────────────────────────────────────────────

        private static async Task SendAsync(Socket socket, byte[] data, CancellationToken token)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int sent = await socket.SendAsync(new ReadOnlyMemory<byte>(data, offset, data.Length - offset), SocketFlags.None, token).ConfigureAwait(false);
                if (sent <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                offset += sent;
            }
        }

        /// <summary>
        /// Sends an error response, then closes politely: half-close our side and discard whatever
        /// the client is still uploading (bounded). Closing with unread data would make the OS send
        /// a TCP RST, and the client would lose the error response before reading it.
        /// </summary>
        private static async Task SendErrorAndCloseAsync(Socket socket, int status, string message)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    await SendAsync(socket, CsHttp.Web.HttpResponse.BuildError(status, message, "hearth-cshttp"), cts.Token).ConfigureAwait(false);
                    socket.Shutdown(SocketShutdown.Send);

                    byte[] sink = new byte[16384];
                    long drained = 0;
                    while (drained < 4 * 1024 * 1024)
                    {
                        int n = await socket.ReceiveAsync(new Memory<byte>(sink), SocketFlags.None, cts.Token).ConfigureAwait(false);
                        if (n == 0) break;
                        drained += n;
                    }
                }
            }
            catch (Exception) { /* best effort; the socket is closed by the caller */ }
        }

        private static void CloseSocket(Socket socket)
        {
            try { socket.Shutdown(SocketShutdown.Both); } catch (Exception) { }
            try { socket.Dispose(); } catch (Exception) { }
        }

        private static string FormatEndPoint(IPAddress address, int port)
        {
            string host = address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString();
            return host + ":" + port;
        }

        public static void Log(string message) { ServerLog.Info(message); }

        private void LogRequest(string line) { if (_options.LogRequests) ServerLog.Info(line); }
    }
}

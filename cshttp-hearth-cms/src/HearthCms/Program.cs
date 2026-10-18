using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CsHttp.Web;
using HearthCms.Host;

namespace HearthCms
{
    // ============================================================
    // Entry point - a plain console application.
    //
    //   HearthCms [--port 5080] [--bind 127.0.0.1] [--contentroot <dir>] [--quiet]
    //
    // Configuration precedence (highest first):
    //   command line  >  HTTP_PLATFORM_PORT (set by IIS HttpPlatformHandler)
    //                 >  HEARTH_PORT / HEARTH_BIND / HEARTH_CONTENT_ROOT  >  defaults
    //
    // Default bind is 127.0.0.1: the intended deployment is behind a reverse
    // proxy (nginx on Linux, IIS on Windows). Use --bind 0.0.0.0 to expose the
    // process directly.
    //
    // Process supervision is the operating system's job:
    //   Linux   : systemd (deploy/linux/hearthcms.service), stops with SIGTERM
    //   Windows : IIS HttpPlatformHandler starts/restarts the process
    //             (deploy/windows/web.config)
    // ============================================================
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            var options = new WebServerOptions();
            string contentRoot = Environment.GetEnvironmentVariable("HEARTH_CONTENT_ROOT");

            string envPort = Environment.GetEnvironmentVariable("HTTP_PLATFORM_PORT");
            if (string.IsNullOrEmpty(envPort)) envPort = Environment.GetEnvironmentVariable("HEARTH_PORT");
            int p;
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out p)) options.Port = p;

            string envBind = Environment.GetEnvironmentVariable("HEARTH_BIND");
            if (!string.IsNullOrEmpty(envBind)) options.BindAddress = IPAddress.Parse(envBind);

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "--port" && next != null) { options.Port = int.Parse(next); i++; }
                else if (a == "--bind" && next != null) { options.BindAddress = IPAddress.Parse(next); i++; }
                else if (a == "--contentroot" && next != null) { contentRoot = next; i++; }
                else if (a == "--quiet") { options.LogRequests = false; }
                else if (a == "--help" || a == "-h" || a == "/?")
                {
                    Console.WriteLine("HearthCms [--port 5080] [--bind 127.0.0.1] [--contentroot <dir>] [--quiet]");
                    return 0;
                }
            }

            if (!string.IsNullOrEmpty(contentRoot)) HostingEnvironment.SetRoot(contentRoot);
            ServerLog.Configure(Path.Combine(HostingEnvironment.ApplicationPhysicalPath, "App_Data", "logs"));

            Console.WriteLine("Hearth CMS on cshttp - no ASP.NET, no IIS pipeline, no Kestrel");
            Console.WriteLine("  runtime      : " + RuntimeInformation.FrameworkDescription + " on " + RuntimeInformation.OSDescription);
            Console.WriteLine("  content root : " + HostingEnvironment.ApplicationPhysicalPath);

            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                ServerLog.Error("unhandled exception (process terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += delegate (object s, UnobservedTaskExceptionEventArgs e)
            {
                ServerLog.Error("unobserved task exception", e.Exception);
                e.SetObserved();
            };

            // Application_Start runs with a request-less context so startup code
            // can use Server.MapPath exactly as it did under IIS.
            HttpContext.Current = HttpContext.CreateStartupContext();
            try { Global.Application_Start(); }
            finally { HttpContext.Current = null; }

            using (var cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                // systemd sends SIGTERM on "systemctl stop"; SIGINT/SIGQUIT for consoles.
                using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; cts.Cancel(); }))
                using (PosixSignalRegistration.Create(PosixSignal.SIGQUIT, c => { c.Cancel = true; cts.Cancel(); }))
                {
                    var server = new WebServer(options, Global.Application_BeginRequest);
                    try
                    {
                        await server.RunAsync(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        ServerLog.Error("server failed to start on " + options.BindAddress + ":" + options.Port, ex);
                        return 1;
                    }
                }
            }

            HostingEnvironment.InitiateShutdown();
            Global.Application_End();
            return 0;
        }
    }
}

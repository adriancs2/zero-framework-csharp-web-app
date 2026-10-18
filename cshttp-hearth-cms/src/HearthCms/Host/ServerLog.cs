using System;
using System.IO;
using System.Text;

namespace HearthCms.Host
{
    // Console logging (captured by systemd-journald on Linux and by the IIS
    // HttpPlatformHandler stdout log on Windows) plus a daily error log file
    // under App_Data/logs so failures survive a restart.
    public static class ServerLog
    {
        static readonly object _lock = new object();
        static string _dir;

        public static void Configure(string logDirectory)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                _dir = logDirectory;
            }
            catch { _dir = null; }
        }

        public static void Info(string message)
        {
            lock (_lock)
                Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message);
        }

        public static void Error(string message, Exception ex = null)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " ERROR " + message + (ex != null ? Environment.NewLine + ex : "");
            lock (_lock)
            {
                Console.Error.WriteLine(line);
                if (_dir == null) return;
                try
                {
                    File.AppendAllText(Path.Combine(_dir, "error-" + DateTime.Now.ToString("yyyyMMdd") + ".log"),
                        line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            }
        }
    }
}

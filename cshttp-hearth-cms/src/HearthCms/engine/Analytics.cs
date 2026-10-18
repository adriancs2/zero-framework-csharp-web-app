using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Web;
using CsHttp.Web;

namespace System.engine
{
    // ============================================================
    // Internal Content Analytics - dedicated SQLite store + background
    // batch pipeline. Tracks ONLY public page views (never admin, never
    // login). No IP addresses are ever recorded.
    //
    // Storage: a SEPARATE SQLite file (App_Data/hearth_analytics.sqlite),
    // never the main CMS database. WAL mode is enabled on this connection.
    //
    // Pipeline (producer/consumer):
    //   - Public request threads (AnalyticsApi) never touch SQLite directly.
    //     They only enqueue a lightweight command into a BlockingCollection.
    //   - A single background thread wakes up every 5 seconds, drains
    //     whatever is queued, and per tick:
    //       1) READ  - one batched SELECT of last-heartbeat for every token
    //          referenced by a heartbeat command in this batch.
    //       2) DECIDE - in memory: heartbeat commands whose token is stale
    //          (now - last_heartbeat > 30 min) or unknown are dropped.
    //          Insert commands always proceed.
    //       3) WRITE - a single transaction with all surviving inserts and
    //          "+60s" duration updates for this tick.
    //   - This background thread is the ONLY writer to the analytics file
    //     (single-writer discipline). Safe to lose all in-flight state on
    //     an app-pool recycle - nothing here is required to survive it.
    // ============================================================
    public static class Analytics
    {
        public const string DbFileName = "hearth_analytics.sqlite";
        const int BatchTickMs = 5000;
        const int StaleMinutes = 30;

        // The READ phase looks up last-heartbeat for every distinct token via a
        // single "WHERE token IN (@t0..@tN)" query, one bound parameter per token.
        // SQLite caps bound parameters per statement (SQLITE_MAX_VARIABLE_NUMBER,
        // historically 999), so a batch with thousands of concurrent heartbeats
        // would blow the limit and throw the whole tick away. We chunk the IN(...)
        // lookup into slices of this size to stay safely under that cap regardless
        // of how many heartbeats land in one 5s window.
        const int ReadChunkSize = 500;

        // ----- Command objects enqueued by the request threads -----
        public class StartVisitCommand
        {
            public string Token;
            public string Path;
            public DateTime NowUtc;
            public DateTime? PublishDateUtc;
        }

        public class HeartbeatCommand
        {
            public string Token;
            public DateTime NowUtc;
        }

        static readonly BlockingCollection<object> Queue = new BlockingCollection<object>();

        static Thread _worker;
        static readonly object StartLock = new object();
        static volatile bool _started;

        // ----- Public entry points (called from request threads) -----

        public static void EnqueueStartVisit(string token, string path, DateTime? publishDateUtc)
        {
            EnsureWorkerStarted();
            Queue.Add(new StartVisitCommand
            {
                Token = token,
                Path = path,
                NowUtc = DateTime.UtcNow,
                PublishDateUtc = publishDateUtc
            });
        }

        public static void EnqueueHeartbeat(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            EnsureWorkerStarted();
            Queue.Add(new HeartbeatCommand { Token = token, NowUtc = DateTime.UtcNow });
        }

        // Starts the single background consumer thread on first use. Cheap to
        // call repeatedly - only the first caller actually spins it up.
        static void EnsureWorkerStarted()
        {
            if (_started) return;
            lock (StartLock)
            {
                if (_started) return;
                EnsureSchema();
                _worker = new Thread(WorkerLoop);
                _worker.IsBackground = true;
                _worker.Name = "AnalyticsBatchWorker";
                _worker.Start();
                _started = true;
            }
        }

        static void WorkerLoop()
        {
            while (true)
            {
                try
                {
                    ProcessOneTick();
                }
                catch
                {
                    // Never let a bad tick kill the background thread - the next
                    // tick tries again. There is no client waiting on this thread.
                }
                Thread.Sleep(BatchTickMs);
            }
        }

        // Graceful-shutdown hook (cshttp build): write whatever is still queued
        // so a service stop / redeploy does not drop the last few seconds of
        // visits. Under IIS this batch was simply lost on app-pool recycle.
        public static void FlushOnShutdown()
        {
            if (!_started) return;
            try { ProcessOneTick(); } catch { }
        }

        // Drains whatever is currently queued and runs the READ / DECIDE / WRITE
        // phases described above. A no-op tick (nothing queued) does nothing.
        static void ProcessOneTick()
        {
            var inserts = new List<StartVisitCommand>();
            var heartbeats = new List<HeartbeatCommand>();

            object item;
            while (Queue.TryTake(out item))
            {
                var sv = item as StartVisitCommand;
                if (sv != null) { inserts.Add(sv); continue; }
                var hb = item as HeartbeatCommand;
                if (hb != null) { heartbeats.Add(hb); continue; }
            }

            if (inserts.Count == 0 && heartbeats.Count == 0) return;

            using (var conn = new SQLiteConnection(GetConnString()))
            {
                conn.Open();

                // ----- READ: last-heartbeat time for every distinct heartbeat token -----
                var lastSeen = new Dictionary<string, DateTime>();
                if (heartbeats.Count > 0)
                {
                    var distinctTokens = new List<string>();
                    var seen = new Dictionary<string, bool>();
                    foreach (var hb in heartbeats)
                    {
                        if (string.IsNullOrEmpty(hb.Token)) continue;
                        if (seen.ContainsKey(hb.Token)) continue;
                        seen[hb.Token] = true;
                        distinctTokens.Add(hb.Token);
                    }

                    // Look tokens up in chunks so the IN(...) list never exceeds the
                    // SQLite bound-parameter cap, however many heartbeats are batched.
                    for (int start = 0; start < distinctTokens.Count; start += ReadChunkSize)
                    {
                        int len = distinctTokens.Count - start;
                        if (len > ReadChunkSize) len = ReadChunkSize;

                        using (var cmd = conn.CreateCommand())
                        {
                            var s = new SQLiteExpress(cmd);
                            var ph = new List<string>();
                            var prm = new Dictionary<string, object>();
                            for (int i = 0; i < len; i++)
                            {
                                string pname = "@t" + i;
                                ph.Add(pname);
                                prm[pname] = distinctTokens[start + i];
                            }
                            string sql = "SELECT token, last_heartbeat_utc FROM Visits WHERE token IN (" +
                                string.Join(",", ph) + ");";
                            var dt = s.Select(sql, prm);
                            foreach (System.Data.DataRow row in dt.Rows)
                            {
                                string tok = row["token"] + "";
                                DateTime lh;
                                if (DateTime.TryParse(row["last_heartbeat_utc"] + "",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                                    out lh))
                                {
                                    lastSeen[tok] = lh;
                                }
                            }
                        }
                    }
                }

                // ----- DECIDE: drop stale/unknown heartbeats -----
                var survivingHeartbeats = new List<HeartbeatCommand>();
                foreach (var hb in heartbeats)
                {
                    if (string.IsNullOrEmpty(hb.Token)) continue;
                    DateTime lastHb;
                    if (!lastSeen.TryGetValue(hb.Token, out lastHb)) continue; // unknown token: ignore
                    if ((hb.NowUtc - lastHb).TotalMinutes > StaleMinutes) continue; // stale: ignore
                    survivingHeartbeats.Add(hb);
                }

                // ----- WRITE: one transaction for all surviving inserts + updates -----
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    s.BeginTransaction();
                    try
                    {
                        foreach (var sv in inserts)
                        {
                            var dic = new Dictionary<string, object>();
                            dic["token"] = sv.Token;
                            dic["path"] = sv.Path;
                            dic["entry_utc"] = sv.NowUtc.ToString("yyyy-MM-dd HH:mm:ss");
                            dic["last_heartbeat_utc"] = sv.NowUtc.ToString("yyyy-MM-dd HH:mm:ss");
                            dic["duration_seconds"] = 0;
                            dic["publish_date_utc"] = sv.PublishDateUtc.HasValue
                                ? (object)sv.PublishDateUtc.Value.ToString("yyyy-MM-dd HH:mm:ss")
                                : null;
                            s.InsertOrReplace("Visits", dic);
                        }

                        foreach (var hb in survivingHeartbeats)
                        {
                            cmd.CommandText =
                                "UPDATE Visits SET duration_seconds = duration_seconds + 60, last_heartbeat_utc = @now WHERE token = @token;";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@now", hb.NowUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                            cmd.Parameters.AddWithValue("@token", hb.Token);
                            cmd.ExecuteNonQuery();
                        }

                        s.Commit();
                    }
                    catch
                    {
                        try { s.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        // ----- Storage location / schema -----

        // Absolute path to the App_Data folder, resolved ONCE at Application_Start
        // (see Global.asax.cs -> Analytics.ConfigureDataDir) and held statically.
        //
        // This is deliberate: the background worker thread has no HttpContext.Current
        // and Server.MapPath is a request-thread facility that is unreliable off it.
        // Relying on HttpContext.Current.Server.MapPath from the worker threw a
        // NullReferenceException on every tick, which WorkerLoop's catch swallowed -
        // so the file was created (schema runs on the request thread) but no row was
        // ever written. Resolving the path at startup removes that dependency.
        static string _dataDir;

        // Called once from Application_Start with the resolved absolute App_Data path.
        public static void ConfigureDataDir(string appDataDir)
        {
            if (!string.IsNullOrEmpty(appDataDir)) _dataDir = appDataDir;
        }

        public static string GetDbPath()
        {
            string dataDir = _dataDir;

            // Fallbacks only if ConfigureDataDir was not called (e.g. a code path
            // that reaches analytics before Application_Start finished). These run
            // on request threads where the facilities are available.
            if (string.IsNullOrEmpty(dataDir))
            {
                var ctx = HttpContext.Current;
                if (ctx != null) dataDir = ctx.Server.MapPath("~/App_Data/");
                if (string.IsNullOrEmpty(dataDir))
                    dataDir = CsHttp.Web.HostingEnvironment.MapPath("~/App_Data/");
                _dataDir = dataDir;
            }

            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            return Path.Combine(dataDir, DbFileName);
        }

        public static string GetConnString()
        {
            return "Data Source=" + GetDbPath() + ";Version=3;Pooling=True;Max Pool Size=100;";
        }

        static bool _schemaEnsured;
        static readonly object SchemaLock = new object();

        public static void EnsureSchema()
        {
            if (_schemaEnsured) return;
            lock (SchemaLock)
            {
                if (_schemaEnsured) return;
                using (var conn = new SQLiteConnection(GetConnString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA journal_mode=WAL;";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Visits (
    token TEXT PRIMARY KEY,
    path TEXT NOT NULL,
    entry_utc TEXT NOT NULL,
    last_heartbeat_utc TEXT NOT NULL,
    duration_seconds INTEGER NOT NULL DEFAULT 0,
    publish_date_utc TEXT
);";
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_visits_path ON Visits(path);";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_visits_entry ON Visits(entry_utc);";
                        cmd.ExecuteNonQuery();
                    }
                }
                _schemaEnsured = true;
            }
        }

        // Deletes every row from Visits (the admin "Clear Analytics" action).
        // Runs synchronously on the calling (admin) request thread - this is an
        // explicit, rare admin action, not part of the hot public-page path, so
        // it does not need to go through the queue/single-writer pipeline.
        public static void ClearAll()
        {
            EnsureSchema();
            using (var conn = new SQLiteConnection(GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM Visits;";
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}

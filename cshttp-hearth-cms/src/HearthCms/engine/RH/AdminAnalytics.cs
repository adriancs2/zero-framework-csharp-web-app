using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // Internal Content Analytics - admin report pages.
    //   /admin/analytics        -> HandleListRequest()   (Page 1: most viewed)
    //   /admin/analytics/detail -> HandleDetailRequest() (Page 2: reading time)
    //
    // Both pages read the dedicated analytics SQLite file directly (reads
    // only - the single background writer in engine/Analytics.cs stays the
    // only writer). Aggregation is a live GROUP BY over the raw Visits
    // table; there is no rollup table by design.
    // ============================================================
    public static class AdminAnalytics
    {
        // ----- Page 1: Most Viewed Pages -----
        public static void HandleListRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            var req = HttpContext.Current.Request;

            // Date range filter, default "past 3 months to today".
            DateTime toDate = ParseDate(req.QueryString["to"], DateTime.UtcNow.Date);
            DateTime fromDate = ParseDate(req.QueryString["from"], DateTime.UtcNow.Date.AddMonths(-3));
            if (fromDate > toDate) fromDate = toDate;

            // Partial "contains" match against the page path.
            string q = (req.QueryString["q"] + "").Trim();

            // Sort: "views" (default) or "published".
            string sort = (req.QueryString["sort"] + "").Trim().ToLowerInvariant();
            if (sort != "published") sort = "views";

            // Pagination: 50 rows per page, navigated with Previous/Next.
            const int PageSize = 50;
            int page = 1;
            int.TryParse((req.QueryString["page"] + "").Trim(), out page);
            if (page < 1) page = 1;

            bool enabled = Settings.AnalyticsEnabled;

            // ----- Aggregate query (one page of results + a total for the pager) -----
            var rows = new List<PathAgg>();
            int totalRows = 0;
            Analytics.EnsureSchema();
            using (var conn = new SQLiteConnection(Analytics.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var prm = new Dictionary<string, object>();
                    prm["@from"] = fromDate.ToString("yyyy-MM-dd") + " 00:00:00";
                    prm["@to"] = toDate.ToString("yyyy-MM-dd") + " 23:59:59";
                    string where = "entry_utc >= @from AND entry_utc <= @to";
                    if (q.Length > 0)
                    {
                        where += " AND path LIKE @q";
                        prm["@q"] = "%" + q + "%";
                    }
                    string orderBy = sort == "published"
                        ? "pub DESC, cnt DESC"
                        : "cnt DESC, path ASC";

                    // Total number of distinct paths matching the filter, so we can
                    // show "Page X of Y" and enable/disable the Next button.
                    object totalObj = s.ExecuteScalar(
                        "SELECT COUNT(*) FROM (SELECT path FROM Visits WHERE " + where + " GROUP BY path);", prm);
                    int.TryParse(totalObj + "", out totalRows);

                    // Clamp the requested page to the available range before paging.
                    int pageCount = (totalRows + PageSize - 1) / PageSize;
                    if (pageCount < 1) pageCount = 1;
                    if (page > pageCount) page = pageCount;

                    prm["@limit"] = PageSize;
                    prm["@offset"] = (page - 1) * PageSize;
                    var dt = s.Select(
                        "SELECT path, COUNT(*) AS cnt, MAX(publish_date_utc) AS pub " +
                        "FROM Visits WHERE " + where + " GROUP BY path ORDER BY " + orderBy +
                        " LIMIT @limit OFFSET @offset;", prm);
                    foreach (Data.DataRow r in dt.Rows)
                    {
                        var agg = new PathAgg();
                        agg.Path = r["path"] + "";
                        int cnt;
                        int.TryParse(r["cnt"] + "", out cnt);
                        agg.Views = cnt;
                        DateTime pub;
                        if (DateTime.TryParse(r["pub"] + "", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out pub))
                            agg.PublishDate = pub;
                        rows.Add(agg);
                    }
                }
            }

            var tpl = new AdminTemplate
            {
                Title = "Analytics",
                ActiveItem = "analytics",
                PageHeading = "Analytics — Most Viewed Pages"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            // ----- Enable/disable + clear controls -----
            sb.Append($@"
<div class='card'>
    <div class='card-header'><h2><i class='fa-solid fa-chart-line'></i> Internal Content Analytics</h2></div>
    <div class='card-body'>
        <label class='switch'>
            <input type='checkbox' id='analyticsEnabled' {(enabled ? "checked" : "")} onchange='toggleAnalytics(this)' />
            <span>Enable analytics</span>
        </label>
        <p class='form-hint' style='margin-top:10px'>
            Tracks page views and reading time on <strong>public pages only</strong> (never admin, never login).
            No IP addresses are ever recorded. Default is off.
        </p>
        <div style='margin-top:10px'>
            <button type='button' class='btn btn-ghost btn-sm' onclick='clearAnalytics()'><i class='fa-solid fa-trash-can'></i> Clear analytics data</button>
        </div>
    </div>
</div>
");

            // ----- Filter bar: date range + path contains + sort -----
            string fromVal = fromDate.ToString("yyyy-MM-dd");
            string toVal = toDate.ToString("yyyy-MM-dd");
            sb.Append($@"
<div class='card'>
    <div class='card-body'>
        <form method='get' action='/admin/analytics' class='analytics-filter' style='display:flex;flex-wrap:wrap;gap:10px;align-items:flex-end'>
            <div class='form-field'>
                <label class='form-label' for='from'>From</label>
                <input type='date' id='from' name='from' class='form-control' value='{fromVal}' />
            </div>
            <div class='form-field'>
                <label class='form-label' for='to'>To</label>
                <input type='date' id='to' name='to' class='form-control' value='{toVal}' />
            </div>
            <div class='form-field'>
                <label class='form-label' for='q'>Path contains</label>
                <input type='text' id='q' name='q' class='form-control' value='{HttpUtility.HtmlAttributeEncode(q)}' placeholder='e.g. my-post' />
            </div>
            <div class='form-field'>
                <label class='form-label' for='sort'>Sort by</label>
                <select id='sort' name='sort' class='form-control'>
                    <option value='views' {(sort == "views" ? "selected" : "")}>Most Views</option>
                    <option value='published' {(sort == "published" ? "selected" : "")}>Publish Date</option>
                </select>
            </div>
            <div class='form-field'>
                <button type='submit' class='btn btn-primary btn-sm'><i class='fa-solid fa-filter'></i> Apply</button>
            </div>
        </form>
    </div>
</div>
");

            // ----- Results -----
            if (rows.Count == 0)
            {
                sb.Append(@"
<div class='empty-card empty-card-sm'>
    <i class='fa-solid fa-chart-line empty-icon'></i>
    <h2>No visits recorded</h2>
    <p>No page views match the current filter." + (enabled ? "" : " Analytics is currently disabled — enable it above to start collecting data.") + @"</p>
</div>
");
            }
            else
            {
                string carry = "from=" + fromVal + "&to=" + toVal +
                    (q.Length > 0 ? "&q=" + HttpUtility.UrlEncode(q) : "") +
                    "&sort=" + sort;

                sb.Append(@"
<div class='data-table-wrap'>
<table class='data-table'>
    <thead>
        <tr>
            <th>Page path</th>
            <th class='col-narrow'>Views</th>
            <th class='col-narrow' style='width:160px;white-space:nowrap'>Published</th>
            <th class='col-actions'></th>
        </tr>
    </thead>
    <tbody>
");
                foreach (var r in rows)
                {
                    string detailUrl = "/admin/analytics/detail?path=" + HttpUtility.UrlEncode(r.Path) + "&" + carry;
                    string pubTxt = r.PublishDate.HasValue ? DateDisplay.Format(r.PublishDate.Value) : "-";
                    sb.Append($@"
        <tr>
            <td><a href='{detailUrl}' class='row-title'><code>{HttpUtility.HtmlEncode(r.Path)}</code></a></td>
            <td>{r.Views}</td>
            <td class='text-muted' style='white-space:nowrap'>{pubTxt}</td>
            <td class='col-actions'>
                <a href='{detailUrl}' class='icon-btn' title='Reading time detail'><i class='fa-solid fa-stopwatch'></i></a>
            </td>
        </tr>");
                }
                sb.Append(@"
    </tbody>
</table>
</div>
");

                // ----- Pager: Previous / Next over 50-row pages -----
                int totalPages = (totalRows + PageSize - 1) / PageSize;
                if (totalPages < 1) totalPages = 1;
                sb.Append("<div class='pagination' style='display:flex;gap:10px;align-items:center;justify-content:flex-end;margin-top:14px'>");
                if (page > 1)
                    sb.Append($"<a class='btn btn-ghost btn-sm' href='/admin/analytics?{carry}&page={page - 1}'><i class='fa-solid fa-arrow-left'></i> Previous</a>");
                else
                    sb.Append("<span class='btn btn-ghost btn-sm' style='opacity:.4;pointer-events:none'><i class='fa-solid fa-arrow-left'></i> Previous</span>");
                sb.Append($"<span class='text-muted'>Page {page} of {totalPages}</span>");
                if (page < totalPages)
                    sb.Append($"<a class='btn btn-ghost btn-sm' href='/admin/analytics?{carry}&page={page + 1}'>Next <i class='fa-solid fa-arrow-right'></i></a>");
                else
                    sb.Append("<span class='btn btn-ghost btn-sm' style='opacity:.4;pointer-events:none'>Next <i class='fa-solid fa-arrow-right'></i></span>");
                sb.Append("</div>");
            }

            // Toggle + clear wiring (plain XHR, matching the /js/ style).
            sb.Append(@"
<script>
function analyticsApi(body, done) {
    var xhr = new XMLHttpRequest();
    xhr.open('POST', '/api/admin/analytics', true);
    xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded');
    xhr.onreadystatechange = function () {
        if (xhr.readyState === 4) done(xhr);
    };
    xhr.send(body);
}
function toggleAnalytics(cb) {
    analyticsApi('action=toggle&enabled=' + (cb.checked ? '1' : '0'), function (xhr) {
        if (xhr.status !== 200) { alert('Could not save the setting.'); cb.checked = !cb.checked; }
    });
}
function clearAnalytics() {
    if (!confirm('Delete ALL collected analytics data? This cannot be undone.')) return;
    analyticsApi('action=clear', function (xhr) {
        if (xhr.status === 200) { window.location.reload(); }
        else { alert('Could not clear analytics data.'); }
    });
}
</script>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        // ----- Page 2: Page Reading Time Detail -----
        public static void HandleDetailRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            var req = HttpContext.Current.Request;
            string path = (req.QueryString["path"] + "").Trim();
            if (path.Length == 0)
            {
                ApiHelper.Redirect("/admin/analytics");
                return;
            }

            // Date range carried over from Page 1 (same defaults as fallback).
            DateTime toDate = ParseDate(req.QueryString["to"], DateTime.UtcNow.Date);
            DateTime fromDate = ParseDate(req.QueryString["from"], DateTime.UtcNow.Date.AddMonths(-3));
            if (fromDate > toDate) fromDate = toDate;
            string fromVal = fromDate.ToString("yyyy-MM-dd");
            string toVal = toDate.ToString("yyyy-MM-dd");

            // Bucket boundaries in seconds; the last bucket is "more than 30 min".
            int[] limits = new int[] { 60, 300, 600, 900, 1200, 1800 };
            string[] labels = new string[] {
                "Less than 1 minute", "Less than 5 minutes", "Less than 10 minutes",
                "Less than 15 minutes", "Less than 20 minutes", "Less than 30 minutes",
                "More than 30 minutes"
            };
            int[] counts = new int[7];
            int totalVisits = 0;

            Analytics.EnsureSchema();
            using (var conn = new SQLiteConnection(Analytics.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    var prm = new Dictionary<string, object>();
                    prm["@p"] = path;
                    prm["@from"] = fromVal + " 00:00:00";
                    prm["@to"] = toVal + " 23:59:59";
                    var dt = s.Select(
                        "SELECT duration_seconds FROM Visits WHERE path = @p AND entry_utc >= @from AND entry_utc <= @to;", prm);
                    foreach (Data.DataRow r in dt.Rows)
                    {
                        int secs;
                        int.TryParse(r["duration_seconds"] + "", out secs);
                        int bucket = limits.Length;   // default: "more than 30 minutes"
                        for (int i = 0; i < limits.Length; i++)
                        {
                            if (secs < limits[i]) { bucket = i; break; }
                        }
                        counts[bucket]++;
                        totalVisits++;
                    }
                }
            }

            var tpl = new AdminTemplate
            {
                Title = "Reading Time",
                ActiveItem = "analytics",
                PageHeading = "Page Reading Time Detail",
                PageHeadingActionsHtml =
                    $"<a href='/admin/analytics?from={fromVal}&to={toVal}' class='btn btn-ghost btn-sm'><i class='fa-solid fa-arrow-left'></i> Back to Most Viewed</a>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            sb.Append($@"
<div class='card'>
    <div class='card-header'><h2><i class='fa-solid fa-stopwatch'></i> <code>{HttpUtility.HtmlEncode(path)}</code></h2></div>
    <div class='card-body'>
        <p class='form-hint'>
            Active reading time for <strong>{totalVisits}</strong> visit{(totalVisits == 1 ? "" : "s")}
            between <strong>{fromVal}</strong> and <strong>{toVal}</strong> (UTC).
            Time only accrues while the tab is visible, in one-minute heartbeats.
        </p>
    </div>
</div>

<div class='card'>
    <div class='card-body'>
        <form method='get' action='/admin/analytics/detail' class='analytics-filter' style='display:flex;flex-wrap:wrap;gap:10px;align-items:flex-end'>
            <input type='hidden' name='path' value='{HttpUtility.HtmlAttributeEncode(path)}' />
            <div class='form-field'>
                <label class='form-label' for='from'>From</label>
                <input type='date' id='from' name='from' class='form-control' value='{fromVal}' />
            </div>
            <div class='form-field'>
                <label class='form-label' for='to'>To</label>
                <input type='date' id='to' name='to' class='form-control' value='{toVal}' />
            </div>
            <div class='form-field'>
                <button type='submit' class='btn btn-primary btn-sm'><i class='fa-solid fa-filter'></i> Apply</button>
            </div>
        </form>
    </div>
</div>

<div class='data-table-wrap'>
<table class='data-table'>
    <thead>
        <tr>
            <th>Reading time</th>
            <th class='col-narrow'>Visits</th>
        </tr>
    </thead>
    <tbody>
");
            for (int i = 0; i < labels.Length; i++)
            {
                sb.Append($@"
        <tr>
            <td>{labels[i]}</td>
            <td>{counts[i]}</td>
        </tr>");
            }
            sb.Append(@"
    </tbody>
</table>
</div>
");

            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        // yyyy-MM-dd from the querystring; anything unparsable -> fallback.
        static DateTime ParseDate(string raw, DateTime fallback)
        {
            DateTime d;
            if (DateTime.TryParseExact((raw + "").Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
            return fallback;
        }

        class PathAgg
        {
            public string Path;
            public int Views;
            public DateTime? PublishDate;
        }
    }
}

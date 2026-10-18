// ============================================================
// Internal Content Analytics - public page tracking client
// Emitted only when analytics is enabled (see Hearth Layout.cs).
// Protocol:
//   - on load: POST /api/analytics/start {path} -> {token}
//   - every 60s while the tab is VISIBLE: POST /api/analytics/heartbeat {token}
//   - tab hidden: pause the timer. Visible again: if hidden > 30 min,
//     discard the token and start a brand new visit; otherwise resume.
//   - on unload: best-effort sendBeacon heartbeat (fire and forget).
// No IPs, no cookies, no cross-page session linkage - one row per view.
// ============================================================

var analyticsToken = null;
var analyticsTimer = null;
var analyticsHiddenAt = null;
var ANALYTICS_HEARTBEAT_MS = 60000;
var ANALYTICS_AWAY_LIMIT_MS = 30 * 60 * 1000;

function analyticsPost(url, body, done) {
    var xhr = new XMLHttpRequest();
    xhr.open('POST', url, true);
    xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded');
    xhr.onreadystatechange = function () {
        if (xhr.readyState === 4 && done) {
            var data = null;
            try { data = JSON.parse(xhr.responseText); } catch (e) { }
            done(data);
        }
    };
    try { xhr.send(body); } catch (e) { }
}

function analyticsStartVisit() {
    analyticsPost('/api/analytics/start',
        'path=' + encodeURIComponent(window.location.pathname),
        function (data) {
            if (data && data.data && data.data.token) {
                analyticsToken = data.data.token;
            } else if (data && data.token) {
                analyticsToken = data.token;
            }
        });
}

function analyticsHeartbeat() {
    if (!analyticsToken) return;
    analyticsPost('/api/analytics/heartbeat', 'token=' + encodeURIComponent(analyticsToken), null);
}

function analyticsStartTimer() {
    if (analyticsTimer !== null) return;
    analyticsTimer = window.setInterval(analyticsHeartbeat, ANALYTICS_HEARTBEAT_MS);
}

function analyticsStopTimer() {
    if (analyticsTimer === null) return;
    window.clearInterval(analyticsTimer);
    analyticsTimer = null;
}

document.addEventListener('visibilitychange', function () {
    if (document.hidden) {
        analyticsHiddenAt = new Date().getTime();
        analyticsStopTimer();
    } else {
        var awayMs = analyticsHiddenAt === null ? 0 : (new Date().getTime() - analyticsHiddenAt);
        analyticsHiddenAt = null;
        if (awayMs > ANALYTICS_AWAY_LIMIT_MS) {
            // Away too long: the old token is stale server-side. New visit.
            analyticsToken = null;
            analyticsStartVisit();
        }
        analyticsStartTimer();
    }
});

window.addEventListener('pagehide', function () {
    // Best-effort parting heartbeat - no guarantee it lands.
    if (!analyticsToken || !navigator.sendBeacon) return;
    var fd = new FormData();
    fd.append('token', analyticsToken);
    try { navigator.sendBeacon('/api/analytics/heartbeat', fd); } catch (e) { }
});

analyticsStartVisit();
if (!document.hidden) analyticsStartTimer();

using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    // ============================================================
    // Shared UI fragments for the soft-delete ("trash") experience on
    // the Pages and Posts list screens: the Active/Deleted filter tabs,
    // the bulk-action toolbar, and the client-side script that wires up
    // select-all, per-row and bulk delete / undelete / permanent-delete.
    //
    // `entity` is the lowercase singular ("post" | "page"); it drives the
    // generated function names (deletePost / deletePage, etc.) and the
    // confirmation copy. `apiUrl` is the POST endpoint for that entity.
    // ============================================================
    internal static class AdminTrashUi
    {
        static string Cap(string entity)
        {
            if (string.IsNullOrEmpty(entity)) return entity;
            return char.ToUpperInvariant(entity[0]) + entity.Substring(1);
        }

        // Active / Deleted filter tabs with live counts.
        public static string FilterTabs(string baseUrl, bool showDeleted, int activeCount, int deletedCount)
        {
            string b = HttpUtility.HtmlAttributeEncode(baseUrl);
            string activeCls = showDeleted ? "filter-tab" : "filter-tab is-active";
            string deletedCls = showDeleted ? "filter-tab is-active" : "filter-tab";
            return $@"
<div class='filter-tabs'>
    <a href='{b}' class='{activeCls}'>Active <span class='filter-count'>{activeCount}</span></a>
    <a href='{b}?filter=deleted' class='{deletedCls}'>Deleted <span class='filter-count'>{deletedCount}</span></a>
</div>
";
        }

        // Bulk-action toolbar. Buttons differ between the active view (soft
        // delete) and the deleted view (undelete / permanent delete).
        public static string BulkBar(string entity, bool showDeleted)
        {
            string buttons = showDeleted
                ? @"<button type='button' class='btn btn-ghost btn-sm' onclick=""bulkAction('undelete')""><i class='fa-solid fa-rotate-left'></i> Undelete</button>
        <button type='button' class='btn btn-danger btn-sm' onclick=""bulkAction('purge')""><i class='fa-solid fa-trash-can'></i> Permanent delete</button>"
                : @"<button type='button' class='btn btn-danger btn-sm' onclick=""bulkAction('delete')""><i class='fa-solid fa-trash'></i> Delete</button>";

            return $@"
<div id='bulkBar' class='bulk-bar' style='display:none'>
    <span id='bulkCount' class='bulk-count'>0 selected</span>
    <div class='bulk-actions'>
        {buttons}
    </div>
</div>
";
        }

        public static string Script(string entity, string apiUrl)
        {
            string cap = Cap(entity);
            string api = HttpUtility.JavaScriptStringEncode(apiUrl);
            string ent = HttpUtility.JavaScriptStringEncode(entity);

            return $@"
<script>
function toggleAll(cb) {{
    document.querySelectorAll('.row-check').forEach(function(c) {{ c.checked = cb.checked; }});
    syncBulk();
}}
function selectedIds() {{
    return Array.prototype.map.call(document.querySelectorAll('.row-check:checked'), function(c) {{ return c.value; }});
}}
function syncBulk() {{
    var n = selectedIds().length;
    var bar = document.getElementById('bulkBar');
    if (bar) bar.style.display = n > 0 ? '' : 'none';
    var cnt = document.getElementById('bulkCount');
    if (cnt) cnt.textContent = n + ' selected';
    var all = document.getElementById('checkAll');
    var total = document.querySelectorAll('.row-check').length;
    if (all) all.checked = (n > 0 && n === total);
}}
async function postAction(action, ids, okMsg) {{
    var fd = new FormData();
    fd.append('action', action);
    fd.append('ids', ids.join(','));
    try {{
        var r = await fetch('{api}', {{ method: 'POST', body: fd }});
        var d = await r.json();
        if (d.success) flashGoodAndReload(okMsg, d.message || okMsg);
        else showErrorMessage('Action failed', d.message);
    }} catch (ex) {{ showErrorMessage('Network error', 'Please try again.'); }}
}}
function delete{cap}(id) {{
    if (!confirm('Move this {ent} to the trash?')) return;
    postAction('delete', [id], 'Deleted');
}}
function undelete{cap}(id) {{
    postAction('undelete', [id], 'Restored');
}}
function purge{cap}(id) {{
    if (!confirm('Permanently delete this {ent}? This cannot be undone.')) return;
    postAction('purge', [id], 'Deleted');
}}
function bulkAction(action) {{
    var ids = selectedIds();
    if (!ids.length) return;
    var msg = null;
    if (action === 'delete') msg = 'Move ' + ids.length + ' {ent}(s) to the trash?';
    else if (action === 'purge') msg = 'Permanently delete ' + ids.length + ' {ent}(s)? This cannot be undone.';
    if (msg && !confirm(msg)) return;
    var ok = action === 'undelete' ? 'Restored' : 'Deleted';
    postAction(action, ids, ok);
}}
</script>
";
        }
    }
}

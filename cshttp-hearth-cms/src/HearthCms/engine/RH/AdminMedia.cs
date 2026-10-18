using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Web;
using CsHttp.Web;

namespace System.engine.RH
{
    public static class AdminMedia
    {
        public static void HandleRequest()
        {
            if (!AdminGuard.RequireLogin()) return;

            List<obMedia> lst = new List<obMedia>();
            using (var conn = new SQLiteConnection(Config.GetConnString()))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    var s = new SQLiteExpress(cmd);
                    lst = s.GetObjectList<obMedia>("SELECT * FROM media ORDER BY date_created DESC;");
                }
            }

            var tpl = new AdminTemplate
            {
                Title = "Media",
                ActiveItem = "media",
                PageHeading = "Media library",
                PageHeadingActionsHtml = "<button type='button' class='btn btn-primary btn-sm' onclick='document.getElementById(\"fileUpload\").click()'><i class='fa-solid fa-cloud-arrow-up'></i> Upload</button>"
            };

            var sb = new StringBuilder();
            sb.Append(tpl.RenderHeader());

            sb.Append(@"
<div class='media-uploader' id='dropZone'>
    <input type='file' id='fileUpload' accept='image/*' multiple style='display:none' onchange='uploadFiles(this.files)' />
    <div class='uploader-inner'>
        <i class='fa-solid fa-cloud-arrow-up uploader-icon'></i>
        <h3>Drop files to upload</h3>
        <p>or <button type='button' class='link-btn' onclick='document.getElementById(""fileUpload"").click()'>browse from your computer</button></p>
        <small class='form-hint'>Images up to 50&nbsp;MB</small>
    </div>
    <div id='uploadProgress' class='upload-progress' style='display:none'></div>
</div>
");

            if (lst.Count == 0)
            {
                sb.Append(@"
<div class='empty-card empty-card-sm'>
    <i class='fa-solid fa-images empty-icon'></i>
    <h2>No media yet</h2>
    <p>Upload images above to use them in posts and pages.</p>
</div>
");
            }
            else
            {
                sb.Append("<div class='media-grid'>");
                foreach (var m in lst)
                {
                    string size = FormatBytes(m.FileSize);
                    string thumb = HttpUtility.HtmlAttributeEncode(m.ThumbUrl);
                    sb.Append($@"
<div class='media-tile' data-url='{HttpUtility.HtmlAttributeEncode(m.Url)}'>
    <div class='media-thumb' style='background-image:url({thumb})' onclick='copyUrl(this.parentNode)'></div>
    <div class='media-info'>
        <div class='media-name' title='{HttpUtility.HtmlAttributeEncode(m.OriginalName)}'>{HttpUtility.HtmlEncode(m.OriginalName)}</div>
        <div class='media-meta'>{size}</div>
    </div>
    <div class='media-actions'>
        <button type='button' class='icon-btn' title='Copy URL' onclick='copyUrl(this.closest(""&quot;.media-tile&quot;""))'><i class='fa-solid fa-link'></i></button>
        <a class='icon-btn' title='Open' href='{HttpUtility.HtmlAttributeEncode(m.Url)}' target='_blank'><i class='fa-solid fa-arrow-up-right-from-square'></i></a>
        <button type='button' class='icon-btn icon-btn-danger' title='Delete' onclick='deleteMedia({m.Id})'><i class='fa-solid fa-trash'></i></button>
    </div>
</div>");
                }
                sb.Append("</div>");
            }

            sb.Append(@"
<script>
function copyUrl(tile) {
    var url = tile.getAttribute('data-url');
    var abs = location.origin + url;
    navigator.clipboard.writeText(abs).then(function(){
        toast('Copied: ' + abs);
    });
}
async function deleteMedia(id) {
    if (!confirm('Delete this file? This cannot be undone.')) return;
    var fd = new FormData();
    fd.append('action', 'delete');
    fd.append('id', id);
    var r = await fetch('/api/admin/media', { method: 'POST', body: fd });
    var d = await r.json();
    if (d.success) flashGoodAndReload('Deleted', 'File deleted.');
    else showErrorMessage('Delete failed', d.message);
}
function toast(msg) {
    var t = document.createElement('div');
    t.className = 'toast';
    t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(function(){ t.classList.add('toast-show'); }, 10);
    setTimeout(function(){ t.classList.remove('toast-show'); setTimeout(function(){t.remove();},250); }, 2200);
}
function uploadFiles(files) {
    if (!files || !files.length) return;
    var pBox = document.getElementById('uploadProgress');
    pBox.style.display = 'block';
    pBox.innerHTML = '';
    var done = 0; var total = files.length;
    Array.from(files).forEach(function(f, idx) {
        var bar = document.createElement('div');
        bar.className = 'upload-bar';
        bar.innerHTML = '<span class=\'upload-bar-label\'>' + f.name + '</span><span class=\'upload-bar-track\'><span class=\'upload-bar-fill\' id=\'pf' + idx + '\'></span></span><span class=\'upload-bar-pct\' id=\'pp' + idx + '\'>0%</span>';
        pBox.appendChild(bar);

        var fd = new FormData();
        fd.append('action', 'upload');
        fd.append('file', f);

        var xhr = new XMLHttpRequest();
        xhr.upload.onprogress = function(e) {
            if (e.lengthComputable) {
                var p = Math.round((e.loaded / e.total) * 100);
                document.getElementById('pf' + idx).style.width = p + '%';
                document.getElementById('pp' + idx).textContent = p + '%';
            }
        };
        xhr.onload = function() {
            done++;
            try {
                var d = JSON.parse(xhr.responseText);
                if (!d.success) { bar.classList.add('upload-bar-error'); }
            } catch (e) { bar.classList.add('upload-bar-error'); }
            if (done === total) setTimeout(function(){ location.reload(); }, 400);
        };
        xhr.onerror = function() { done++; bar.classList.add('upload-bar-error'); };
        xhr.open('POST', '/api/admin/media');
        xhr.send(fd);
    });
}
// Drag and drop
var dz = document.getElementById('dropZone');
['dragenter','dragover'].forEach(function(ev){ dz.addEventListener(ev, function(e){ e.preventDefault(); dz.classList.add('is-drag'); }); });
['dragleave','drop'].forEach(function(ev){ dz.addEventListener(ev, function(e){ e.preventDefault(); dz.classList.remove('is-drag'); }); });
dz.addEventListener('drop', function(e){ if (e.dataTransfer.files) uploadFiles(e.dataTransfer.files); });
</script>
");
            sb.Append(tpl.RenderFooter());
            ApiHelper.WriteHtml(sb.ToString());
            ApiHelper.EndResponse();
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return v.ToString(u == 0 ? "0" : "0.#") + " " + units[u];
        }

        // ============================================================
        // Picker tile - minimal markup expected by mediaBrowser.js.
        // Each tile MUST expose data-url; data-id / data-name optional.
        // No action buttons here - mediaBrowser injects its own affordances.
        // ============================================================
        public static string RenderPickerTilesHtml(System.Collections.Generic.IList<obMedia> items)
        {
            var sb = new StringBuilder();
            foreach (var m in items)
            {
                string url = HttpUtility.HtmlAttributeEncode(m.Url);
                string thumb = HttpUtility.HtmlAttributeEncode(m.ThumbUrl);
                string nameAttr = HttpUtility.HtmlAttributeEncode(m.OriginalName);
                string nameText = HttpUtility.HtmlEncode(m.OriginalName);
                string size = FormatBytes(m.FileSize);
                sb.Append($@"<div class='media-tile' data-id='{m.Id}' data-url='{url}' data-name='{nameAttr}'>
    <div class='media-thumb' style='background-image:url({thumb})'></div>
    <div class='media-info'>
        <div class='media-name' title='{nameAttr}'>{nameText}</div>
        <div class='media-meta'>{size}</div>
    </div>
</div>");
            }
            return sb.ToString();
        }
    }
}

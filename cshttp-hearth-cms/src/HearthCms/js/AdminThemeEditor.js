// Theme editor page script.
// The server emits `var THEME` and `var missingFiles` in an inline <script>
// immediately before this file is loaded.

var curArea = null, curPath = null, mode = 'file'; // file | preview | home

function openFile(el){
    if (mode !== 'file') showFileMode();
    var area = el.getAttribute('data-area');
    var path = el.getAttribute('data-path');
    document.querySelectorAll('.te-file').forEach(function(b){ b.classList.remove('is-active'); });
    el.classList.add('is-active');
    curArea = area; curPath = path;
    document.getElementById('curFile').textContent = (area === 'asset' ? 'assets/' : '') + path;
    loadFile();
}

async function loadFile(){
    if (curArea === null) return;
    var ta = document.getElementById('fileText');
    ta.value = 'Loading…'; ta.disabled = true;
    try {
        var qs = '?action=readfile&slug=' + encodeURIComponent(THEME)
               + '&area=' + encodeURIComponent(curArea) + '&path=' + encodeURIComponent(curPath);
        var r = await fetch('/api/admin/themes' + qs);
        var d = await r.json();
        if (d.success){ ta.value = d.data.content; }
        else { ta.value = 'This file is missing'; }
    } catch(ex){ ta.value = 'This file is missing'; }
    ta.disabled = false;
}

function refreshFile(){
    if (curArea === null){ showErrorMessage('No file','Pick a file first.'); return; }
    loadFile();
}

async function saveFile(){
    if (curArea === null){ showErrorMessage('No file','Pick a file first.'); return; }
    if (document.getElementById('fileText').value === 'This file is missing'){ showErrorMessage('Nothing to save','This file does not exist yet.'); return; }
    var fd = new FormData();
    fd.append('action','savefile');
    fd.append('slug', THEME);
    fd.append('area', curArea);
    fd.append('path', curPath);
    fd.append('content', document.getElementById('fileText').value);
    try {
        var r = await fetch('/api/admin/themes', { method:'POST', body: fd });
        var d = await r.json();
        if (d.success) showGoodMessage('Saved', 'File saved.');
        else showErrorMessage('Save failed', d.message);
    } catch(ex){ showErrorMessage('Network error','Please try again.'); }
}

// ---- view-mode switching ----
function showFileMode(){
    mode = 'file';
    removeFloatingSave();
    document.getElementById('previewFrame').style.display = 'none';
    document.getElementById('fileText').style.display = '';
    document.getElementById('editorBar').style.display = '';
    setPreviewBtn(false); setHomeBtn(false);
}
function showIframe(){
    document.getElementById('fileText').style.display = 'none';
    document.getElementById('editorBar').style.display = 'none';
    document.getElementById('previewFrame').style.display = 'block';
}
function setPreviewBtn(on){
    document.getElementById('previewBtn').innerHTML = on
        ? "<i class='fa-solid fa-pen'></i> Back to editor"
        : "<i class='fa-solid fa-eye'></i> Preview";
}
function setHomeBtn(on){
    document.getElementById('homeBtn').innerHTML = on
        ? "<i class='fa-solid fa-xmark'></i> Close home editor"
        : "<i class='fa-solid fa-pen-to-square'></i> Edit Home Content";
}

// ---- preview (read-only) ----
function togglePreview(){ mode === 'preview' ? showFileMode() : enterPreview(); }
function enterPreview(){
    mode = 'preview'; removeFloatingSave();
    var f = document.getElementById('previewFrame');
    f.onload = null;
    f.src = '/admin/themes/preview/' + encodeURIComponent(THEME) + '?t=' + Date.now();
    showIframe(); setPreviewBtn(true); setHomeBtn(false);
}

// ---- Edit Home Content: inline editing inside the live home preview ----
function editHome(){
    if (mode === 'home'){ showFileMode(); return; }
    mode = 'home'; setHomeBtn(true); setPreviewBtn(false);
    var f = document.getElementById('previewFrame');
    f.onload = function(){ if (mode === 'home') makeHomeEditable(f); };
    f.src = '/admin/themes/preview/' + encodeURIComponent(THEME) + '?t=' + Date.now();
    showIframe();
}
function makeHomeEditable(frame){
    var doc;
    try { doc = frame.contentDocument; } catch(e){ doc = null; }
    if (!doc || !doc.body) return;

    var total = doc.querySelectorAll('[data-edit],[data-edit-href],[data-edit-src],[data-edit-bg],[data-edit-icon]').length;
    if (!total){
        showErrorMessage('Nothing to edit yet', 'Mark regions in home.html with data-edit / data-edit-href / data-edit-src / data-edit-bg / data-edit-icon.');
    }

    var st = doc.createElement('style');
    st.textContent = ".home-ed{outline:2px dashed #6366f1;outline-offset:3px;border-radius:4px}.home-ed:hover{background:rgba(99,102,241,.10)}[contenteditable].home-ed{cursor:text}.home-ed-icon{outline-color:#0ea5e9;cursor:pointer}.home-ed-img,.home-ed-bg{outline-color:#16a34a;cursor:pointer}.home-ed-link{outline-color:#d946ef;cursor:pointer}";
    doc.head.appendChild(st);

    doc.querySelectorAll('[data-edit]').forEach(function(el){ el.setAttribute('contenteditable','true'); el.classList.add('home-ed','home-ed-text'); });
    doc.querySelectorAll('[data-edit-href]').forEach(function(el){ el.classList.add('home-ed','home-ed-link'); el.title='Click to edit link URL'; });
    doc.querySelectorAll('[data-edit-src]').forEach(function(el){ el.classList.add('home-ed','home-ed-img'); el.title='Click to change image'; });
    doc.querySelectorAll('[data-edit-bg]').forEach(function(el){ el.classList.add('home-ed','home-ed-bg'); el.title='Click to change background image'; });
    doc.querySelectorAll('[data-edit-icon]').forEach(function(el){ el.classList.add('home-ed','home-ed-icon'); el.title='Click to pick an icon'; });

    // One capturing router: deny navigation in edit mode + open the right editor.
    doc.addEventListener('click', function(e){
        var t = e.target;
        var icon = t.closest ? t.closest('[data-edit-icon]') : null;
        var img  = t.closest ? t.closest('[data-edit-src]')  : null;
        var bg   = t.closest ? t.closest('[data-edit-bg]')   : null;
        var txt  = t.closest ? t.closest('[data-edit]')      : null;
        var a    = t.closest ? t.closest('a')                : null;
        if (icon){ e.preventDefault(); e.stopPropagation(); editIcon(icon); return; }
        if (img){  e.preventDefault(); e.stopPropagation(); editImage(img,'src'); return; }
        if (bg){   e.preventDefault(); e.stopPropagation(); editImage(bg,'bg');  return; }
        if (txt){  if (a) e.preventDefault(); return; }
        if (a){    e.preventDefault(); if (a.hasAttribute('data-edit-href')) editLink(a); return; }
    }, true);
    // Block form submits (e.g. the search bar) while editing.
    doc.addEventListener('submit', function(e){ e.preventDefault(); }, true);

    var save = doc.createElement('button');
    save.id = '__homeSave'; save.type = 'button'; save.textContent = 'Save home content';
    save.style.cssText = 'position:fixed;right:20px;bottom:20px;z-index:2147483647;background:#4f46e5;color:#fff;border:0;border-radius:999px;padding:13px 22px;font:600 14px system-ui,sans-serif;box-shadow:0 8px 24px rgba(79,70,229,.45);cursor:pointer';
    save.addEventListener('click', function(){ saveHomeEdits(frame); });
    doc.body.appendChild(save);
}

// --- field editors (run in the parent; operate on iframe elements) ---
function editLink(a){
    inputModal('Link URL', a.getAttribute('href') || '', 'https://…  or  /path').then(function(url){
        if (url === null) return;
        a.setAttribute('href', url);
    });
}
function inputModal(title, current, placeholder){
    return new Promise(function(resolve){
        var back = document.createElement('div');
        back.style.cssText = 'position:fixed;inset:0;background:rgba(17,24,39,.5);z-index:2147483646;display:flex;align-items:center;justify-content:center;padding:20px';
        var box = document.createElement('div');
        box.style.cssText = 'background:#fff;border-radius:14px;max-width:460px;width:100%;box-shadow:0 24px 60px rgba(0,0,0,.3);font-family:system-ui,sans-serif;overflow:hidden';
        box.innerHTML =
            "<div style='padding:16px 18px;border-bottom:1px solid #eee;font-weight:700;color:#111'>" + title + "</div>" +
            "<div style='padding:16px 18px'><input id='__imq' style='width:100%;padding:10px 12px;border:1px solid #d1d5db;border-radius:9px;font-size:14px;box-sizing:border-box'></div>" +
            "<div style='padding:14px 18px;border-top:1px solid #eee;display:flex;justify-content:flex-end;gap:10px'><button id='__imcancel' type='button' style='padding:9px 16px;border:1px solid #d1d5db;background:#fff;border-radius:9px;cursor:pointer'>Cancel</button><button id='__imok' type='button' style='padding:9px 16px;border:0;background:#4f46e5;color:#fff;border-radius:9px;cursor:pointer;font-weight:600'>Save</button></div>";
        back.appendChild(box); document.body.appendChild(back);
        var inp = box.querySelector('#__imq');
        inp.value = current || ''; inp.placeholder = placeholder || '';
        function done(v){ if (back.parentNode) back.parentNode.removeChild(back); resolve(v); }
        box.querySelector('#__imcancel').addEventListener('click', function(){ done(null); });
        box.querySelector('#__imok').addEventListener('click', function(){ done(inp.value); });
        inp.addEventListener('keydown', function(e){ if (e.key === 'Enter'){ e.preventDefault(); done(inp.value); } else if (e.key === 'Escape'){ done(null); } });
        back.addEventListener('click', function(e){ if (e.target === back) done(null); });
        setTimeout(function(){ inp.focus(); inp.select(); }, 30);
    });
}
async function editImage(el, kind){
    if (typeof mediaBrowser === 'undefined'){ showErrorMessage('Media browser unavailable', 'media-browser.js did not load.'); return; }
    var picked;
    try { picked = await mediaBrowser.pick({ accept: ['image/*'] }); } catch(e){ picked = null; }
    if (!picked) return;
    if (Array.isArray(picked)) picked = picked[0];
    if (!picked) return;
    if (kind === 'src') el.setAttribute('src', picked);
    else el.style.backgroundImage = "url('" + picked + "')";
}
function editIcon(el){
    var current = cleanIconClass(el.getAttribute('class') || '');
    iconPicker(current).then(function(cls){
        if (!cls) return;
        el.setAttribute('class', cls + ' home-ed home-ed-icon');
    });
}
function cleanIconClass(c){
    return (c || '').replace(/\bhome-ed[\w-]*\b/g, '').replace(/\s+/g, ' ').trim();
}
function extractBgUrl(el){
    var s = (el.style && el.style.backgroundImage) || '';
    var m = s.match(/url\((['"]?)(.*?)\1\)/);
    return m ? m[2] : '';
}

// --- minimal Font Awesome icon picker (curated grid + free-text class) ---
var FA_ICONS = ['fa-fire','fa-fire-flame-simple','fa-feather-pointed','fa-bolt','fa-mug-hot','fa-star','fa-heart','fa-bookmark','fa-book','fa-pen-nib','fa-pen','fa-quote-left','fa-lightbulb','fa-rocket','fa-compass','fa-map','fa-mountain-sun','fa-leaf','fa-seedling','fa-tree','fa-sun','fa-moon','fa-cloud','fa-camera','fa-image','fa-music','fa-headphones','fa-microphone','fa-film','fa-palette','fa-wand-magic-sparkles','fa-gem','fa-crown','fa-trophy','fa-flag','fa-bell','fa-envelope','fa-paper-plane','fa-comment','fa-comments','fa-thumbs-up','fa-circle-check','fa-shield-halved','fa-lock','fa-key','fa-gear','fa-wrench','fa-code','fa-terminal','fa-database','fa-server','fa-globe','fa-link','fa-house','fa-user','fa-users','fa-cart-shopping','fa-tag','fa-gift','fa-clock','fa-calendar','fa-location-dot','fa-phone','fa-coffee','fa-utensils','fa-anchor','fa-plane','fa-car','fa-bicycle'];

function iconPicker(current){
    return new Promise(function(resolve){
        var back = document.createElement('div');
        back.style.cssText = 'position:fixed;inset:0;background:rgba(17,24,39,.5);z-index:2147483646;display:flex;align-items:center;justify-content:center;padding:20px';
        var box = document.createElement('div');
        box.style.cssText = 'background:#fff;border-radius:14px;max-width:560px;width:100%;max-height:80vh;display:flex;flex-direction:column;overflow:hidden;box-shadow:0 24px 60px rgba(0,0,0,.3);font-family:system-ui,sans-serif';
        var grid = FA_ICONS.map(function(n){ return "<button type='button' data-cls='fa-solid " + n + "' title='" + n + "' style='aspect-ratio:1;border:1px solid #e5e7eb;background:#fff;border-radius:10px;font-size:18px;cursor:pointer;color:#374151'><i class='fa-solid " + n + "'></i></button>"; }).join('');
        box.innerHTML =
            "<div style='padding:16px 18px;border-bottom:1px solid #eee;font-weight:700;color:#111'>Pick an icon</div>" +
            "<div style='padding:14px 18px'><input id='__icq' placeholder='Filter, or type a full class e.g. fa-solid fa-rocket' value='" + (current || '') + "' style='width:100%;padding:10px 12px;border:1px solid #d1d5db;border-radius:9px;font-size:14px;box-sizing:border-box'></div>" +
            "<div id='__icgrid' style='padding:0 18px 14px;display:grid;grid-template-columns:repeat(8,1fr);gap:8px;overflow:auto'>" + grid + "</div>" +
            "<div style='padding:14px 18px;border-top:1px solid #eee;display:flex;justify-content:flex-end;gap:10px'><button id='__iccancel' type='button' style='padding:9px 16px;border:1px solid #d1d5db;background:#fff;border-radius:9px;cursor:pointer'>Cancel</button><button id='__icok' type='button' style='padding:9px 16px;border:0;background:#4f46e5;color:#fff;border-radius:9px;cursor:pointer;font-weight:600'>Use icon</button></div>";
        back.appendChild(box); document.body.appendChild(back);
        var inp = box.querySelector('#__icq');
        function done(v){ if (back.parentNode) back.parentNode.removeChild(back); resolve(v); }
        box.querySelectorAll('#__icgrid button').forEach(function(b){
            b.addEventListener('click', function(){ inp.value = b.getAttribute('data-cls'); });
        });
        inp.addEventListener('input', function(){
            var q = inp.value.toLowerCase().replace('fa-solid','').replace('fa-regular','').trim();
            box.querySelectorAll('#__icgrid button').forEach(function(b){
                b.style.display = b.getAttribute('data-cls').toLowerCase().indexOf(q) >= 0 ? '' : 'none';
            });
        });
        box.querySelector('#__iccancel').addEventListener('click', function(){ done(null); });
        box.querySelector('#__icok').addEventListener('click', function(){ var v = inp.value.trim(); done(v || null); });
        back.addEventListener('click', function(e){ if (e.target === back) done(null); });
        setTimeout(function(){ inp.focus(); }, 30);
    });
}

async function saveHomeEdits(frame){
    var doc = frame.contentDocument;
    var values = {};
    doc.querySelectorAll('[data-edit]').forEach(function(el){ values[el.getAttribute('data-edit')] = (el.innerText || '').trim(); });
    doc.querySelectorAll('[data-edit-href]').forEach(function(el){ values[el.getAttribute('data-edit-href')] = el.getAttribute('href') || ''; });
    doc.querySelectorAll('[data-edit-src]').forEach(function(el){ values[el.getAttribute('data-edit-src')] = el.getAttribute('src') || ''; });
    doc.querySelectorAll('[data-edit-bg]').forEach(function(el){ values[el.getAttribute('data-edit-bg')] = extractBgUrl(el); });
    doc.querySelectorAll('[data-edit-icon]').forEach(function(el){ values[el.getAttribute('data-edit-icon')] = cleanIconClass(el.getAttribute('class') || ''); });
    var btn = doc.getElementById('__homeSave');
    if (btn){ btn.textContent = 'Saving…'; btn.disabled = true; }
    var fd = new FormData();
    fd.append('action','savehome'); fd.append('slug', THEME); fd.append('values', JSON.stringify(values));
    try {
        var r = await fetch('/api/admin/themes', { method:'POST', body: fd });
        var d = await r.json();
        if (d.success){
            showGoodMessage('Saved', 'Home content saved.');
            if (btn){ btn.textContent = 'Saved ✓'; setTimeout(function(){ if(btn){ btn.textContent='Save home content'; btn.disabled=false; } }, 1200); }
        } else {
            showErrorMessage('Save failed', d.message);
            if (btn){ btn.textContent='Save home content'; btn.disabled=false; }
        }
    } catch(ex){
        showErrorMessage('Network error','Please try again.');
        if (btn){ btn.textContent='Save home content'; btn.disabled=false; }
    }
}
function removeFloatingSave(){
    var f = document.getElementById('previewFrame');
    try { var b = f.contentDocument && f.contentDocument.getElementById('__homeSave'); if (b) b.remove(); } catch(e){}
}

// ---- theme actions ----
async function activate(){
    var fd = new FormData();
    fd.append('action','activate'); fd.append('slug', THEME);
    var r = await fetch('/api/admin/themes', { method:'POST', body: fd });
    var d = await r.json();
    if (d.success) flashGoodAndReload('Activated','Theme activated.');
    else showErrorMessage('Activate failed', d.message);
}
async function del(){
    if (!confirm('Delete this theme and its assets? This cannot be undone.')) return;
    var fd = new FormData();
    fd.append('action','delete'); fd.append('slug', THEME);
    var r = await fetch('/api/admin/themes', { method:'POST', body: fd });
    var d = await r.json();
    if (d.success) flashGoodAndGo('Deleted','Theme deleted.','/admin/themes');
    else showErrorMessage('Delete failed', d.message);
}

// ---- missing-file styling (missingFiles emitted by the server) ----
function renderMissingFiles(){
    (missingFiles || []).forEach(function(path){
        var el = document.querySelector(".te-file[data-path='" + path + "']");
        if (el) el.classList.add('is-file-missing');
    });
}

// ---- init ----
renderMissingFiles();
(function(){
    var first = document.querySelector('.te-file[data-path="_layout.html"]') || document.querySelector('.te-file');
    if (first) openFile(first);
})();

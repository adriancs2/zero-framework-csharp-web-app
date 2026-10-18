// ============================================================
// Modern CMS - MediaBrowser (portable component)
// ============================================================
//
// Usage:
//   const url  = await mediaBrowser.pick();                              // any file
//   const url  = await mediaBrowser.pick({ accept: ['image/*'] });       // images only
//   const urls = await mediaBrowser.pick({ multiSelect: true, accept: ['image/*'] });
//   await mediaBrowser.open();                                           // viewer only
//   const handle = mediaBrowser.mount('#div_media_container');           // inline
//   handle.unmount();
//
// Server contract (must implement on the backend):
//   GET  /api/admin/media?action=list
//        -> { success: true, html: "<div class='media-tile' data-url='...'>...</div>..." }
//   POST /api/admin/media   action=upload, file=<File>     -> { success, ... }
//   POST /api/admin/media   action=delete, id=<int>        -> { success, message? }
//
// Tile contract (each .media-tile must expose data-url; data-name and data-id optional).
// ============================================================

(function (global) {
    'use strict';

    var STATE = {
        isActive: false,
        root: null,        // outer overlay element (modal modes) or mount container (inline)
        mode: null,        // 'pick' | 'open' | 'mount'
        resolver: null,    // current pending promise resolver, or null
        options: null,
        selection: new Set() // selected URLs for multi-select
    };

    // ---------- styles (injected once) ----------
    var STYLES_INJECTED = false;
    function injectStyles() {
        if (STYLES_INJECTED) return;
        STYLES_INJECTED = true;
        var css = `
            .mb-overlay{position:fixed;inset:0;background:rgba(15,18,23,.72);z-index:9999;display:flex;align-items:center;justify-content:center;}
            .mb-modal{width:85vw;height:85vh;max-width:1400px;background:#fff;border-radius:10px;display:flex;flex-direction:column;overflow:hidden;box-shadow:0 20px 60px rgba(0,0,0,.4);position:relative;}
            .mb-inline{width:100%;height:100%;display:flex;flex-direction:column;background:#fff;}
            .mb-header{display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-bottom:1px solid #e5e7eb;flex-shrink:0;}
            .mb-title{font-weight:600;font-size:15px;color:#111827;}
            .mb-close{background:transparent;border:0;font-size:18px;color:#6b7280;cursor:pointer;width:34px;height:34px;border-radius:6px;display:flex;align-items:center;justify-content:center;}
            .mb-close:hover{background:#f3f4f6;color:#111827;}
            .mb-toolbar{display:flex;align-items:center;gap:10px;padding:10px 18px;border-bottom:1px solid #e5e7eb;background:#fafafa;flex-shrink:0;}
            .mb-toolbar .mb-btn{padding:6px 12px;border-radius:6px;border:1px solid #d1d5db;background:#fff;cursor:pointer;font-size:13px;display:inline-flex;align-items:center;gap:6px;}
            .mb-toolbar .mb-btn:hover{background:#f3f4f6;}
            .mb-toolbar .mb-btn-primary{background:#2563eb;color:#fff;border-color:#2563eb;}
            .mb-toolbar .mb-btn-primary:hover{background:#1d4ed8;}
            .mb-toolbar .mb-btn:disabled{opacity:.5;cursor:not-allowed;}
            .mb-body{flex:1;overflow-y:auto;padding:18px;}
            .mb-empty{padding:40px;text-align:center;color:#6b7280;}
            .mb-loading{padding:40px;text-align:center;color:#6b7280;}
            .mb-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:14px;}
            .mb-grid .media-tile{position:relative;border:1px solid #e5e7eb;border-radius:8px;overflow:hidden;background:#fff;cursor:default;transition:border-color .15s,box-shadow .15s;}
            .mb-grid .media-tile:hover{border-color:#2563eb;box-shadow:0 2px 8px rgba(37,99,235,.12);}
            .mb-grid .media-tile.is-selected{border-color:#2563eb;box-shadow:0 0 0 2px rgba(37,99,235,.25);}
            .mb-grid .media-thumb{width:100%;aspect-ratio:1;background-size:cover;background-position:center;background-color:#f3f4f6;}
            .mb-grid .media-info{padding:8px 10px;font-size:12px;}
            .mb-grid .media-name{font-weight:500;color:#111827;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
            .mb-grid .media-meta{color:#6b7280;font-size:11px;margin-top:2px;}
            .mb-grid .media-actions{position:absolute;top:6px;right:6px;display:flex;gap:4px;opacity:0;transition:opacity .15s;}
            .mb-grid .media-tile:hover .media-actions{opacity:1;}
            .mb-pick-btn{position:absolute;left:6px;bottom:6px;right:6px;padding:6px 10px;background:#2563eb;color:#fff;border:0;border-radius:6px;font-size:12px;font-weight:500;cursor:pointer;opacity:0;transition:opacity .15s;}
            .mb-grid .media-tile:hover .mb-pick-btn,.mb-grid .media-tile.is-selected .mb-pick-btn{opacity:1;}
            .mb-pick-btn:hover{background:#1d4ed8;}
            .mb-check{position:absolute;top:6px;left:6px;width:22px;height:22px;border-radius:5px;background:rgba(255,255,255,.92);border:1px solid #d1d5db;display:flex;align-items:center;justify-content:center;color:#fff;cursor:pointer;font-size:12px;}
            .mb-grid .media-tile.is-selected .mb-check{background:#2563eb;border-color:#2563eb;}
            .mb-footer{display:flex;align-items:center;justify-content:space-between;padding:12px 18px;border-top:1px solid #e5e7eb;background:#fafafa;flex-shrink:0;}
            .mb-footer-info{font-size:13px;color:#6b7280;}
            .mb-uploader{margin-bottom:14px;border:2px dashed #d1d5db;border-radius:8px;padding:18px;text-align:center;color:#6b7280;font-size:13px;background:#fafafa;}
            .mb-uploader.is-drag{border-color:#2563eb;background:#eff6ff;color:#2563eb;}
            .mb-uploader .mb-link{color:#2563eb;cursor:pointer;text-decoration:underline;background:none;border:0;font-size:inherit;padding:0;}
            .mb-upload-progress{margin-top:10px;}
            .mb-upload-bar{display:flex;align-items:center;gap:8px;font-size:12px;margin-top:6px;}
            .mb-upload-bar-label{flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
            .mb-upload-bar-track{flex:0 0 120px;height:6px;background:#e5e7eb;border-radius:3px;overflow:hidden;}
            .mb-upload-bar-fill{height:100%;background:#2563eb;width:0;transition:width .2s;}
            .mb-upload-bar-error .mb-upload-bar-fill{background:#dc2626;}
            .mb-upload-bar-pct{flex:0 0 36px;text-align:right;color:#6b7280;}
            `;
        var s = document.createElement('style');
        s.setAttribute('data-mb-styles', '1');
        s.textContent = css;
        document.head.appendChild(s);
    }

    // ---------- helpers ----------
    function acceptMatches(url, acceptList) {
        if (!acceptList || !acceptList.length) return true;
        var lower = String(url).toLowerCase().split('?')[0];
        var ext = lower.substring(lower.lastIndexOf('.') + 1);
        var IMG_EXT = ['jpg','jpeg','png','gif','webp','bmp','svg','avif','ico'];
        var VID_EXT = ['mp4','webm','mov','avi','mkv'];
        var AUD_EXT = ['mp3','wav','ogg','flac','m4a'];
        for (var i = 0; i < acceptList.length; i++) {
            var rule = String(acceptList[i]).toLowerCase();
            if (rule === '*/*' || rule === '*') return true;
            if (rule === 'image/*') { if (IMG_EXT.indexOf(ext) !== -1) return true; continue; }
            if (rule === 'video/*') { if (VID_EXT.indexOf(ext) !== -1) return true; continue; }
            if (rule === 'audio/*') { if (AUD_EXT.indexOf(ext) !== -1) return true; continue; }
            if (rule.charAt(0) === '.') { if (lower.endsWith(rule)) return true; continue; }
            var slash = rule.indexOf('/');
            if (slash !== -1) {
                var sub = rule.substring(slash + 1);
                if (sub !== '*' && ext === sub) return true;
            }
        }
        return false;
    }

    function el(tag, attrs, children) {
        var e = document.createElement(tag);
        if (attrs) {
            for (var k in attrs) {
                if (!Object.prototype.hasOwnProperty.call(attrs, k)) continue;
                if (k === 'class') e.className = attrs[k];
                else if (k === 'html') e.innerHTML = attrs[k];
                else if (k === 'text') e.textContent = attrs[k];
                else if (k.indexOf('on') === 0) e.addEventListener(k.substring(2), attrs[k]);
                else e.setAttribute(k, attrs[k]);
            }
        }
        if (children) {
            for (var i = 0; i < children.length; i++) {
                if (children[i] != null) e.appendChild(children[i]);
            }
        }
        return e;
    }

    function toast(msg) {
        if (typeof global.toast === 'function') { global.toast(msg); return; }
        var t = document.createElement('div');
        t.className = 'toast';
        t.textContent = msg;
        t.style.cssText = 'position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:#111827;color:#fff;padding:10px 16px;border-radius:6px;font-size:13px;z-index:10001;';
        document.body.appendChild(t);
        setTimeout(function () { t.remove(); }, 2400);
    }

    // ---------- rendering ----------
    function buildShell(mode) {
        var isModal = (mode === 'pick' || mode === 'open');
        var titleText = mode === 'pick' ? 'Select media' : 'Media library';

        var headerChildren = [el('div', { class: 'mb-title', text: titleText })];
        if (isModal) {
            headerChildren.push(el('button', {
                class: 'mb-close', type: 'button', 'aria-label': 'Close',
                html: '<i class="fa-solid fa-xmark"></i>',
                onclick: function () { closeActive(mode === 'pick' ? (STATE.options.multiSelect ? [] : null) : undefined); }
            }));
        }

        var header = el('div', { class: 'mb-header' }, headerChildren);

        var uploader = el('div', { class: 'mb-uploader', 'data-mb-dropzone': '1' }, [
            el('input', { type: 'file', multiple: 'multiple', style: 'display:none', 'data-mb-file-input': '1' }),
            el('div', {}, [
                el('i', { class: 'fa-solid fa-cloud-arrow-up', style: 'font-size:22px;display:block;margin-bottom:6px;' }),
                el('div', { html: 'Drop files here or <button type="button" class="mb-link" data-mb-browse="1">browse</button>' })
            ]),
            el('div', { class: 'mb-upload-progress', 'data-mb-progress': '1', style: 'display:none' })
        ]);

        var body = el('div', { class: 'mb-body' }, [
            uploader,
            el('div', { class: 'mb-loading', 'data-mb-loading': '1', text: 'Loading…' }),
            el('div', { class: 'mb-grid', 'data-mb-grid': '1', style: 'display:none' })
        ]);

        var footerChildren = null;
        if (mode === 'pick' && STATE.options.multiSelect) {
            footerChildren = [
                el('div', { class: 'mb-footer-info', 'data-mb-count': '1', text: '0 selected' }),
                el('div', {}, [
                    el('button', {
                        class: 'mb-btn', type: 'button', text: 'Cancel',
                        onclick: function () { closeActive([]); }
                    }),
                    el('button', {
                        class: 'mb-btn mb-btn-primary', type: 'button', 'data-mb-confirm': '1', text: 'Select', disabled: 'disabled',
                        style: 'margin-left:8px;',
                        onclick: function () { closeActive(Array.from(STATE.selection)); }
                    })
                ])
            ];
        }
        var footer = footerChildren ? el('div', { class: 'mb-footer' }, footerChildren) : null;

        var inner = el('div', { class: isModal ? 'mb-modal' : 'mb-inline' }, [header, body, footer]);

        if (isModal) {
            var overlay = el('div', { class: 'mb-overlay', onclick: function (e) {
                if (e.target === overlay) closeActive(mode === 'pick' ? (STATE.options.multiSelect ? [] : null) : undefined);
            }}, [inner]);
            return overlay;
        }
        return inner;
    }

    function renderTilesInto(root, html) {
        var grid = root.querySelector('[data-mb-grid]');
        var loading = root.querySelector('[data-mb-loading]');
        if (loading) loading.style.display = 'none';
        if (!grid) return;
        grid.innerHTML = html || '';
        grid.style.display = '';

        var tiles = grid.querySelectorAll('.media-tile');
        if (tiles.length === 0) {
            grid.style.display = 'none';
            var empty = root.querySelector('[data-mb-empty]');
            if (!empty) {
                empty = el('div', { class: 'mb-empty', 'data-mb-empty': '1', text: 'No media yet - upload some files to get started.' });
                grid.parentNode.insertBefore(empty, grid);
            }
            empty.style.display = '';
            return;
        }
        var acceptList = (STATE.options && STATE.options.accept) || null;
        var isPick = STATE.mode === 'pick';
        var isMulti = isPick && STATE.options.multiSelect;

        Array.prototype.forEach.call(tiles, function (tile) {
            var url = tile.getAttribute('data-url') || '';
            if (acceptList && !acceptMatches(url, acceptList)) {
                tile.remove();
                return;
            }
            if (isPick && !isMulti) {
                var btn = el('button', {
                    class: 'mb-pick-btn', type: 'button', text: 'Select',
                    onclick: function (e) { e.stopPropagation(); closeActive(url); }
                });
                tile.appendChild(btn);
            }
            if (isMulti) {
                var check = el('div', {
                    class: 'mb-check',
                    html: '<i class="fa-solid fa-check"></i>',
                    onclick: function (e) { e.stopPropagation(); toggleSelection(tile, url); }
                });
                tile.appendChild(check);
                tile.addEventListener('click', function (e) {
                    if (e.target.closest('.media-actions') || e.target.closest('a')) return;
                    toggleSelection(tile, url);
                });
                if (STATE.selection.has(url)) tile.classList.add('is-selected');
            }
        });

        if (grid.querySelectorAll('.media-tile').length === 0) {
            grid.style.display = 'none';
            var emptyAfter = root.querySelector('[data-mb-empty]') || el('div', { class: 'mb-empty', 'data-mb-empty': '1', text: 'No matching files.' });
            if (!emptyAfter.parentNode) grid.parentNode.insertBefore(emptyAfter, grid);
            emptyAfter.style.display = '';
        }
    }

    function toggleSelection(tile, url) {
        if (STATE.selection.has(url)) {
            STATE.selection.delete(url);
            tile.classList.remove('is-selected');
        } else {
            STATE.selection.add(url);
            tile.classList.add('is-selected');
        }
        var root = STATE.root;
        var counter = root.querySelector('[data-mb-count]');
        var confirm = root.querySelector('[data-mb-confirm]');
        var n = STATE.selection.size;
        if (counter) counter.textContent = n + ' selected';
        if (confirm) {
            if (n > 0) confirm.removeAttribute('disabled');
            else confirm.setAttribute('disabled', 'disabled');
        }
    }

    function loadTiles(root) {
        var grid = root.querySelector('[data-mb-grid]');
        var loading = root.querySelector('[data-mb-loading]');
        if (grid) grid.style.display = 'none';
        if (loading) loading.style.display = '';
        return fetch('/api/admin/media?action=list', { credentials: 'same-origin' })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                if (!d || !d.success) throw new Error((d && d.message) || 'Failed to load media');
                renderTilesInto(root, d.html || '');
            })
            .catch(function (err) {
                if (loading) loading.textContent = 'Error loading media: ' + err.message;
            });
    }

    function wireUploads(root) {
        var dz = root.querySelector('[data-mb-dropzone]');
        var input = root.querySelector('[data-mb-file-input]');
        var browseBtn = root.querySelector('[data-mb-browse]');
        if (!dz || !input) return;

        if (browseBtn) browseBtn.addEventListener('click', function () { input.click(); });

        input.addEventListener('change', function () {
            if (input.files && input.files.length) uploadFiles(root, input.files);
            input.value = '';
        });

        ['dragenter', 'dragover'].forEach(function (ev) {
            dz.addEventListener(ev, function (e) { e.preventDefault(); dz.classList.add('is-drag'); });
        });
        ['dragleave', 'drop'].forEach(function (ev) {
            dz.addEventListener(ev, function (e) { e.preventDefault(); dz.classList.remove('is-drag'); });
        });
        dz.addEventListener('drop', function (e) {
            if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length) {
                uploadFiles(root, e.dataTransfer.files);
            }
        });
    }

    function uploadFiles(root, files) {
        var pBox = root.querySelector('[data-mb-progress]');
        if (!pBox) return;
        pBox.style.display = 'block';
        pBox.innerHTML = '';
        var done = 0, total = files.length;
        Array.prototype.forEach.call(files, function (f, idx) {
            var bar = el('div', { class: 'mb-upload-bar' }, [
                el('span', { class: 'mb-upload-bar-label', text: f.name }),
                el('span', { class: 'mb-upload-bar-track' }, [el('span', { class: 'mb-upload-bar-fill', 'data-fill': String(idx) })]),
                el('span', { class: 'mb-upload-bar-pct', 'data-pct': String(idx), text: '0%' })
            ]);
            pBox.appendChild(bar);

            var fd = new FormData();
            fd.append('action', 'upload');
            fd.append('file', f);
            var xhr = new XMLHttpRequest();
            xhr.upload.onprogress = function (e) {
                if (e.lengthComputable) {
                    var p = Math.round((e.loaded / e.total) * 100);
                    var fill = pBox.querySelector('[data-fill="' + idx + '"]');
                    var pct = pBox.querySelector('[data-pct="' + idx + '"]');
                    if (fill) fill.style.width = p + '%';
                    if (pct) pct.textContent = p + '%';
                }
            };
            xhr.onload = function () {
                done++;
                try {
                    var d = JSON.parse(xhr.responseText);
                    if (!d.success) bar.classList.add('mb-upload-bar-error');
                } catch (e) { bar.classList.add('mb-upload-bar-error'); }
                if (done === total) {
                    setTimeout(function () {
                        pBox.style.display = 'none';
                        loadTiles(root);
                    }, 300);
                }
            };
            xhr.onerror = function () { done++; bar.classList.add('mb-upload-bar-error'); };
            xhr.open('POST', '/api/admin/media');
            xhr.send(fd);
        });
    }

    function onEscape(e) {
        if (e.key !== 'Escape') return;
        if (!STATE.isActive) return;
        if (STATE.mode === 'mount') return;
        closeActive(STATE.mode === 'pick' ? (STATE.options.multiSelect ? [] : null) : undefined);
    }

    function closeActive(resolveValue) {
        if (!STATE.isActive) return;
        var resolver = STATE.resolver;
        var root = STATE.root;
        var mode = STATE.mode;

        STATE.isActive = false;
        STATE.resolver = null;
        STATE.root = null;
        STATE.mode = null;
        STATE.options = null;
        STATE.selection = new Set();

        document.removeEventListener('keydown', onEscape);

        if ((mode === 'pick' || mode === 'open') && root && root.parentNode) {
            root.parentNode.removeChild(root);
        }

        if (resolver) resolver(resolveValue);
    }

    function rejectReentry(returnForPick) {
        if (typeof console !== 'undefined' && console.warn) {
            console.warn('[mediaBrowser] another instance is already active; call ignored.');
        }
        return returnForPick;
    }

    var mediaBrowser = {
        pick: function (options) {
            options = options || {};
            if (STATE.isActive) return Promise.resolve(rejectReentry(options.multiSelect ? [] : null));

            injectStyles();
            STATE.isActive = true;
            STATE.mode = 'pick';
            STATE.options = { accept: options.accept || null, multiSelect: !!options.multiSelect };
            STATE.selection = new Set();

            var root = buildShell('pick');
            STATE.root = root;
            document.body.appendChild(root);
            document.addEventListener('keydown', onEscape);
            wireUploads(root);
            loadTiles(root);

            return new Promise(function (resolve) { STATE.resolver = resolve; });
        },

        open: function () {
            if (STATE.isActive) return Promise.resolve(rejectReentry(undefined));

            injectStyles();
            STATE.isActive = true;
            STATE.mode = 'open';
            STATE.options = {};
            STATE.selection = new Set();

            var root = buildShell('open');
            STATE.root = root;
            document.body.appendChild(root);
            document.addEventListener('keydown', onEscape);
            wireUploads(root);
            loadTiles(root);

            return new Promise(function (resolve) { STATE.resolver = resolve; });
        },

        mount: function (containerOrSelector) {
            if (STATE.isActive) { rejectReentry(); return { unmount: function(){}, refresh: function(){} }; }

            var container = typeof containerOrSelector === 'string'
                ? document.querySelector(containerOrSelector)
                : containerOrSelector;
            if (!container) {
                console.warn('[mediaBrowser] mount target not found:', containerOrSelector);
                return { unmount: function(){}, refresh: function(){} };
            }

            injectStyles();
            STATE.isActive = true;
            STATE.mode = 'mount';
            STATE.options = {};
            STATE.selection = new Set();

            var root = buildShell('mount');
            STATE.root = root;
            container.appendChild(root);
            wireUploads(root);
            loadTiles(root);

            return {
                unmount: function () {
                    if (STATE.root === root) {
                        STATE.isActive = false;
                        STATE.mode = null;
                        STATE.root = null;
                        STATE.options = null;
                        STATE.selection = new Set();
                    }
                    if (root.parentNode) root.parentNode.removeChild(root);
                },
                refresh: function () {
                    if (STATE.root === root) loadTiles(root);
                }
            };
        }
    };

    global.mediaBrowser = mediaBrowser;
})(typeof window !== 'undefined' ? window : this);

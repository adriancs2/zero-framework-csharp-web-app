/* =========================================================
   Content editor page wiring (pages / posts).
   Static counterpart to engine/ContentEditor.cs.

   Dynamic, per-request values are injected by the server via a
   small inline <script> that defines window.HEARTH_EDITOR BEFORE
   this file is loaded:

       window.HEARTH_EDITOR = {
           hasStoredFormat: true|false,  // a saved DB content_format exists
           apiUrl:  '/api/admin/...',    // save endpoint (POST)
           listUrl: '/admin/...'         // back-to-list URL after save
       };

   Everything else here is static and safe to cache.
   Requires: editor.js (WYSIWYG), media-browser.js (optional, cover picker).
   ========================================================= */

(function () {
    'use strict';

    function init() {
        var cfg = window.HEARTH_EDITOR || {};
        var apiUrl = cfg.apiUrl || '';
        var listUrl = cfg.listUrl || '';
        var hasStoredFormat = cfg.hasStoredFormat === true;

        // The editor form must exist; if not, this isn't the editor page.
        var form = document.getElementById('contentForm');
        if (!form) return;

        // ===== Title -> slug auto-fill =====
        var titleEl = document.getElementById('title');
        var slugEl = document.getElementById('slug');
        var slugPreviewEl = document.getElementById('slugPreview');

        function slugify(s) {
            return s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
        }
        if (titleEl) {
            titleEl.addEventListener('input', function () {
                if (slugEl && !slugEl.dataset.touched) {
                    var s = slugify(this.value);
                    slugEl.value = s;
                    if (slugPreviewEl) slugPreviewEl.textContent = s;
                }
            });
        }
        if (slugEl) {
            slugEl.addEventListener('input', function () {
                this.dataset.touched = '1';
                if (slugPreviewEl) slugPreviewEl.textContent = this.value;
            });
        }

        // ===== Cover image preview + picker =====
        // Guarded by element presence, so this is harmless when there is
        // no cover field on the page.
        var coverInput = document.getElementById('cover_image');
        if (coverInput) {
            coverInput.addEventListener('input', function () {
                var v = this.value.trim();
                var box = document.getElementById('coverPreview');
                if (!box) return;
                if (v) { box.style.display = ''; box.querySelector('img').src = v; }
                else { box.style.display = 'none'; }
            });
        }
        window.pickCover = async function () {
            if (window.mediaBrowser && typeof window.mediaBrowser.pick === 'function') {
                try {
                    var picked = await window.mediaBrowser.pick({ accept: ['image/*'] });
                    if (picked) {
                        var el = document.getElementById('cover_image');
                        el.value = picked.trim();
                        el.dispatchEvent(new Event('input'));
                    }
                } catch (e) { console.error('Media picker error:', e); }
            } else {
                var url = prompt('Paste an image URL (e.g. /uploads/photo.jpg):');
                if (url) {
                    var el2 = document.getElementById('cover_image');
                    el2.value = url.trim();
                    el2.dispatchEvent(new Event('input'));
                }
            }
        };

        // ===== Editor type toggle =====
        // The WYSIWYG and Markdown views share the hidden form field
        // [name=content] via data-editor-source. We swap which input is
        // visible and re-route content sync.
        var fmtSelect = document.getElementById('content_format');
        var wysiwygWrap = document.getElementById('wysiwyg-wrap');
        var markdownWrap = document.getElementById('markdown-wrap');
        var markdownTa = document.getElementById('markdown_content');
        var sourceTa = document.querySelector('[data-editor-source]');

        function currentFormat() {
            return fmtSelect ? fmtSelect.value : 'html';
        }

        // Sync the form-bound field from whichever editor is active.
        function syncContentToSource() {
            if (!sourceTa) return;
            if (currentFormat() === 'markdown' && markdownTa) {
                sourceTa.value = markdownTa.value;
            } else if (window.editor && typeof window.editor.getHTML === 'function') {
                sourceTa.value = window.editor.getHTML();
            } else {
                var fallback = document.getElementById('editor');
                if (fallback && typeof fallback.getHTML === 'function') sourceTa.value = fallback.getHTML();
            }
        }

        // Editor-type precedence: DB > LocalStorage > Default.
        //  - hasStoredFormat = true  -> a real saved value exists; the server
        //    already rendered it, so we leave it alone (DB wins).
        //  - hasStoredFormat = false -> new item, or legacy row with no saved
        //    format; fall back to the user's last LocalStorage choice, else
        //    the rendered default.
        var EDITOR_PREF_KEY = 'hearth.editorType';

        // Swap which editor is visible and re-route content into it.
        function applyFormat(fmt) {
            if (fmt !== 'markdown') fmt = 'html';
            if (fmtSelect) fmtSelect.value = fmt;
            if (fmt === 'markdown') {
                if (markdownTa && sourceTa) markdownTa.value = sourceTa.value;
                if (wysiwygWrap) wysiwygWrap.style.display = 'none';
                if (markdownWrap) markdownWrap.style.display = '';
            } else {
                if (sourceTa) {
                    // Push markdown text back into the WYSIWYG verbatim if format flips.
                    if (markdownTa) sourceTa.value = markdownTa.value;
                    var ed = document.getElementById('editor');
                    if (ed && typeof ed.setHTML === 'function') ed.setHTML(sourceTa.value);
                }
                if (markdownWrap) markdownWrap.style.display = 'none';
                if (wysiwygWrap) wysiwygWrap.style.display = '';
            }
        }

        function savePref(fmt) {
            try { localStorage.setItem(EDITOR_PREF_KEY, fmt); } catch (e) {}
        }

        function readPref() {
            try { return localStorage.getItem(EDITOR_PREF_KEY); } catch (e) { return null; }
        }

        if (fmtSelect) {
            // User changing the dropdown: apply the swap immediately, but
            // don't persist the LocalStorage preference yet — that only
            // happens on actual save (see the form 'submit' listener below),
            // so an abandoned/cancelled edit doesn't change the user's
            // remembered default for next time.
            fmtSelect.addEventListener('change', function () {
                applyFormat(currentFormat());
            });

            // On load: when no saved DB format exists (new item or legacy
            // empty row), adopt the LocalStorage preference. A real DB value
            // is left as rendered.
            if (!hasStoredFormat) {
                var pref = readPref();
                if (pref === 'markdown' || pref === 'html') {
                    if (pref !== currentFormat()) applyFormat(pref);
                }
            }
        }

        // Persist the editor-type preference to LocalStorage only once the
        // user actually saves, not on every dropdown change.
        form.addEventListener('submit', function () {
            savePref(currentFormat());
        });

        // ===== Markdown tabbed Edit / Preview =====
        // The preview tab POSTs the current markdown to the API, which renders
        // it to a full themed HTML document and returns it inline as
        // { success: true, html: "..." }. On success we inject that HTML into
        // the iframe via srcdoc - one round-trip, no token, no second request.
        var mdTabs = document.getElementById('md-tabs');
        if (mdTabs) {
            var tabBtns = mdTabs.querySelectorAll('[data-md-tab]');
            var panels = mdTabs.querySelectorAll('[data-md-panel]');
            var frame = document.getElementById('md-preview-frame');
            var statusEl = document.getElementById('md-preview-status');

            var activateTab = function (name) {
                tabBtns.forEach(function (b) {
                    var on = b.dataset.mdTab === name;
                    b.classList.toggle('is-active', on);
                    b.setAttribute('aria-selected', on ? 'true' : 'false');
                });
                panels.forEach(function (p) {
                    p.classList.toggle('is-active', p.dataset.mdPanel === name);
                });
            };

            // Render a diagnostic readout into the iframe so any failure is
            // visible in the Preview pane itself (screenshot-friendly).
            var showDiag = function (title, lines) {
                var esc = function (s) {
                    return String(s == null ? '' : s)
                        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
                };
                var rows = (lines || []).map(function (l) {
                    return '<tr><td style="padding:4px 10px;color:#555;white-space:nowrap;vertical-align:top">'
                        + esc(l[0]) + '</td><td style="padding:4px 10px;font-family:monospace;white-space:pre-wrap">'
                        + esc(l[1]) + '</td></tr>';
                }).join('');
                frame.srcdoc =
                    '<!DOCTYPE html><html><body style="font-family:system-ui,sans-serif;padding:16px">'
                    + '<h2 style="margin:0 0 12px;color:#b00">' + esc(title) + '</h2>'
                    + '<table style="border-collapse:collapse;font-size:13px">' + rows + '</table>'
                    + '</body></html>';
                statusEl.style.display = 'none';
                frame.style.visibility = '';
            };

            var loadPreview = async function () {
                if (!frame) { return; }
                if (!markdownTa) {
                    showDiag('Preview error', [['stage', 'precheck'], ['reason', 'markdown textarea (#markdown_content) not found']]);
                    return;
                }
                statusEl.textContent = 'Loading preview…';
                statusEl.style.display = '';
                frame.style.visibility = 'hidden';

                var stage = 'start';
                var status = '(none)';
                var rawText = '';
                try {
                    stage = 'build-formdata';
                    var fd = new FormData();
                    fd.append('markdown', markdownTa.value);

                    stage = 'fetch';
                    var r = await fetch('/api/admin/preview-markdown', { method: 'POST', body: fd });
                    status = r.status + ' ' + r.statusText;

                    stage = 'read-text';
                    rawText = await r.text();

                    stage = 'parse-json';
                    var d;
                    try {
                        d = JSON.parse(rawText);
                    } catch (pe) {
                        showDiag('Preview failed: response was not JSON', [
                            ['stage', stage],
                            ['http status', status],
                            ['parse error', pe && pe.message],
                            ['body length', rawText.length],
                            ['body (first 1500)', rawText.slice(0, 1500)]
                        ]);
                        return;
                    }

                    stage = 'check-success';
                    if (!d.success) {
                        showDiag('Preview failed: server returned success=false', [
                            ['stage', stage],
                            ['http status', status],
                            ['message', d.message],
                            ['keys', Object.keys(d).join(', ')]
                        ]);
                        return;
                    }

                    stage = 'check-html';
                    if (typeof d.html !== 'string' || d.html.length === 0) {
                        showDiag('Preview failed: success=true but html is missing/empty', [
                            ['stage', stage],
                            ['http status', status],
                            ['typeof html', typeof d.html],
                            ['html length', d.html ? d.html.length : 0],
                            ['keys', Object.keys(d).join(', ')]
                        ]);
                        return;
                    }

                    stage = 'inject-srcdoc';
                    frame.srcdoc = d.html;
                    statusEl.style.display = 'none';
                    frame.style.visibility = '';
                } catch (ex) {
                    showDiag('Preview threw an exception', [
                        ['stage', stage],
                        ['http status', status],
                        ['error name', ex && ex.name],
                        ['error message', ex && ex.message],
                        ['body length', rawText ? rawText.length : 0],
                        ['body (first 800)', rawText ? rawText.slice(0, 800) : '']
                    ]);
                }
            };

            tabBtns.forEach(function (b) {
                b.addEventListener('click', function () {
                    var name = this.dataset.mdTab;
                    activateTab(name);
                    if (name === 'preview') loadPreview();
                });
            });
        }

        // ===== Save handler =====
        // Generic: iterates every checkbox in the form and normalizes its
        // value to "1"/"0" - works for is_published, show_in_nav, and any
        // future toggle without code changes here.
        window.saveItem = async function (e) {
            e.preventDefault();
            var theForm = document.getElementById('contentForm');
            // Pull content from the currently-active editor BEFORE building FormData.
            syncContentToSource();
            var fd = new FormData(theForm);
            fd.append('action', 'save');
            theForm.querySelectorAll('input[type=checkbox]').forEach(function (cb) {
                fd.set(cb.name, cb.checked ? '1' : '0');
            });
            try {
                var r = await fetch(apiUrl, { method: 'POST', body: fd });
                var d = await r.json();
                if (d.success) {
                    // Stay on the editor after saving so the author can keep working
                    // and save milestones without being thrown back to the list.
                    // For a brand-new item, adopt the id the server just created so
                    // the NEXT save updates the same record (no duplicate), and
                    // reflect the edit URL so a manual refresh reloads this item.
                    var idField = theForm.querySelector('input[name=id]');
                    if (idField && (!idField.value || idField.value === '0')) {
                        var newId = d.data && d.data.id;
                        if (newId) {
                            idField.value = newId;
                            if (listUrl) {
                                try { history.replaceState(null, '', listUrl + '/edit/' + newId); } catch (e) {}
                            }
                        }
                    }
                    // Save the user's preference to LocalStorage
                    savePref(currentFormat());
                    showGoodMessage('Saved', 'Changes saved.');
                } else {
                    showErrorMessage('Save failed', d.message);
                }
            } catch (ex) { showErrorMessage('Network error', 'Please try again.'); }
            return false;
        };
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
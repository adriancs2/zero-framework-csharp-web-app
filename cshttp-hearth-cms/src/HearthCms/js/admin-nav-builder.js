// ============================================================
// Modern CMS - Navigation menu builder
// Page: /admin/nav   (loaded only on that page)
//
// Works on an in-memory model `menu`: an array of nodes
//   { label, url, children:[ { label, url } ] }
// Two levels only - sub-items never get an "add sub-item" button.
//
// Text edits write straight into the model (no re-render, so focus
// is never lost). Structural changes (add/move/delete) re-render.
// Saving serialises `menu` and POSTs it to /api/admin/nav.
// ============================================================

(function () {
    var menu = Array.isArray(window.__navMenu) ? window.__navMenu : [];
    var quick = Array.isArray(window.__navQuick) ? window.__navQuick : [];

    // Normalise shape so every node has a children array at the top level.
    menu.forEach(function (n) {
        if (!n.children || !Array.isArray(n.children)) n.children = [];
    });

    var root = document.getElementById('navBuilderRoot');

    // ---------- helpers ----------
    function el(tag, cls, attrs) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (attrs) Object.keys(attrs).forEach(function (k) { e.setAttribute(k, attrs[k]); });
        return e;
    }

    function iconBtn(icon, title, cls, onClick) {
        var b = el('button', 'icon-btn' + (cls ? ' ' + cls : ''), { type: 'button', title: title });
        b.innerHTML = '<i class="fa-solid ' + icon + '"></i>';
        b.addEventListener('click', onClick);
        return b;
    }

    function moveInArray(arr, from, to) {
        if (to < 0 || to >= arr.length) return;
        var item = arr.splice(from, 1)[0];
        arr.splice(to, 0, item);
    }

    // ---------- a single editable row (label + url inputs) ----------
    function fieldRow(node, isChild) {
        var row = el('div', 'nav-fields');

        var labelIn = el('input', 'nav-input', { type: 'text', placeholder: 'Label' });
        labelIn.value = node.label || '';
        labelIn.addEventListener('input', function () { node.label = labelIn.value; });

        var urlIn = el('input', 'nav-input nav-input-url', {
            type: 'text',
            placeholder: isChild ? 'URL (e.g. /about)' : 'URL (blank = label only)'
        });
        urlIn.value = node.url || '';
        urlIn.addEventListener('input', function () { node.url = urlIn.value; });

        row.appendChild(labelIn);
        row.appendChild(urlIn);
        return row;
    }

    // ---------- render ----------
    function render() {
        root.innerHTML = '';

        if (menu.length === 0) {
            var empty = el('div', 'nav-empty');
            empty.innerHTML = '<i class="fa-solid fa-bars"></i><p>No menu items yet. Use <strong>Add item</strong> to start.</p>';
            root.appendChild(empty);
            return;
        }

        menu.forEach(function (top, ti) {
            var card = el('div', 'nav-item-card');

            // ----- top row -----
            var head = el('div', 'nav-item-head');
            head.appendChild(el('span', 'nav-grip', null)).innerHTML = '<i class="fa-solid fa-grip-vertical"></i>';
            head.appendChild(fieldRow(top, false));

            var headTools = el('div', 'nav-tools');
            headTools.appendChild(iconBtn('fa-arrow-up', 'Move up', '', function () { moveInArray(menu, ti, ti - 1); render(); }));
            headTools.appendChild(iconBtn('fa-arrow-down', 'Move down', '', function () { moveInArray(menu, ti, ti + 1); render(); }));
            headTools.appendChild(iconBtn('fa-plus', 'Add sub-item', '', function () {
                top.children.push({ label: '', url: '' });
                render();
            }));
            headTools.appendChild(iconBtn('fa-trash', 'Delete item', 'icon-btn-danger', function () {
                menu.splice(ti, 1); render();
            }));
            head.appendChild(headTools);
            card.appendChild(head);

            // ----- children -----
            if (top.children && top.children.length) {
                var kids = el('div', 'nav-children');
                top.children.forEach(function (child, ci) {
                    var crow = el('div', 'nav-child');
                    crow.appendChild(el('span', 'nav-child-elbow', null)).innerHTML =
                        '<i class="fa-solid fa-turn-up fa-rotate-90"></i>';
                    crow.appendChild(fieldRow(child, true));

                    var ctools = el('div', 'nav-tools');
                    ctools.appendChild(iconBtn('fa-arrow-up', 'Move up', '', function () { moveInArray(top.children, ci, ci - 1); render(); }));
                    ctools.appendChild(iconBtn('fa-arrow-down', 'Move down', '', function () { moveInArray(top.children, ci, ci + 1); render(); }));
                    ctools.appendChild(iconBtn('fa-trash', 'Delete sub-item', 'icon-btn-danger', function () {
                        top.children.splice(ci, 1); render();
                    }));
                    crow.appendChild(ctools);
                    kids.appendChild(crow);
                });
                card.appendChild(kids);
            }

            root.appendChild(card);
        });
    }

    // ---------- quick-add dropdown ----------
    function fillQuickSelect() {
        var sel = document.getElementById('navQuickSelect');
        if (!sel) return;
        sel.innerHTML = '';
        quick.forEach(function (q, i) {
            var o = document.createElement('option');
            o.value = String(i);
            o.textContent = q.label + '  (' + q.url + ')';
            sel.appendChild(o);
        });
    }

    // ---------- public actions (called from inline onclick in the page) ----------
    window.navAddTop = function () {
        menu.push({ label: '', url: '', children: [] });
        render();
    };

    window.navAddQuick = function () {
        var sel = document.getElementById('navQuickSelect');
        if (!sel || sel.value === '') return;
        var q = quick[parseInt(sel.value, 10)];
        if (!q) return;
        menu.push({ label: q.label, url: q.url, children: [] });
        render();
    };

    window.saveNav = function () {
        // Strip empties client-side too (server normalises again).
        var payload = menu
            .map(function (n) {
                var kids = (n.children || [])
                    .map(function (c) { return { label: (c.label || '').trim(), url: (c.url || '').trim() }; })
                    .filter(function (c) { return c.label || c.url; });
                return { label: (n.label || '').trim(), url: (n.url || '').trim(), children: kids };
            })
            .filter(function (n) { return n.label || n.children.length; });

        var fd = new FormData();
        fd.append('action', 'save');
        fd.append('menu', JSON.stringify(payload));

        fetch('/api/admin/nav', { method: 'POST', body: fd })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                if (d && d.success) showGoodMessage('Saved', 'Navigation menu updated.');
                else showErrorMessage('Save failed', (d && d.message) || 'Please try again.');
            })
            .catch(function () { showErrorMessage('Network error', 'Please try again.'); });
    };

    // ---------- init ----------
    fillQuickSelect();
    render();
})();

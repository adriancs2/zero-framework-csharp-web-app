// ============================================================
// Hearth CMS - Admin User Management
//
// Drives the /admin/users page: a popup dialog for add/edit, plus delete.
// All requests go to /api/admin/users; after any change the table block is
// refreshed from the server-rendered "list" action (the same HTML the page
// was first painted with), so the client never rebuilds rows itself.
//
// Depends on the toast helpers in admin.js (showGoodMessage / showErrorMessage).
// ============================================================
(function () {
    'use strict';

    var API = '/api/admin/users';

    // ----- small helpers -----
    function $(id) { return document.getElementById(id); }

    function post(fields) {
        var fd = new FormData();
        Object.keys(fields).forEach(function (k) { fd.append(k, fields[k]); });
        return fetch(API, { method: 'POST', body: fd }).then(function (r) { return r.json(); });
    }

    function toastOk(t, m) {
        if (typeof showGoodMessage === 'function') showGoodMessage(t, m);
    }
    function toastErr(t, m) {
        if (typeof showErrorMessage === 'function') showErrorMessage(t, m);
        else alert((t ? t + ': ' : '') + (m || ''));
    }

    // ----- dialog open/close -----
    function openDialog() {
        var ov = $('userDialogOverlay');
        if (ov) ov.style.display = 'flex';
        document.body.classList.add('user-dialog-open');
    }

    function closeDialog() {
        var ov = $('userDialogOverlay');
        if (ov) ov.style.display = 'none';
        document.body.classList.remove('user-dialog-open');
    }

    // Reset the form to a clean "create" state.
    function resetForm() {
        $('user_id').value = '0';
        $('userForm').reset();
        $('user_id').value = '0'; // reset() clears hidden too; re-assert
        $('user_role').value = 'admin';
    }

    // ----- create -----
    function openCreate() {
        resetForm();
        $('userDialogTitle').textContent = 'New user';
        $('user_password_label').textContent = 'Password';
        $('user_password').setAttribute('required', 'required');
        $('user_password_hint').textContent = 'Set a password for the new user.';
        openDialog();
        setTimeout(function () { $('user_username').focus(); }, 30);
    }

    // ----- edit -----
    function openEdit(id) {
        post({ action: 'get', id: id }).then(function (d) {
            if (!d.success || !d.data) { toastErr('Load failed', d.message || 'Could not load user.'); return; }
            var u = d.data;
            resetForm();
            $('user_id').value = u.id;
            $('user_username').value = u.username || '';
            $('user_display_name').value = u.display_name || '';
            $('user_email').value = u.email || '';
            $('user_role').value = (u.role === 'editor') ? 'editor' : 'admin';

            $('userDialogTitle').textContent = 'Edit user';
            // Password optional on edit: blank means keep the existing one.
            $('user_password_label').textContent = 'New password';
            $('user_password').removeAttribute('required');
            $('user_password').value = '';
            $('user_password_hint').textContent = 'Leave blank to keep the current password.';

            openDialog();
            setTimeout(function () { $('user_display_name').focus(); }, 30);
        }).catch(function () { toastErr('Network error', 'Please try again.'); });
    }

    // ----- submit (create or update) -----
    function submit(e) {
        if (e) e.preventDefault();
        var form = $('userForm');

        // Native validation (required username, password-on-create, pattern).
        if (form.reportValidity && !form.reportValidity()) return false;

        var fd = new FormData(form);
        fd.append('action', 'save');

        fetch(API, { method: 'POST', body: fd })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                if (d.success) {
                    closeDialog();
                    refreshList();
                    toastOk('Saved', 'User saved.');
                } else {
                    toastErr('Save failed', d.message);
                }
            })
            .catch(function () { toastErr('Network error', 'Please try again.'); });
        return false;
    }

    // ----- delete -----
    function del(id) {
        if (!confirm('Delete this user? This cannot be undone.')) return;
        post({ action: 'delete', id: id }).then(function (d) {
            if (d.success) {
                refreshList();
                toastOk('Deleted', 'User deleted.');
            } else {
                toastErr('Delete failed', d.message);
            }
        }).catch(function () { toastErr('Network error', 'Please try again.'); });
    }

    // ----- refresh the table block from the server -----
    function refreshList() {
        post({ action: 'list' }).then(function (d) {
            if (d.success && d.data && typeof d.data.html === 'string') {
                var wrap = $('userListWrap');
                if (wrap) wrap.innerHTML = d.data.html;
            }
        }).catch(function () { /* leave the stale table; a reload will fix it */ });
    }

    // Backdrop click closes (but clicks inside the dialog don't bubble here).
    function backdrop(e) {
        if (e && e.target && e.target.id === 'userDialogOverlay') closeDialog();
    }

    // Esc closes the dialog.
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') {
            var ov = $('userDialogOverlay');
            if (ov && ov.style.display !== 'none') closeDialog();
        }
    });

    // ----- expose the handlers the inline markup calls -----
    window.userOpenCreate = openCreate;
    window.userOpenEdit = openEdit;
    window.userSubmit = submit;
    window.userDelete = del;
    window.userCloseDialog = closeDialog;
    window.userDialogBackdrop = backdrop;
})();

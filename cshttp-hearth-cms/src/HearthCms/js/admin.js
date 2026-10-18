// ============================================================
// Modern CMS - Admin JS
// ============================================================

function adminToggleSidebar() {
    var sb = document.getElementById('adminSidebar');
    var ov = document.getElementById('adminSidebarOverlay');
    if (!sb) return;
    sb.classList.toggle('is-open');
    if (ov) ov.classList.toggle('is-open');
}

document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        var sb = document.getElementById('adminSidebar');
        var ov = document.getElementById('adminSidebarOverlay');
        if (sb && sb.classList.contains('is-open')) {
            sb.classList.remove('is-open');
            if (ov) ov.classList.remove('is-open');
        }
    }
});

function escapeHtml(text) {
    var div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}


// ============================================================
// Toast notifications
// ============================================================
const activeMessages = [];

function showGoodMessage(title, message) {
    showMessage(title, message, true);
}

function showErrorMessage(title, message) {
    showMessage(title, message, false);
}

function showMessage(title, message, isSuccess) {
    const container = document.createElement('div');
    container.className = 'toast-container';
    container.classList.add(isSuccess ? 'toast-success' : 'toast-error');

    const messageId = Date.now() + Math.random();
    container.dataset.messageId = messageId;

    const titleEl = document.createElement('div');
    titleEl.className = 'toast-title';
    titleEl.textContent = title;

    const messageEl = document.createElement('div');
    messageEl.className = 'toast-text';
    messageEl.textContent = message;

    container.appendChild(titleEl);
    container.appendChild(messageEl);
    document.body.appendChild(container);

    activeMessages.push({
        id: messageId,
        element: container,
        timeout: null
    });

    setTimeout(() => {
        container.classList.add('toast-show');
        updateMessagePositions();
    }, 10);

    const messageObj = activeMessages.find(m => m.id === messageId);

    messageObj.timeout = setTimeout(() => {
        removeMessage(messageId);
    }, 2700);

    container.addEventListener('click', () => {
        if (messageObj.timeout) {
            clearTimeout(messageObj.timeout);
        }
        removeMessage(messageId);
    });
}

function removeMessage(messageId) {
    const index = activeMessages.findIndex(m => m.id === messageId);
    if (index === -1) return;

    const messageObj = activeMessages[index];
    const container = messageObj.element;

    container.classList.remove('toast-show');

    activeMessages.splice(index, 1);

    setTimeout(() => {
        container.remove();
        updateMessagePositions();
    }, 300);
}

function updateMessagePositions() {
    let currentTop = 30;

    activeMessages.forEach((messageObj, index) => {
        const container = messageObj.element;
        container.style.top = currentTop + 'px';

        const containerHeight = container.offsetHeight;
        currentTop += containerHeight + 20;
    });
}

// Persist a toast across a reload/navigation.
function flashGoodAndReload(title, message) {
    try { sessionStorage.setItem('pendingToast', JSON.stringify({ ok: true, title: title, message: message })); } catch (e) {}
    location.reload();
}
function flashErrorAndReload(title, message) {
    try { sessionStorage.setItem('pendingToast', JSON.stringify({ ok: false, title: title, message: message })); } catch (e) {}
    location.reload();
}
function flashGoodAndGo(title, message, url) {
    try { sessionStorage.setItem('pendingToast', JSON.stringify({ ok: true, title: title, message: message })); } catch (e) {}
    window.location = url;
}

document.addEventListener('DOMContentLoaded', function () {
    try {
        var raw = sessionStorage.getItem('pendingToast');
        if (raw) {
            sessionStorage.removeItem('pendingToast');
            var o = JSON.parse(raw);
            if (o && o.ok) showGoodMessage(o.title || 'Success', o.message || '');
            else if (o) showErrorMessage(o.title || 'Error', o.message || '');
        }
    } catch (e) {}
    if (window.__pendingFlash) {
        var f = window.__pendingFlash;
        window.__pendingFlash = null;
        if (f.ok) showGoodMessage(f.title || 'Success', f.message || '');
        else showErrorMessage(f.title || 'Error', f.message || '');
    }
});

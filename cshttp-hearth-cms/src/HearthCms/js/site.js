// ============================================================
// Modern CMS - Public site JS
// ============================================================

function toggleNav() {
    var nav = document.getElementById('siteNav');
    var ov = document.getElementById('navOverlay');
    if (!nav) return;
    nav.classList.toggle('is-open');
    if (ov) ov.classList.toggle('is-open');
}

document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        var nav = document.getElementById('siteNav');
        var ov = document.getElementById('navOverlay');
        if (nav && nav.classList.contains('is-open')) {
            nav.classList.remove('is-open');
            if (ov) ov.classList.remove('is-open');
        }
    }
});

function escapeHtml(text) {
    var div = document.createElement('div');
    div.textContent = text == null ? '' : String(text);
    return div.innerHTML;
}

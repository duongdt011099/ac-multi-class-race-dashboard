// Frameless window: the host tells the page whether the window is maximized, so the maximize button
// can show the restore icon. The state is kept as a class on <html>, which Blazor never re-renders.
// The page also asks the host to start a window move when its title-bar-like areas are dragged.
(function () {
    var webview = window.chrome && window.chrome.webview;
    if (!webview) { return; }

    function apply(maximized) {
        document.documentElement.classList.toggle('wec-window-maximized', maximized);
        var button = document.querySelector('.wec-window-btn--maximize');
        if (button) {
            button.title = maximized ? 'Restore' : 'Maximize';
            button.setAttribute('aria-label', maximized ? 'Restore window' : 'Maximize window');
        }
    }

    webview.addEventListener('message', function (e) {
        if (e.data === 'maximized') { apply(true); }
        else if (e.data === 'normal') { apply(false); }
    });

    // The button may render after this runs, so keep its labels in step once it appears.
    new MutationObserver(function () {
        var button = document.querySelector('.wec-window-btn--maximize');
        if (button && !button.dataset.wecSynced) {
            button.dataset.wecSynced = '1';
            apply(document.documentElement.classList.contains('wec-window-maximized'));
        }
    }).observe(document.documentElement, { childList: true, subtree: true });

    // Dragging: the sidebar header and the empty top band of the window act as the title bar. The move
    // only starts once the mouse has travelled a few pixels, so a plain click (on the logo, say) still works.
    var DRAG_BAND = 40;
    var DRAG_THRESHOLD = 4;
    var INTERACTIVE = 'a[href]:not(.navbar-brand), button, input, select, textarea, label, summary, [role="button"], ' +
        '[role="tab"], [role="radio"], [contenteditable], [draggable="true"], .wec-window-controls';
    var press = null;

    function inDragZone(e) {
        var target = e.target instanceof Element ? e.target : null;
        if (!target || target.closest(INTERACTIVE)) { return false; }
        if (target.closest('.top-row')) { return true; }
        // Not over a popup: those sit on top of the page and keep their own mouse handling.
        return e.clientY < DRAG_BAND && !target.closest('.wec-modal, [role="dialog"], [role="listbox"], [role="menu"]');
    }

    document.addEventListener('mousedown', function (e) {
        press = e.button === 0 && e.detail === 1 && inDragZone(e) ? { x: e.screenX, y: e.screenY } : null;
    }, true);

    document.addEventListener('mousemove', function (e) {
        if (!press) { return; }
        if ((e.buttons & 1) === 0) { press = null; return; }
        if (Math.abs(e.screenX - press.x) < DRAG_THRESHOLD && Math.abs(e.screenY - press.y) < DRAG_THRESHOLD) { return; }
        press = null;
        webview.postMessage('drag');
    }, true);

    document.addEventListener('mouseup', function () { press = null; }, true);

    // The logo is a link with a picture, which the browser would otherwise start dragging itself.
    document.addEventListener('dragstart', function (e) {
        if (e.target instanceof Element && e.target.closest('.top-row')) { e.preventDefault(); }
    }, true);

    document.addEventListener('dblclick', function (e) {
        if (e.button === 0 && inDragZone(e)) {
            webview.postMessage('maximize');
        }
    }, true);

    webview.postMessage('state');
})();

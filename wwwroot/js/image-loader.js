// Marks images as loaded (or failed) so the stylesheet can show a shimmer placeholder until the
// picture arrives and fade it in afterwards. Blazor adds and re-renders images constantly, so one
// observer on the document covers all of them.
(function () {
    var SELECTOR = [
        '.wec-table__logo', '.wec-podium__logo', '.wec-drivers__livery', '.wec-drivers__car-img',
        '.wec-race-track__thumb', '.wec-nav-human__car-img', '.wec-nav-human__logo',
        '.wec-image-viewer__img', '.wec-track-preview__img', '.wec-form-field__preview',
        '.wec-skin-preview__img', 'img.wec-imgselect__img', '.wec-navbar-brand__logo'
    ].join(',');

    // Shown in place of any picture that cannot be loaded. The address that failed is kept so the
    // picture can be tried again later (a skin that is put back on disk, for example).
    var PLACEHOLDER = 'data:image/svg+xml;utf8,' + encodeURIComponent(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 48"><rect width="64" height="48" fill="#1d1d24"/>' +
        '<g fill="none" stroke="#6b6b78" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">' +
        '<rect x="16" y="12" width="32" height="24" rx="3"/><circle cx="26" cy="21" r="2.5"/>' +
        '<path d="M16 32l9-8 7 6 5-4 11 9"/></g></svg>');

    function showPlaceholder(img) {
        var src = img.getAttribute('src') || '';
        if (!src || src === PLACEHOLDER) { return; }
        img.dataset.wecFailedSrc = src;
        img.dataset.wecTracked = PLACEHOLDER;
        img.classList.add('is-error');
        img.setAttribute('src', PLACEHOLDER);
    }

    function settle(img) {
        if (!img.complete) { return; }
        if (img.naturalWidth > 0) { img.classList.add('is-loaded'); } else { showPlaceholder(img); }
    }

    function track(img) {
        if (img.getAttribute('src') === PLACEHOLDER) { return; }
        delete img.dataset.wecFailedSrc;
        if (img.dataset.wecTracked === img.getAttribute('src')) { return; }
        img.dataset.wecTracked = img.getAttribute('src') || '';
        img.classList.remove('is-loaded', 'is-error');
        settle(img);
    }

    function scan(root) {
        if (root.nodeType !== 1) { return; }
        if (root.matches && root.matches(SELECTOR)) { track(root); }
        if (root.querySelectorAll) { root.querySelectorAll(SELECTOR).forEach(track); }
    }

    // load/error do not bubble, but they can be captured on the document.
    document.addEventListener('load', function (e) {
        if (e.target.matches && e.target.matches(SELECTOR)) { e.target.classList.add('is-loaded'); }
    }, true);
    document.addEventListener('error', function (e) {
        if (e.target.tagName === 'IMG') { showPlaceholder(e.target); }
    }, true);

    new MutationObserver(function (mutations) {
        mutations.forEach(function (m) {
            if (m.type === 'attributes') { track(m.target); return; }
            m.addedNodes.forEach(scan);
        });
    }).observe(document.documentElement, {
        childList: true, subtree: true, attributes: true, attributeFilter: ['src']
    });

    // Blazor never re-sets an unchanged src, so a picture that was missing (a skin deleted and put
    // back on disk) would stay hidden. Retry the failed ones whenever the window is used again.
    function retryFailed() {
        document.querySelectorAll('img[data-wec-failed-src]').forEach(function (img) {
            var src = img.dataset.wecFailedSrc;
            // The page may have given the picture a new address since it failed.
            if (!src || img.getAttribute('src') !== PLACEHOLDER) { delete img.dataset.wecFailedSrc; return; }
            var base = src.replace(/([?&])_r=\d+&?/, '$1').replace(/[?&]$/, '');
            var retry = base + (base.indexOf('?') < 0 ? '?' : '&') + '_r=' + Date.now();
            // Probe off-screen first so a picture that is still missing does not flicker.
            var probe = new Image();
            probe.onload = function () {
                if (img.getAttribute('src') !== PLACEHOLDER) { return; }
                img.classList.remove('is-error');
                delete img.dataset.wecFailedSrc;
                img.dataset.wecTracked = '';
                img.setAttribute('src', retry);
            };
            probe.src = retry;
        });
    }
    // The host window does not always raise focus events, so also look again every few seconds.
    setInterval(function () { if (!document.hidden) { retryFailed(); } }, 5000);
    window.addEventListener('focus', retryFailed);
    document.addEventListener('visibilitychange', function () {
        if (!document.hidden) { retryFailed(); }
    });

    scan(document.documentElement);
})();

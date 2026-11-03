// Marks images as loaded (or failed) so the stylesheet can show a shimmer placeholder until the
// picture arrives and fade it in afterwards. Blazor adds and re-renders images constantly, so one
// observer on the document covers all of them.
(function () {
    var SELECTOR = [
        '.wec-table__logo', '.wec-podium__logo', '.wec-drivers__livery', '.wec-drivers__car-img',
        '.wec-race-track__thumb', '.wec-nav-human__car-img', '.wec-nav-human__logo',
        '.wec-image-viewer__img', '.wec-track-preview__img', '.wec-form-field__preview',
        '.wec-skin-preview__img', '.wec-navbar-brand__logo'
    ].join(',');

    function settle(img) {
        if (!img.complete) { return; }
        img.classList.add(img.naturalWidth > 0 ? 'is-loaded' : 'is-error');
    }

    function track(img) {
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
        if (e.target.matches && e.target.matches(SELECTOR)) { e.target.classList.add('is-error'); }
    }, true);

    new MutationObserver(function (mutations) {
        mutations.forEach(function (m) {
            if (m.type === 'attributes') { track(m.target); return; }
            m.addedNodes.forEach(scan);
        });
    }).observe(document.documentElement, {
        childList: true, subtree: true, attributes: true, attributeFilter: ['src']
    });

    scan(document.documentElement);
})();

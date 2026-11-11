// The class picker's dropdown is position:fixed so the scrolling modal body cannot clip it.
// Fixed elements have no anchor, so it is placed under (or, when short on room, above) the input.
window.wecPlaceClassMenu = function (wrapper) {
    if (!wrapper) return;
    var menu = wrapper.querySelector('.wec-class-search__menu');
    var input = wrapper.querySelector('input');
    if (!menu || !input) return;

    var rect = input.getBoundingClientRect();
    var gap = 4;
    var below = window.innerHeight - rect.bottom - gap - 8;
    var above = rect.top - gap - 8;
    var height = menu.scrollHeight + 2;
    var openUp = height > below && above > below;
    var room = Math.max(120, openUp ? above : below);

    menu.style.left = rect.left + 'px';
    menu.style.width = rect.width + 'px';
    menu.style.maxHeight = Math.min(room, 208) + 'px';
    var shown = Math.min(height, Math.min(room, 208));
    menu.style.top = (openUp ? rect.top - gap - shown : rect.bottom + gap) + 'px';
};

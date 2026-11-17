// Quick Race: the class columns scroll sideways with the mouse wheel when they overflow.
(function () {
    document.addEventListener('wheel', function (e) {
        var strip = e.target instanceof Element ? e.target.closest('.wec-qr-columns') : null;
        if (!strip || e.ctrlKey || e.shiftKey) { return; }
        // A list that scrolls on its own keeps the wheel.
        var list = e.target.closest('.wec-qr-col__list');
        if (list && list.scrollHeight > list.clientHeight) { return; }
        var max = strip.scrollWidth - strip.clientWidth;
        if (max <= 0) { return; }
        var delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
        var next = Math.max(0, Math.min(max, strip.scrollLeft + delta));
        if (next === strip.scrollLeft) { return; }
        strip.scrollLeft = next;
        e.preventDefault();
    }, { passive: false });
})();

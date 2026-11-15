// Highlight layer for the guided tour: a glowing ring around the element the engineer is talking about.
// It never darkens or blocks the page, so everything stays usable during the tour. The ring steps aside
// while a dialog is open so it does not glow over the form.
window.wecTour = (function () {
    const PAD = 8;
    const WAIT_MS = 4000;
    const TICK_MS = 120;

    let layer = null;
    let ring = null;
    let timer = null;
    let selector = null;
    let passThrough = false;
    let dotnet = null;
    let targetEl = null;
    let lastKey = "";
    let pending = null;
    let started = 0;

    function ensureLayer() {
        if (layer && document.body.contains(layer)) {
            return;
        }

        layer = document.createElement("div");
        layer.className = "wec-tour-layer";

        ring = document.createElement("div");
        ring.className = "wec-tour-ring";

        layer.appendChild(ring);
        document.body.appendChild(layer);
    }

    function query(sel) {
        try {
            return sel ? document.querySelector(sel) : null;
        } catch {
            return null;
        }
    }

    function isVisible(el) {
        const r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }

    function dialogOpen() {
        return !!document.querySelector(".wec-modal-overlay:not(.wec-modal-overlay--closing)");
    }

    function onTargetClick() {
        if (passThrough && dotnet) {
            dotnet.invokeMethodAsync("OnTargetClicked").catch(function () { });
        }
    }

    function detach() {
        if (targetEl) {
            targetEl.removeEventListener("click", onTargetClick, true);
        }

        targetEl = null;
    }

    function hideRing() {
        ring.style.display = "none";
        lastKey = "";
    }

    function place(el) {
        const r = el.getBoundingClientRect();
        const x = Math.max(0, r.left - PAD);
        const y = Math.max(0, r.top - PAD);
        const w = r.width + PAD * 2;
        const h = r.height + PAD * 2;
        const key = [x, y, w, h].join(",");

        if (key === lastKey) {
            return;
        }

        lastKey = key;
        ring.style.display = "block";
        ring.style.left = x + "px";
        ring.style.top = y + "px";
        ring.style.width = w + "px";
        ring.style.height = h + "px";
    }

    function sideFor(el) {
        const r = el.getBoundingClientRect();
        return r.left + r.width / 2 < window.innerWidth / 2 ? "right" : "left";
    }

    function tick() {
        const el = query(selector);

        if (el !== targetEl) {
            detach();
            targetEl = el;

            if (el) {
                el.addEventListener("click", onTargetClick, true);
                el.scrollIntoView({ block: "center", inline: "nearest" });
            }
        }

        if (el && isVisible(el)) {
            if (dialogOpen()) {
                hideRing();
            } else {
                place(el);
            }

            if (pending) {
                pending(sideFor(el));
                pending = null;
            }
        } else {
            hideRing();

            // The page may still be loading its data; give up quietly and let the engineer just talk.
            if (pending && Date.now() - started > WAIT_MS) {
                pending(null);
                pending = null;
            }
        }
    }

    function stopTimer() {
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
    }

    // Rings the element for the selector (nothing when there is none). Resolves with the side of the window
    // the engineer should stand on, or null if the element never showed up.
    function focus(sel, pass, ref) {
        ensureLayer();
        stopTimer();
        detach();

        if (pending) {
            pending(null);
            pending = null;
        }

        selector = sel || null;
        passThrough = !!pass;
        dotnet = ref || null;
        started = Date.now();
        hideRing();

        if (!selector) {
            return Promise.resolve("right");
        }

        // Keep following the layout so a page that is still loading, or a resized window, stays in step.
        timer = setInterval(tick, TICK_MS);

        return new Promise(function (resolve) {
            pending = resolve;
            tick();
        });
    }

    function clear() {
        stopTimer();
        detach();

        if (pending) {
            pending(null);
            pending = null;
        }

        if (layer && layer.parentNode) {
            layer.parentNode.removeChild(layer);
        }

        layer = ring = null;
        selector = null;
        dotnet = null;
        lastKey = "";
    }

    return { focus: focus, clear: clear };
})();

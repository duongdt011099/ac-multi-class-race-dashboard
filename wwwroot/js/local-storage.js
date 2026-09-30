window.wecLocalStorage = (function () {
    function get(key) {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    }

    function set(key, value) {
        try {
            window.localStorage.setItem(key, value);
        } catch {
        }
    }

    function remove(key) {
        try {
            window.localStorage.removeItem(key);
        } catch {
        }
    }

    return {
        get: get,
        set: set,
        remove: remove
    };
})();

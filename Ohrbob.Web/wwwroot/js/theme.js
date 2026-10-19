// Theme resolution for ohrbob.web (issue #7).
//
// The theme follows the browser/OS preference by default and can be overridden
// per device with the `ohrbob_theme` cookie (values: system | light | dark).
// Loaded synchronously from <head> so the correct theme is applied before the
// body paints (no flash of the wrong theme).
(function () {
    'use strict';

    var COOKIE = 'ohrbob_theme';
    var VALID = ['system', 'light', 'dark'];
    var mql = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    function readCookie() {
        var parts = document.cookie ? document.cookie.split('; ') : [];
        for (var i = 0; i < parts.length; i++) {
            var idx = parts[i].indexOf('=');
            if (idx > -1 && parts[i].slice(0, idx) === COOKIE) {
                return decodeURIComponent(parts[i].slice(idx + 1));
            }
        }
        return null;
    }

    function writeCookie(value) {
        document.cookie = COOKIE + '=' + encodeURIComponent(value) +
            '; path=/; max-age=31536000; samesite=lax';
    }

    function systemTheme() {
        return mql && mql.matches ? 'dark' : 'light';
    }

    // The theme the user saved (falls back to "system" for missing/garbage values).
    function getTheme() {
        var value = readCookie();
        return VALID.indexOf(value) > -1 ? value : 'system';
    }

    // The theme actually shown: "system" resolves against the OS preference.
    function resolveTheme() {
        var mode = getTheme();
        return mode === 'system' ? systemTheme() : mode;
    }

    function apply() {
        document.documentElement.setAttribute('data-bs-theme', resolveTheme());
    }

    function setTheme(mode) {
        if (VALID.indexOf(mode) < 0) mode = 'system';
        writeCookie(mode);
        apply();
    }

    // Follow OS changes live while the user is on "system".
    if (mql) {
        var onChange = function () { if (getTheme() === 'system') apply(); };
        if (mql.addEventListener) mql.addEventListener('change', onChange);
        else if (mql.addListener) mql.addListener(onChange);
    }

    window.ohrbobGetTheme = getTheme;
    window.ohrbobResolveTheme = resolveTheme;
    window.ohrbobApplyTheme = apply;
    window.ohrbobSetTheme = setTheme;

    apply();
})();

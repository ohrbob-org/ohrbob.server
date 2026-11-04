(function () {
    function init() {
        var input = document.getElementById('discover-search');
        var lanes = document.getElementById('recommendation-lanes');
        if (!input || !lanes) return;

        var sync = function () {
            lanes.classList.toggle('d-none', input.value.trim().length > 0);
        };

        input.addEventListener('input', sync);
        input.addEventListener('search', sync);
        sync();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // HTMX boosted navigation replaces the body without firing DOMContentLoaded.
    if (!window.__discoverLanesBound) {
        window.__discoverLanesBound = true;
        document.body.addEventListener('htmx:afterSwap', init);
    }
})();

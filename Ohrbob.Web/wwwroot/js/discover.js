(function () {
    function closeOpenCards(except) {
        document.querySelectorAll('.podcast-card.is-open').forEach(function (card) {
            if (card === except) return;
            card.classList.remove('is-open');
            var toggle = card.querySelector('.podcast-details-toggle');
            if (toggle) toggle.setAttribute('aria-expanded', 'false');
        });
    }

    function initCardToggles() {
        if (window.__discoverCardsBound) return;
        window.__discoverCardsBound = true;

        document.addEventListener('click', function (event) {
            var card = event.target.closest('.podcast-card');
            if (!card) {
                closeOpenCards(null);
                return;
            }

            var toggle = event.target.closest('.podcast-details-toggle');
            if (!toggle) {
                // Clicking anywhere on the card body also toggles, except real controls.
                if (event.target.closest('button, a, input, label, select, textarea')) return;
                toggle = card.querySelector('.podcast-details-toggle');
            }
            if (!toggle) return;

            var isOpen = card.classList.toggle('is-open');
            toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            closeOpenCards(card);
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') closeOpenCards(null);
        });
    }

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

    initCardToggles();

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

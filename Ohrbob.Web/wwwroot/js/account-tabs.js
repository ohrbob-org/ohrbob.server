// Account page: card overview first, then details with a Back button.
(function () {
    function showDetails() {
        const nav = document.getElementById('accountTabsNav');
        const content = document.getElementById('accountTabsContent');
        if (!nav || !content) return;
        nav.classList.add('d-none');
        content.classList.remove('d-none');
    }

    function showList() {
        const nav = document.getElementById('accountTabsNav');
        const content = document.getElementById('accountTabsContent');
        if (!nav || !content) return;
        nav.classList.remove('d-none');
        content.classList.add('d-none');
    }

    // Delegated so it keeps working after HTMX boosted navigation swaps the page.
    document.addEventListener('click', function (event) {
        if (event.target.closest('.account-tab-link')) {
            showDetails();
            return;
        }
        if (event.target.closest('#accountTabsBack')) {
            showList();
        }
    });
})();

(function () {
    function selectColor(picker, color, custom) {
        picker.querySelector('[data-color-value]').value = color;
        picker.querySelector('[data-color-preview]').style.backgroundColor = color;

        picker.querySelectorAll('.profile-color-swatch').forEach(function (swatch) {
            swatch.classList.toggle('is-selected', !custom && swatch.dataset.color === color);
        });

        var customSwatch = picker.querySelector('.profile-color-custom');
        if (customSwatch) customSwatch.classList.toggle('is-selected', custom);
    }

    document.addEventListener('click', function (event) {
        var swatch = event.target.closest('[data-color]');
        if (!swatch) return;
        var picker = swatch.closest('[data-profile-color]');
        if (!picker) return;
        selectColor(picker, swatch.dataset.color, false);
    });

    document.addEventListener('input', function (event) {
        var input = event.target.closest('[data-color-custom]');
        if (!input) return;
        var picker = input.closest('[data-profile-color]');
        if (!picker) return;
        selectColor(picker, input.value, true);
    });

    // The nav lives outside the HTMX-swapped content, so refresh its avatars from the cards.
    function syncNavColors() {
        document.querySelectorAll('#profileDropdown [data-profile-id]').forEach(function (avatar) {
            var card = document.querySelector('.profile-avatar[data-profile-id="' + avatar.dataset.profileId + '"]');
            if (card) avatar.style.backgroundColor = getComputedStyle(card).backgroundColor;
        });
    }

    document.body.addEventListener('htmx:afterSwap', syncNavColors);
})();

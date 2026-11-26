(function () {
    // White or navy initial for a profile colour, mirroring ProfileColorInk on the server.
    function inkFor(color) {
        var match = /^#([0-9a-f]{6})$/i.exec(color || '');
        if (!match) return '';
        var rgb = parseInt(match[1], 16);
        var r = (rgb >> 16) & 0xff;
        var g = (rgb >> 8) & 0xff;
        var b = rgb & 0xff;
        var luma = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
        return luma > 0.55 ? 'var(--metro-navy)' : 'var(--metro-on-accent)';
    }

    function selectColor(picker, color, custom) {
        picker.querySelector('[data-color-value]').value = color;
        var preview = picker.querySelector('[data-color-preview]');
        preview.style.backgroundColor = color;
        preview.style.color = inkFor(color);

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
            // The header avatar also uses .profile-avatar now, so only read the
            // colour from the swapped page content, never from the header itself.
            var card = document.querySelector('#main-content .profile-avatar[data-profile-id="' + avatar.dataset.profileId + '"]');
            if (card) avatar.style.backgroundColor = getComputedStyle(card).backgroundColor;
        });
    }

    document.body.addEventListener('htmx:afterSwap', syncNavColors);
})();

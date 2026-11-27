// Account page: card overview first, then details with a Back button; the
// connection QR modal and clipboard actions live here so the view has no
// inline behaviour.
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
            return;
        }

        const qrButton = event.target.closest('[data-qr-deep-link]');
        if (qrButton) {
            showConnectionQr(qrButton.getAttribute('data-qr-key-name'),
                qrButton.getAttribute('data-qr-deep-link'));
            return;
        }

        const confirmButton = event.target.closest('[data-confirm-form]');
        if (confirmButton) {
            const form = document.getElementById(confirmButton.getAttribute('data-confirm-form'));
            if (form) {
                window.showConfirmationModal(confirmButton.getAttribute('data-confirm-message'), function () {
                    form.requestSubmit();
                });
            }
        }
    });

    function showToast(message, type) {
        if (!message) return;
        document.body.dispatchEvent(new CustomEvent('showToast', {
            detail: { message: message, type: type || 'success' }
        }));
    }

    // Server-rendered notices become transient toasts instead of page banners.
    function showPendingToasts() {
        const pending = document.querySelectorAll('[data-toast-message]');
        pending.forEach(function (el, index) {
            const message = el.getAttribute('data-toast-message');
            const type = el.getAttribute('data-toast-type') || 'success';
            el.remove();
            if (!message) return;
            if (index === 0) showToast(message, type);
            else setTimeout(function () { showToast(message, type); }, index * 250);
        });
    }

    function showConnectionQr(keyName, deepLink) {
        document.getElementById('qrKeyName').textContent = keyName;
        document.getElementById('qrDeepLink').textContent = deepLink;

        let serverUrl = '';
        let token = '';
        try {
            const queryStr = deepLink.split('?')[1];
            if (queryStr) {
                const pairs = queryStr.split('&');
                for (const pair of pairs) {
                    const parts = pair.split('=');
                    if (parts[0] === 'server') serverUrl = decodeURIComponent(parts[1] || '');
                    if (parts[0] === 'token') token = decodeURIComponent(parts[1] || '');
                }
            }
        } catch (e) {
            console.error('Failed to parse deep link parameters:', e);
        }

        document.getElementById('qrServerUrl').value = serverUrl;
        document.getElementById('qrToken').value = token;

        const container = document.getElementById('qrcode-container');
        container.innerHTML = '';

        if (typeof QRCode !== 'undefined') {
            new QRCode(container, {
                text: deepLink,
                width: 200,
                height: 200,
                colorDark: '#000000',
                colorLight: '#ffffff',
                correctLevel: QRCode.CorrectLevel.M
            });
        }

        const modal = new bootstrap.Modal(document.getElementById('qrCodeModal'));
        modal.show();
    }
    window.showConnectionQr = showConnectionQr;

    // Clipboard buttons never navigate, so they are plain type=button controls.
    document.addEventListener('click', function (event) {
        const button = event.target.closest('[data-copy-target]');
        if (!button) return;
        const input = document.getElementById(button.getAttribute('data-copy-target'));
        if (!input) return;

        const message = button.getAttribute('data-copy-message') || 'Copied to clipboard.';
        const fallback = function () {
            input.select();
            if (document.execCommand) document.execCommand('copy');
            showToast(message, 'success');
        };

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(input.value).then(function () {
                showToast(message, 'success');
            }).catch(fallback);
        } else {
            fallback();
        }
    });

    // A freshly created API key opens its QR modal on arrival (TempData values
    // are rendered onto the modal as data attributes by the view).
    function openInitialQr() {
        const modal = document.getElementById('qrCodeModal');
        if (!modal || typeof bootstrap === 'undefined') return;
        const name = modal.getAttribute('data-initial-key-name');
        const link = modal.getAttribute('data-initial-deep-link');
        if (!name || !link) return;
        setTimeout(function () { showConnectionQr(name, link); }, 100);
    }

    function init() {
        showPendingToasts();
        openInitialQr();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();

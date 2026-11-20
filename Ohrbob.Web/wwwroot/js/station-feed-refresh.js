// Per-feed "Refresh" action in the station editor's three-dot menu.
//
// Bound once on document (not per card) so it keeps working after the feed list is
// re-rendered by window.refreshStationEdit() and after htmx history restores. Fetching
// a feed takes a moment, so the icon spins while the request runs; the card is then
// re-read from the server. Playback is untouched because this is plain fetch, not a
// page navigation.
(function () {
    if (window.__ohrbobFeedRefreshBound) return;
    window.__ohrbobFeedRefreshBound = true;

    function dispatchHxTrigger(header) {
        if (!header) return;
        try {
            const events = JSON.parse(header);
            for (const [name, detail] of Object.entries(events)) {
                document.body.dispatchEvent(new CustomEvent(name, { detail: detail }));
            }
        } catch (e) {
            console.error('Error parsing HX-Trigger header:', e);
        }
    }

    document.addEventListener('click', async function (e) {
        const button = e.target.closest('.refresh-feed');
        if (!button) return;
        e.preventDefault();

        const item = button.closest('.rotation-item');
        const container = button.closest('#rotationContainer');
        const feedId = item && item.getAttribute('data-feed-id');
        const stationId = container && container.getAttribute('data-station-id');
        if (!feedId || !stationId || button.disabled) return;

        const icon = button.querySelector('i');
        button.disabled = true;
        if (icon) icon.classList.add('ohrbob-spin');

        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]');
            const response = await fetch(
                '/Stations/RefreshFeed?id=' + encodeURIComponent(stationId) +
                '&feedId=' + encodeURIComponent(feedId),
                {
                    method: 'POST',
                    headers: token ? { 'RequestVerificationToken': token.value } : {}
                });

            dispatchHxTrigger(response.headers.get('HX-Trigger'));

            if (!response.ok && !response.headers.get('HX-Trigger')) {
                document.body.dispatchEvent(new CustomEvent('showToast', {
                    detail: { message: 'The podcast could not be refreshed.', type: 'danger' }
                }));
            }

            // Even a failed fetch reschedules the feed, so re-read the card either way.
            if (typeof window.refreshStationEdit === 'function') {
                await window.refreshStationEdit();
            }
        } catch (err) {
            console.error('Error refreshing feed:', err);
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: 'Network error refreshing the podcast.', type: 'danger' }
            }));
        } finally {
            // The card may have been replaced by refreshStationEdit, so the original
            // button can be detached; restoring it is harmless either way.
            button.disabled = false;
            if (icon) icon.classList.remove('ohrbob-spin');
        }
    });

    // Small indeterminate spinner for Bootstrap Icons inside the refresh action.
    if (!document.getElementById('ohrbob-spin-style')) {
        const style = document.createElement('style');
        style.id = 'ohrbob-spin-style';
        style.textContent = '@keyframes ohrbob-spin{to{transform:rotate(360deg)}}' +
            '.ohrbob-spin{display:inline-block;animation:ohrbob-spin 1s linear infinite}';
        document.head.appendChild(style);
    }
})();

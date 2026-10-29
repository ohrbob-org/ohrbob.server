(function () {
    const getToken = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    const parsePosition = (value) => {
        const position = parseInt(value, 10);
        return Number.isFinite(position) && position > 0 ? position : 0;
    };

    const showError = (message) => document.body.dispatchEvent(new CustomEvent('showToast', {
        detail: { message, type: 'danger' }
    }));

    async function savePlayhead(stationId, episodeId, positionSeconds) {
        const response = await fetch(
            `/Stations/SetPlayhead?id=${encodeURIComponent(stationId)}&episodeId=${encodeURIComponent(episodeId)}&positionSeconds=${positionSeconds}`,
            { method: 'POST', headers: { 'RequestVerificationToken': getToken() } });

        if (!response.ok) throw new Error('Request failed');

        if (typeof window._ohrbobSeekIfCurrent === 'function') {
            window._ohrbobSeekIfCurrent(episodeId, positionSeconds);
        }
        if (typeof window.refreshStationEdit === 'function') {
            await window.refreshStationEdit();
        }
    }

    document.addEventListener('click', function (e) {
        const continueBtn = e.target.closest('.continue-episode-btn');
        if (continueBtn) {
            if (typeof window._ohrbobContinueEpisode === 'function') {
                window._ohrbobContinueEpisode(
                    continueBtn.getAttribute('data-station-id'),
                    continueBtn.getAttribute('data-episode-id'),
                    parsePosition(continueBtn.getAttribute('data-position')));
            }
            return;
        }

        const resetBtn = e.target.closest('.reset-playhead-btn');
        if (resetBtn) {
            savePlayhead(
                resetBtn.getAttribute('data-station-id'),
                resetBtn.getAttribute('data-episode-id'),
                0).catch(() => showError('Could not reset the playhead.'));
            return;
        }

        const setBtn = e.target.closest('.set-playhead-btn');
        if (setBtn) {
            const modalEl = document.getElementById('setPlayheadModal');
            const input = document.getElementById('playheadSecondsInput');
            if (!modalEl || !input) return;
            input.value = parsePosition(setBtn.getAttribute('data-position'));
            input.setAttribute('data-station-id', setBtn.getAttribute('data-station-id'));
            input.setAttribute('data-episode-id', setBtn.getAttribute('data-episode-id'));
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
            return;
        }

        if (e.target.closest('#confirmSetPlayheadBtn')) {
            const input = document.getElementById('playheadSecondsInput');
            if (!input) return;
            bootstrap.Modal.getInstance(document.getElementById('setPlayheadModal'))?.hide();
            savePlayhead(
                input.getAttribute('data-station-id'),
                input.getAttribute('data-episode-id'),
                parsePosition(input.value)).catch(() => showError('Could not set the playhead.'));
        }
    });
})();

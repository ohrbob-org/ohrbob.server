// OPML import for the station edit view (Views/Stations/Edit.cshtml).
// Keeps playback untouched: it uploads the file over fetch and refreshes the
// rotation list via window.refreshStationEdit() instead of navigating.
window.initOpmlImport = function (stationId) {
    const btn = document.getElementById('importOpmlBtn');
    const modalEl = document.getElementById('importOpmlModal');
    const fileInput = document.getElementById('opmlFileInput');
    const errorMsg = document.getElementById('opmlErrorMsg');
    const submitBtn = document.getElementById('submitOpmlBtn');
    if (!btn || !modalEl || !fileInput || !submitBtn) return;

    const showFileError = (message) => {
        fileInput.classList.add('is-invalid');
        if (errorMsg) {
            errorMsg.textContent = message;
            errorMsg.classList.remove('d-none');
        }
    };

    const clearFileError = () => {
        fileInput.classList.remove('is-invalid');
        if (errorMsg) errorMsg.classList.add('d-none');
    };

    btn.addEventListener('click', function () {
        fileInput.value = '';
        clearFileError();
        submitBtn.disabled = false;
        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    });

    submitBtn.addEventListener('click', function () {
        const file = fileInput.files && fileInput.files[0];
        if (!file) {
            showFileError('Please choose an OPML file.');
            return;
        }

        clearFileError();
        submitBtn.disabled = true;

        const formData = new FormData();
        formData.append('opml', file);

        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';

        fetch(`/Stations/ImportOpml/${stationId}`, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: formData
        })
        .then(async response => {
            submitBtn.disabled = false;

            if (response.ok) {
                const hxTrigger = response.headers.get('HX-Trigger');
                if (hxTrigger) {
                    try {
                        const events = JSON.parse(hxTrigger);
                        for (const [evtName, detail] of Object.entries(events)) {
                            document.body.dispatchEvent(new CustomEvent(evtName, { detail: detail }));
                        }
                    } catch (e) {
                        console.error('Error parsing HX-Trigger header:', e);
                    }
                }

                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) modal.hide();

                if (typeof window.refreshStationEdit === 'function') {
                    window.refreshStationEdit();
                }
            } else {
                const text = await response.text();
                document.body.dispatchEvent(new CustomEvent('showToast', {
                    detail: { message: text || 'Failed to import OPML.', type: 'danger' }
                }));
            }
        })
        .catch(err => {
            submitBtn.disabled = false;
            console.error('Error importing OPML:', err);
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: 'Network error importing OPML.', type: 'danger' }
            }));
        });
    });
};

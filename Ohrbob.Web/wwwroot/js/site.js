// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
document.body.addEventListener('showToast', function (evt) {
    const toastEl = document.getElementById('liveToast');
    const toastBody = document.getElementById('toast-body-text');
    if (toastEl && toastBody && evt.detail && evt.detail.message) {
        toastBody.textContent = evt.detail.message;
        
        // Color based on type (success / danger)
        toastEl.classList.remove('bg-danger', 'bg-success');
        if (evt.detail.type === 'success') {
            toastEl.classList.add('bg-success');
        } else {
            toastEl.classList.add('bg-danger');
        }
        
        if (typeof bootstrap !== 'undefined' && bootstrap.Toast) {
            const toast = new bootstrap.Toast(toastEl);
            toast.show();
        }
    }
});

window.showConfirmationModal = function (message, onConfirm) {
    const modalEl = document.getElementById('confirmationModal');
    const bodyEl = document.getElementById('confirmationModalBody');
    const confirmBtn = document.getElementById('confirmationModalConfirmBtn');
    
    if (modalEl && bodyEl && confirmBtn) {
        bodyEl.textContent = message;
        
        // Remove existing event listeners from confirm button to avoid duplicates
        const newConfirmBtn = confirmBtn.cloneNode(true);
        confirmBtn.parentNode.replaceChild(newConfirmBtn, confirmBtn);
        
        const modal = new bootstrap.Modal(modalEl);
        
        newConfirmBtn.addEventListener('click', function () {
            modal.hide();
            onConfirm();
        });
        
        modal.show();
    }
};

window.toggleNewStationInput = function (safeId) {
    const check = document.getElementById(`create-new-${safeId}`);
    const container = document.getElementById(`new-station-name-container-${safeId}`);
    if (check && container) {
        container.style.display = check.checked ? 'block' : 'none';
    }
    window.validateFormSelection(safeId);
};

window.validateFormSelection = function (safeId) {
    const createNewCheck = document.getElementById(`create-new-${safeId}`);
    const stationChecks = Array.from(document.querySelectorAll(`#add-feed-form-${safeId} input[name=stationIds]:checked`));
    const confirmBtn = document.getElementById(`confirm-btn-${safeId}`);
    
    if (confirmBtn) {
        const isCreateNewChecked = createNewCheck && createNewCheck.checked;
        const isAnyStationChecked = stationChecks.length > 0;
        confirmBtn.disabled = !(isCreateNewChecked || isAnyStationChecked);
    }
};

window.submitAddFeedJson = function (event, safeId) {
    event.preventDefault();
    const form = document.getElementById(`add-feed-form-${safeId}`);
    if (!form) return;

    if (form.dataset.confirmed !== 'true') {
        const checkedBoxes = Array.from(form.querySelectorAll('input[name=stationIds]:checked'));
        const alreadyHas = [];
        const feedUrl = form.querySelector('input[name=feedUrl]').value;
        for (const cb of checkedBoxes) {
            const feedsAttr = cb.getAttribute('data-feeds') || '';
            const feeds = feedsAttr.split(';');
            if (feeds.includes(feedUrl)) {
                alreadyHas.push(cb.nextElementSibling.textContent.trim());
            }
        }
        if (alreadyHas.length > 0) {
            const msg = alreadyHas.length === 1 
                ? `The station '${alreadyHas[0]}' already has this podcast. Are you sure you want to add it again?`
                : `The stations ${alreadyHas.map(s => `'${s}'`).join(', ')} already have this podcast. Are you sure you want to add it again?`;
            
            window.showConfirmationModal(msg, () => {
                form.dataset.confirmed = 'true';
                window.submitAddFeedJson(event, safeId);
            });
            return;
        }
    }

    // Gather inputs
    const feedUrl = form.querySelector('input[name=feedUrl]').value;
    const feedTitle = form.querySelector('input[name=feedTitle]').value;
    const createNewStation = form.querySelector(`input[name=createNewStation]`)?.checked || false;
    const newStationName = form.querySelector(`input[name=newStationName]`)?.value || '';
    const stationIds = Array.from(form.querySelectorAll('input[name=stationIds]:checked')).map(cb => cb.value);

    const payload = {
        feedUrl: feedUrl,
        feedTitle: feedTitle,
        stationIds: stationIds,
        createNewStation: createNewStation,
        newStationName: newStationName
    };

    const confirmBtn = document.getElementById(`confirm-btn-${safeId}`);
    if (confirmBtn) confirmBtn.disabled = true;

    fetch('/Discover/AddFeed', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(payload)
    })
    .then(async response => {
        if (response.ok) {
            // Process standard HX-Trigger header for toasts
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

            // Close modal
            const modalEl = document.getElementById(`add-to-station-modal-${safeId}`);
            if (modalEl) {
                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) {
                    modal.hide();
                }
            }
        } else {
            const text = await response.text();
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: text || 'Failed to add feed.', type: 'danger' }
            }));
            if (confirmBtn) confirmBtn.disabled = false;
        }
    })
    .catch(err => {
        console.error('Error adding feed:', err);
        document.body.dispatchEvent(new CustomEvent('showToast', {
            detail: { message: 'Network error adding feed.', type: 'danger' }
        }));
        if (confirmBtn) confirmBtn.disabled = false;
    });
};

window.initRenameStation = function (stationId, initialIsAging) {
    const renameBtn = document.getElementById('renameStationBtn');
    const modalEl = document.getElementById('renameStationModal');
    const saveBtn = document.getElementById('saveStationNameBtn');
    const nameInput = document.getElementById('newStationNameInput');
    const errorMsg = document.getElementById('renameErrorMsg');
    const nameDisplay = document.getElementById('stationNameDisplay');
    const isAgingInput = document.getElementById('isAgingInput');
    const isPublicInput = document.getElementById('isPublicInput');

    if (!renameBtn || !modalEl || !saveBtn || !nameInput || !errorMsg || !nameDisplay || !isAgingInput || !isPublicInput) return;

    let modal = null;
    let currentIsAging = initialIsAging;

    renameBtn.addEventListener('click', function () {
        nameInput.value = nameDisplay.textContent.trim();
        isAgingInput.checked = currentIsAging;
        nameInput.classList.remove('is-invalid');
        errorMsg.classList.add('d-none');
        if (!modal) {
            modal = new bootstrap.Modal(modalEl);
        }
        modal.show();
    });

    nameInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            saveBtn.click();
        }
    });

    saveBtn.addEventListener('click', function () {
        const newName = nameInput.value.trim();
        if (!newName) {
            nameInput.classList.add('is-invalid');
            errorMsg.classList.remove('d-none');
            return;
        }

        nameInput.classList.remove('is-invalid');
        errorMsg.classList.add('d-none');
        saveBtn.disabled = true;

        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';

        const formData = new FormData();
        formData.append('name', newName);
        formData.append('isAging', isAgingInput.checked);
        formData.append('isPublic', isPublicInput.checked);

        fetch(`/Stations/UpdateSettings/${stationId}`, {
            method: 'POST',
            headers: {
                'RequestVerificationToken': token
            },
            body: formData
        })
        .then(async response => {
            saveBtn.disabled = false;
            if (response.ok) {
                const data = await response.json();
                nameDisplay.textContent = data.name;
                currentIsAging = data.isAging;
                if (typeof window.setStationPublicState === 'function') {
                    window.setStationPublicState(data.isPublic);
                }
                document.title = `Edit Station - ${data.name} | ohrbob`;
                if (modal) modal.hide();

                document.body.dispatchEvent(new CustomEvent('showToast', {
                    detail: { message: 'Station settings updated successfully.', type: 'success' }
                }));

                // Auto-refresh the playlist view dynamically using reloadPlaylist defined in Edit.cshtml
                if (typeof reloadPlaylist === 'function') {
                    reloadPlaylist();
                } else {
                    // Try reload playlist from page contextual closures
                    const container = document.getElementById('resolved');
                    if (container) {
                        // Find any defined function or simply fetch and update resolved container manually
                        fetch(window.location.href, { cache: 'no-store' })
                        .then(res => res.text())
                        .then(html => {
                            const parser = new DOMParser();
                            const doc = parser.parseFromString(html, 'text/html');
                            const newResolved = doc.getElementById('resolved');
                            if (newResolved) {
                                container.innerHTML = newResolved.innerHTML;
                            }
                        });
                    }
                }
            } else {
                const text = await response.text();
                document.body.dispatchEvent(new CustomEvent('showToast', {
                    detail: { message: text || 'Failed to update station settings.', type: 'danger' }
                }));
            }
        })
        .catch(err => {
            saveBtn.disabled = false;
            console.error('Error updating station settings:', err);
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: 'Network error updating station settings.', type: 'danger' }
            }));
        });
    });

    const deleteBtn = document.getElementById('deleteStationBtn');
    const deleteForm = document.getElementById('delete-station-form-edit');
    if (deleteBtn && deleteForm) {
        deleteBtn.addEventListener('click', function () {
            if (modal) modal.hide();
            // Wait until the settings modal is fully closed before showing the confirmation on top
            modalEl.addEventListener('hidden.bs.modal', function () {
                window.showConfirmationModal('Delete this station?', function () {
                    if (typeof window.checkDeletePlayingStation === 'function') {
                        window.checkDeletePlayingStation(stationId);
                    }
                    deleteForm.requestSubmit();
                });
            }, { once: true });
        });
    }
};

window.initShareBar = function (stationId) {
    const urlInput = document.getElementById('shareStationUrl');
    const visibilitySelect = document.getElementById('shareVisibilitySelect');
    const copyBtn = document.getElementById('copyShareLinkBtn');

    if (!urlInput || !visibilitySelect || !copyBtn) return;

    let previousVisibility = visibilitySelect.value;

    const applyState = function (value) {
        const isPublic = Boolean(value);
        visibilitySelect.value = isPublic ? 'public' : 'private';
        const switchInput = document.getElementById('isPublicInput');
        if (switchInput) switchInput.checked = isPublic;
        previousVisibility = visibilitySelect.value;
    };

    window.setStationPublicState = applyState;

    visibilitySelect.addEventListener('change', function () {
        const wanted = visibilitySelect.value;
        const name = document.getElementById('stationNameDisplay')?.textContent.trim() || '';
        const isAging = document.getElementById('isAgingInput')?.checked || false;
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';

        const formData = new FormData();
        formData.append('name', name);
        formData.append('isAging', isAging);
        formData.append('isPublic', wanted === 'public');
        visibilitySelect.disabled = true;

        fetch(`/Stations/UpdateSettings/${stationId}`, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: formData
        })
        .then(async response => {
            visibilitySelect.disabled = false;
            if (!response.ok) throw new Error(await response.text());
            const data = await response.json();
            applyState(data.isPublic);
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: {
                    message: data.isPublic ? 'Station is now public.' : 'Station is now private.',
                    type: 'success'
                }
            }));
        })
        .catch(() => {
            visibilitySelect.disabled = false;
            visibilitySelect.value = previousVisibility;
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: 'Could not update station visibility.', type: 'danger' }
            }));
        });
    });

    copyBtn.addEventListener('click', async function () {
        urlInput.select();
        try {
            await navigator.clipboard.writeText(urlInput.value);
        } catch {
            document.execCommand('copy');
        }
        document.body.dispatchEvent(new CustomEvent('showToast', {
            detail: { message: 'Share link copied.', type: 'success' }
        }));
    });
};

window.initFeedMenus = function () {
    // Use Popper's fixed strategy so the menu escapes the card's / container's
    // overflow clipping (the feed card and #rotationContainer both clip).
    if (typeof bootstrap === 'undefined' || !bootstrap.Dropdown) return;
    document.querySelectorAll('#rotationContainer .feed-actions-menu').forEach(function (el) {
        bootstrap.Dropdown.getOrCreateInstance(el, { popperConfig: { strategy: 'fixed' } });
    });
};

window.initMoveFeed = function (sourceStationId) {
    window.initFeedMenus();
    const container = document.getElementById('rotationContainer');
    const modalEl = document.getElementById('moveFeedModal');
    const urlInput = document.getElementById('moveFeedUrl');
    const titleEl = document.getElementById('moveFeedTitle');
    const newNameContainer = document.getElementById('moveNewStationNameContainer');
    const newNameInput = document.getElementById('moveNewStationName');
    const confirmBtn = document.getElementById('confirmMoveFeedBtn');
    if (!container || !modalEl || !confirmBtn) return;

    let itemToMove = null;

    container.addEventListener('click', function (e) {
        const moveBtn = e.target.closest('.move-feed');
        if (!moveBtn) return;
        itemToMove = moveBtn.closest('.rotation-item');
        if (!itemToMove) return;

        urlInput.value = itemToMove.getAttribute('data-url') || '';
        const titleStrong = itemToMove.querySelector('strong');
        titleEl.textContent = titleStrong ? titleStrong.textContent.trim() : '';

        modalEl.querySelectorAll('input[name="moveTarget"]').forEach(r => { r.checked = false; });
        if (newNameInput) newNameInput.value = '';
        if (newNameContainer) newNameContainer.style.display = 'none';
        confirmBtn.disabled = false;

        bootstrap.Modal.getOrCreateInstance(modalEl).show();
    });

    confirmBtn.addEventListener('click', function () {
        const selected = modalEl.querySelector('input[name="moveTarget"]:checked');
        if (!selected) {
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: 'Please choose a destination station.', type: 'danger' }
            }));
            return;
        }

        const createNew = selected.value === '__new__';
        const payload = {
            sourceStationId: sourceStationId,
            feedUrl: urlInput.value,
            feedTitle: titleEl.textContent,
            targetStationId: createNew ? null : selected.value,
            createNewStation: createNew,
            newStationName: createNew && newNameInput ? newNameInput.value : null
        };

        confirmBtn.disabled = true;

        fetch('/Stations/MoveFeed', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify(payload)
        })
        .then(async response => {
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

                if (itemToMove) itemToMove.remove();
                itemToMove = null;

                const modal = bootstrap.Modal.getInstance(modalEl);
                if (modal) modal.hide();

                if (typeof window.refreshStationEdit === 'function') {
                    window.refreshStationEdit();
                }
            } else {
                const text = await response.text();
                confirmBtn.disabled = false;
                document.body.dispatchEvent(new CustomEvent('showToast', {
                    detail: { message: text || 'Failed to move feed.', type: 'danger' }
                }));
            }
        })
        .catch(err => {
            console.error('Error moving feed:', err);
            confirmBtn.disabled = false;
            document.body.dispatchEvent(new CustomEvent('showToast', {
                detail: { message: 'Network error moving feed.', type: 'danger' }
            }));
        });
    });
};

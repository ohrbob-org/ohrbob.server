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

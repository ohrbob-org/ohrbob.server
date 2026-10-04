document.addEventListener('DOMContentLoaded', () => {
    const widget = document.getElementById('ohrbob-player');
    if (!widget) return;

    const apiUrl = widget.getAttribute('data-player-api-url');
    const token = widget.getAttribute('data-stream-token');

    if (!apiUrl || !token) return;

    const headers = {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
    };

    let currentEpisodeData = null;
    let nextEpisodeData = null;
    let audio = new Audio();
    let syncInterval = null;

    // Helper to call player API
    const apiCall = async (endpoint, method = 'GET', body = null) => {
        try {
            const options = { method, headers };
            if (body) {
                options.body = JSON.stringify(body);
            }
            const response = await fetch(`${apiUrl}${endpoint}`, options);
            if (!response.ok) throw new Error('API Error');
            
            const text = await response.text();
            return text ? JSON.parse(text) : null;
        } catch (err) {
            console.error('Player API error:', err);
            return null;
        }
    };

    let activeStationId = null;

    const applyStateToWidget = (state) => {
        if (!state || !state.current) return;
        
        currentEpisodeData = state.current;
        nextEpisodeData = state.next;
        activeStationId = state.stationId;

        widget.setAttribute('station', state.stationName || 'Unknown Station');
        widget.setAttribute('track', currentEpisodeData.title || '');
        widget.setAttribute('artist', state.stationName || ''); // fallback artist
        if (currentEpisodeData.coverUrl) {
             widget.setAttribute('cover', currentEpisodeData.coverUrl);
        } else {
             widget.removeAttribute('cover');
        }
    };

    const showAutoplayToast = () => {
        // Create toast container if it doesn't exist
        let toastContainer = document.querySelector('.toast-container.autoplay-toast-container');
        if (!toastContainer) {
            toastContainer = document.createElement('div');
            // Remove bottom-0 so we can position it dynamically
            toastContainer.className = 'toast-container autoplay-toast-container position-fixed end-0 p-3';
            toastContainer.style.zIndex = '1055';
            
            // Position it just above the player widget
            const widgetHeight = widget.offsetHeight || 160; 
            // 20px is the bottom offset from CSS, plus a 10px gap
            toastContainer.style.bottom = (20 + widgetHeight + 10) + 'px';
            
            document.body.appendChild(toastContainer);
        }

        // Create the toast element
        const toastEl = document.createElement('div');
        toastEl.className = 'toast align-items-center text-bg-warning border-0';
        toastEl.setAttribute('role', 'alert');
        toastEl.setAttribute('aria-live', 'assertive');
        toastEl.setAttribute('aria-atomic', 'true');
        
        toastEl.innerHTML = `
          <div class="d-flex">
            <div class="toast-body">
              <strong>Autoplay blocked by browser.</strong><br/>
              Please click the play button in the widget to start listening.
            </div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
          </div>
        `;
        
        toastContainer.appendChild(toastEl);
        
        // Initialize and show via Bootstrap
        const toast = new bootstrap.Toast(toastEl, { autohide: true, delay: 10000 });
        toast.show();
        
        // Clean up DOM after it hides
        toastEl.addEventListener('hidden.bs.toast', () => {
            toastEl.remove();
        });
    };

    const setupAudio = () => {
        if (!currentEpisodeData) return;
        
        // streamUri is now an absolute URL directly to the podcast audio file
        const fullStreamUri = currentEpisodeData.streamUri; 
        
        // If it's a new track, load it
        if (audio.src !== fullStreamUri) {
            audio.src = fullStreamUri;
            audio.currentTime = currentEpisodeData.playheadPositionSeconds || 0;
            audio.play().then(() => {
                widget.isPlaying = true;
                updatePlayButton();
            }).catch(e => {
                console.log('Autoplay blocked by browser', e);
                widget.isPlaying = false;
                updatePlayButton();
                showAutoplayToast();
            });
        }
    };

    const syncState = async () => {
        const state = await apiCall('/stream/status');
        if (state && state.current) {
            applyStateToWidget(state);
            setupAudio();
        }
    };

    const reportPlayhead = async () => {
        if (!widget.isPlaying || !currentEpisodeData || !activeStationId) return;
        
        // Currently playing
        await apiCall('/stream/status', 'POST', {
            episodeId: currentEpisodeData.episodeId,
            stationId: activeStationId,
            positionSeconds: Math.floor(audio.currentTime)
        });
    };

    const updatePlayButton = () => {
        const btnPlay = widget.querySelector('.js-play');
        if (btnPlay) {
            const playSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`;
            const pauseSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`;
            btnPlay.innerHTML = widget.isPlaying ? pauseSvg : playSvg;
            btnPlay.style.paddingLeft = widget.isPlaying ? '0' : '2px';
        }
    };

    // Track transition handling
    audio.onended = async () => {
        if (nextEpisodeData) {
            // Optimistic update
            currentEpisodeData = nextEpisodeData;
            widget.setAttribute('track', currentEpisodeData.title);
            if (currentEpisodeData.coverUrl) widget.setAttribute('cover', currentEpisodeData.coverUrl);
            
            const fullStreamUri = currentEpisodeData.streamUri;
            audio.src = fullStreamUri;
            audio.currentTime = 0;
            audio.play();
            
            // Advance on server
            await apiCall('/stream/next', 'POST');
            
            // Fetch fresh status to get the *new* next track
            const state = await apiCall('/stream/status');
            if (state) applyStateToWidget(state);
        } else {
            widget.isPlaying = false;
            updatePlayButton();
        }
    };

    // Bind widget events to Audio
    widget.addEventListener('play', () => {
        audio.play();
        widget.isPlaying = true;
    });
    
    widget.addEventListener('pause', () => {
        audio.pause();
        widget.isPlaying = false;
    });
    
    widget.addEventListener('forward', async () => {
        await apiCall('/stream/next', 'POST');
        syncState();
    });

    widget.addEventListener('settings', () => {
        if (activeStationId) {
            window.location.href = `/Stations/Edit/${activeStationId}`;
        }
    });

    widget.addEventListener('seek', (e) => {
        const time = e.detail.time;
        if (Number.isFinite(time)) {
            audio.currentTime = time;
        }
    });

    widget.addEventListener('volumechange', (e) => {
        const vol = e.detail.volume;
        if (Number.isFinite(vol)) {
            audio.volume = vol;
        }
    });

    widget.addEventListener('mutechange', (e) => {
        audio.muted = Boolean(e.detail.muted);
    });

    widget.addEventListener('skipback', (e) => {
        audio.currentTime = Math.max(0, audio.currentTime - (e.detail.seconds || 15));
    });

    widget.addEventListener('skipforward', (e) => {
        audio.currentTime = Math.min(audio.duration || 0, audio.currentTime + (e.detail.seconds || 30));
    });

    // Handle station switching from the dashboard cards
    widget.playStation = async (stationId) => {
        console.log("Switching to station:", stationId);
        await apiCall(`/stream/play-station/${stationId}`, 'POST');
        // Let the state re-sync and start playing
        await syncState();
    };

    // Update widget duration & current time periodically from the actual audio element
    setInterval(() => {
        if (audio.readyState > 0 && widget.isPlaying) {
            if (widget.duration !== audio.duration && Number.isFinite(audio.duration)) {
                widget.duration = audio.duration;
            }
            if (widget.currentTime !== audio.currentTime && Number.isFinite(audio.currentTime)) {
                widget.currentTime = audio.currentTime;
            }
        }
    }, 500);

    // Initial sync
    syncState();
    
    // Heartbeat every 10 seconds
    syncInterval = setInterval(reportPlayhead, 10000);
});

(function() {
    function initPlayer() {
        const widget = document.getElementById('ohrbob-player');
        if (!widget) return;
        
        // Prevent multiple initialization
        if (widget._playerIntegrationInitialized) return;
        widget._playerIntegrationInitialized = true;

        const apiUrl = widget.getAttribute('data-player-api-url');
        const token = widget.getAttribute('data-stream-token');

        if (!apiUrl || !token) return;

        const headers = {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        };

        // We can attach `window._ohrbobAudio` so we don't create multiple audio elements?
        // Actually, if HTMX restores the body, the old widget is destroyed.
        // But the old Audio object might still be playing!
        // To be safe, let's reuse window.ohrbobAudio if it exists, otherwise create it.
        if (!window._ohrbobAudio) {
            window._ohrbobAudio = new Audio();
        }
        let audio = window._ohrbobAudio;
        
        // clear old listeners if any (by replacing the audio object or just letting them be if we only create one? 
        // No, if we add multiple `ended` or `error` listeners, it's bad.
        // Let's recreate the audio element to be safe and pause the old one.
        if (window._ohrbobAudio && window._ohrbobAudio._managed) {
            window._ohrbobAudio.pause();
            window._ohrbobAudio.onended = null;
            window._ohrbobAudio.onerror = null;
        }
        audio = new Audio();
        audio._managed = true;
        window._ohrbobAudio = audio;

        let currentEpisodeData = null;
        let nextEpisodeData = null;
        let syncInterval = null;
        let lastConnectionErrorTime = 0;
        let activeStationId = null;
        let currentLoadedUri = null;

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
                
                if (err instanceof TypeError && err.message === 'Failed to fetch') {
                    const now = Date.now();
                    if (now - lastConnectionErrorTime > 30000) { 
                        showToast('Connection Error', 'Cannot reach the player API. Please check your connection or wait for the service to become available.', true);
                        lastConnectionErrorTime = now;
                    }
                }
                
                return null;
            }
        };

        const applyStateToWidget = (state) => {
            if (!state || !state.current) return;
            
            currentEpisodeData = state.current;
            nextEpisodeData = state.next;
            activeStationId = state.stationId;

            widget.setAttribute('station', state.stationName || 'Unknown Station');
            widget.setAttribute('track', currentEpisodeData.title || '');
            widget.setAttribute('artist', state.stationName || ''); 
            if (currentEpisodeData.coverUrl) {
                 widget.setAttribute('cover', currentEpisodeData.coverUrl);
            } else {
                 widget.removeAttribute('cover');
            }
        };

        const showToast = (title, message, isError = false) => {
            let toastContainer = document.querySelector('.toast-container.player-toast-container');
            if (!toastContainer) {
                toastContainer = document.createElement('div');
                toastContainer.className = 'toast-container player-toast-container position-fixed end-0 p-3';
                toastContainer.style.zIndex = '1055';
                
                const widgetHeight = widget.offsetHeight || 160; 
                toastContainer.style.bottom = (20 + widgetHeight + 10) + 'px';
                
                document.body.appendChild(toastContainer);
            }

            const toastEl = document.createElement('div');
            const bgClass = isError ? 'text-bg-danger' : 'text-bg-warning';
            toastEl.className = `toast align-items-center ${bgClass} border-0`;
            toastEl.setAttribute('role', 'alert');
            toastEl.setAttribute('aria-live', 'assertive');
            toastEl.setAttribute('aria-atomic', 'true');
            
            toastEl.innerHTML = `
              <div class="d-flex">
                <div class="toast-body">
                  <strong>${title}</strong><br/>
                  ${message}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
              </div>
            `;
            
            toastContainer.appendChild(toastEl);
            
            const toast = new bootstrap.Toast(toastEl, { autohide: true, delay: 10000 });
            toast.show();
            
            toastEl.addEventListener('hidden.bs.toast', () => {
                toastEl.remove();
            });
        };

        const showAutoplayToast = () => {
            showToast('Autoplay blocked by browser.', 'Please click the play button in the widget to start listening.', false);
        };

        audio.addEventListener('error', (e) => {
            console.error("Audio element error:", audio.error);
            widget.isPlaying = false;
            updatePlayButton();
        });

        const setupAudio = () => {
            if (!currentEpisodeData || !currentEpisodeData.streamUri) {
                return;
            }
            
            const fullStreamUri = currentEpisodeData.streamUri; 
            
            if (currentLoadedUri !== fullStreamUri || !audio.src || audio.src === window.location.href) {
                currentLoadedUri = fullStreamUri;
                audio.src = fullStreamUri;
                audio.load(); 
                audio.currentTime = currentEpisodeData.playheadPositionSeconds || 0;
                
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.then(() => {
                        widget.isPlaying = true;
                        updatePlayButton();
                    }).catch(e => {
                        widget.isPlaying = false;
                        updatePlayButton();
                        showAutoplayToast();
                    });
                }
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

        audio.onended = async () => {
            if (nextEpisodeData && nextEpisodeData.streamUri) {
                currentEpisodeData = nextEpisodeData;
                widget.setAttribute('track', currentEpisodeData.title);
                if (currentEpisodeData.coverUrl) widget.setAttribute('cover', currentEpisodeData.coverUrl);
                
                const fullStreamUri = currentEpisodeData.streamUri;
                currentLoadedUri = fullStreamUri;
                audio.src = fullStreamUri;
                audio.currentTime = 0;
                
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.catch(e => console.error('Play blocked or failed:', e));
                }
                
                await apiCall('/stream/next', 'POST');
                
                const state = await apiCall('/stream/status');
                if (state) applyStateToWidget(state);
            } else {
                widget.isPlaying = false;
                updatePlayButton();
            }
        };

        widget.addEventListener('play', () => {
            if (!currentEpisodeData || !currentEpisodeData.streamUri) {
                widget.isPlaying = false;
                updatePlayButton();
                return;
            }

            if (currentLoadedUri !== currentEpisodeData.streamUri || !audio.src || audio.src === window.location.href) {
                setupAudio();
            } else {
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.then(() => {
                        widget.isPlaying = true;
                    }).catch(e => {
                        widget.isPlaying = false;
                        updatePlayButton();
                    });
                }
            }
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

        widget.playStation = async (stationId) => {
            await apiCall(`/stream/play-station/${stationId}`, 'POST');
            await syncState();
        };

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

        syncState();
        
        syncInterval = setInterval(() => {
            if (widget.isPlaying && currentEpisodeData && activeStationId) {
                reportPlayhead();
            } else if (!currentEpisodeData) {
                syncState();
            }
        }, 10000);

        document.body.addEventListener('stationAdded', () => {
            if (currentEpisodeData) return; 
            
            let attempts = 0;
            const fastPoll = setInterval(async () => {
                attempts++;
                await syncState();
                if (currentEpisodeData || attempts >= 10) {
                    clearInterval(fastPoll);
                }
            }, 2000); 
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initPlayer);
    } else {
        initPlayer();
    }
})();

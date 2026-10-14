
(function() {
    function initPlayer() {
        let widget = document.getElementById('ohrbob-player');
        if (!widget) return;
        
        const apiUrl = widget.getAttribute('data-player-api-url');
        const token = widget.getAttribute('data-stream-token');

        if (!apiUrl || !token) return;

        const headers = {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        };

        if (!window._ohrbobAudio) {
            window._ohrbobAudio = new Audio();
            window._ohrbobAudio._managed = true;
        }
        let audio = window._ohrbobAudio;
        
        // Remove old event listeners
        audio.onended = null;
        audio.onerror = null;
        if (window._ohrbobSyncInterval) clearInterval(window._ohrbobSyncInterval);

        let currentEpisodeData = null;
        let nextEpisodeData = null;
        let prevEpisodeData = null;
        let lastConnectionErrorTime = 0;
        let activeStationId = null;
        const PREV_RESTART_THRESHOLD_SECONDS = 5;

        const toCard = (ep) => {
            if (!ep) return null;
            return {
                track: ep.title || 'Track',
                artist: ep.artist || '',
                cover: ep.coverUrl || null
            };
        };

        const applyEpisodeCards = () => {
            if (nextEpisodeData) {
                widget.setAttribute('next-episode', JSON.stringify(toCard(nextEpisodeData)));
            } else {
                widget.removeAttribute('next-episode');
            }
            if (prevEpisodeData) {
                widget.setAttribute('prev-episode', JSON.stringify(toCard(prevEpisodeData)));
            } else {
                widget.removeAttribute('prev-episode');
            }
        };

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
                return null;
            }
        };

        const updateMediaSessionMetadata = (state) => {
            if (!('mediaSession' in navigator)) return;

            if (!state || !state.stationId || state.isCompiling || state.isEmpty) {
                navigator.mediaSession.metadata = null;
                navigator.mediaSession.playbackState = 'none';
                return;
            }

            const current = state.current || {};
            navigator.mediaSession.metadata = new MediaMetadata({
                title: current.title || 'Track Name',
                artist: state.stationName || 'Ohrbob',
                album: state.stationName || 'Ohrbob',
                artwork: current.coverUrl ? [
                    { src: current.coverUrl, sizes: '96x96' },
                    { src: current.coverUrl, sizes: '128x128' },
                    { src: current.coverUrl, sizes: '192x192' },
                    { src: current.coverUrl, sizes: '256x256' },
                    { src: current.coverUrl, sizes: '384x384' },
                    { src: current.coverUrl, sizes: '512x512' }
                ] : []
            });
            updateMediaSessionPlaybackState();
        };

        const updateMediaSessionPlaybackState = () => {
            if (!('mediaSession' in navigator)) return;
            navigator.mediaSession.playbackState = !audio.paused ? 'playing' : 'paused';
        };

        const updateMediaSessionPosition = () => {
            if (!('mediaSession' in navigator) || !('setPositionState' in navigator.mediaSession)) return;
            if (!audio.duration || !isFinite(audio.duration) || !isFinite(audio.currentTime)) return;

            try {
                navigator.mediaSession.setPositionState({
                    duration: audio.duration,
                    playbackRate: audio.playbackRate || 1,
                    position: audio.currentTime
                });
            } catch (err) {
                console.error('Error setting media session position state:', err);
            }
        };

        const setupMediaSessionActionHandlers = () => {
            if (!('mediaSession' in navigator)) return;

            navigator.mediaSession.setActionHandler('play', () => {
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.then(() => {
                        widget.isPlaying = true;
                        updatePlayButton();
                    }).catch(e => console.error('Play blocked or failed:', e));
                }
            });

            navigator.mediaSession.setActionHandler('pause', () => {
                audio.pause();
                widget.isPlaying = false;
                updatePlayButton();
            });

            navigator.mediaSession.setActionHandler('previoustrack', () => {
                if (typeof window._ohrbobGoToPrev === 'function') {
                    window._ohrbobGoToPrev();
                } else {
                    audio.currentTime = 0;
                    updateMediaSessionPosition();
                }
            });

            navigator.mediaSession.setActionHandler('nexttrack', async () => {
                if (typeof window._ohrbobGoToNext === 'function') {
                    await window._ohrbobGoToNext();
                } else {
                    await apiCall('/stream/next', 'POST');
                    syncState();
                }
            });

            navigator.mediaSession.setActionHandler('seekbackward', (details) => {
                const offset = details.seekOffset || 15;
                audio.currentTime = Math.max(0, audio.currentTime - offset);
                updateMediaSessionPosition();
            });

            navigator.mediaSession.setActionHandler('seekforward', (details) => {
                const offset = details.seekOffset || 30;
                audio.currentTime = Math.min(audio.duration || 0, audio.currentTime + offset);
                updateMediaSessionPosition();
            });

            try {
                navigator.mediaSession.setActionHandler('seekto', (details) => {
                    if (Number.isFinite(details.seekTime)) {
                        audio.currentTime = details.seekTime;
                        updateMediaSessionPosition();
                    }
                });
            } catch (err) {
                console.warn('Seekto action handler not supported:', err);
            }

            // Register global audio listeners to sync Media Session playbackState event-driven
            audio.onplay = updateMediaSessionPlaybackState;
            audio.onplaying = updateMediaSessionPlaybackState;
            audio.onpause = updateMediaSessionPlaybackState;
        };

        const applyStateToWidget = (state) => {
            if (!state || !state.stationId) {
                // No active station/profile, reset to default empty state
                currentEpisodeData = null;
                nextEpisodeData = null;
                prevEpisodeData = null;
                widget.setAttribute('station', 'My Station');
                widget.setAttribute('track', 'Track Name');
                widget.setAttribute('artist', 'Artist');
                widget.removeAttribute('cover');
                widget.removeAttribute('station-link');
                widget.removeAttribute('next-episode');
                widget.removeAttribute('prev-episode');
                widget.duration = 0;
                widget.currentTime = 0;
                
                const btnPlay = widget.querySelector('.js-play');
                if (btnPlay) btnPlay.setAttribute('disabled', 'true');
                const progress = widget.querySelector('.audio-progress');
                if (progress) progress.setAttribute('disabled', 'true');
                
                widget.querySelectorAll('.js-back, .js-forward, .js-skipback, .js-skipforward').forEach(btn => {
                    btn.setAttribute('disabled', 'true');
                });
                updateMediaSessionMetadata(null);
                return;
            }
            
            activeStationId = state.stationId;
            if (activeStationId) {
                widget.setAttribute('data-active-station-id', activeStationId);
            } else {
                widget.removeAttribute('data-active-station-id');
            }

            // Handle compile/empty state
            if (state.isCompiling || state.isEmpty) {
                currentEpisodeData = null;
                nextEpisodeData = null;

                widget.setAttribute('station', state.stationName || 'Unknown Station');
                widget.setAttribute('track', state.isCompiling ? 'Compiling Station...' : 'No Episodes Available');
                widget.setAttribute('artist', state.isCompiling ? 'We are fetching the latest episodes.' : 'Please add some active feeds.');
                widget.removeAttribute('cover');
                widget.removeAttribute('station-link');
                widget.removeAttribute('next-episode');
                widget.removeAttribute('prev-episode');
                widget.duration = 0;
                widget.currentTime = 0;

                // Disable play button and progress bar
                const btnPlay = widget.querySelector('.js-play');
                if (btnPlay) btnPlay.setAttribute('disabled', 'true');
                
                const progress = widget.querySelector('.audio-progress');
                if (progress) progress.setAttribute('disabled', 'true');

                // Disable skips and forwards
                widget.querySelectorAll('.js-back, .js-forward, .js-skipback, .js-skipforward').forEach(btn => {
                    btn.setAttribute('disabled', 'true');
                });
                updateMediaSessionMetadata(state);
            } else {
                // Normal state (prev/next come from the server)
                currentEpisodeData = state.current || {};
                nextEpisodeData = state.next;
                prevEpisodeData = state.prev || null;

                widget.setAttribute('station', state.stationName || 'Unknown Station');
                widget.setAttribute('track', currentEpisodeData.title || '');
                widget.setAttribute('artist', currentEpisodeData.artist || state.stationName || '');
                if (currentEpisodeData.coverUrl) {
                     widget.setAttribute('cover', currentEpisodeData.coverUrl);
                } else {
                     widget.removeAttribute('cover');
                }
                // Link icon -> direct episode audio URL (absolute http URL as required by widget)
                const episodeLink = currentEpisodeData.link || currentEpisodeData.streamUri;
                if (episodeLink && /^(https?:)?\/\//i.test(episodeLink)) {
                    widget.setAttribute('station-link', episodeLink);
                } else {
                    widget.removeAttribute('station-link');
                }
                applyEpisodeCards();

                // Enable buttons
                const btnPlay = widget.querySelector('.js-play');
                if (btnPlay) btnPlay.removeAttribute('disabled');
                
                const progress = widget.querySelector('.audio-progress');
                if (progress) progress.removeAttribute('disabled');

                widget.querySelectorAll('.js-back, .js-forward, .js-skipback, .js-skipforward').forEach(btn => {
                    btn.removeAttribute('disabled');
                });
                updateMediaSessionMetadata(state);
            }

            document.querySelectorAll(".active-station-card").forEach(c => {
                if (c.getAttribute('data-station-id') === state.stationId) {
                    c.classList.remove("shadow-sm");
                    c.classList.add("border-primary", "border-3", "shadow");
                } else {
                    c.classList.remove("border-primary", "border-3", "shadow");
                    c.classList.add("shadow-sm");
                }
            });

            document.body.dispatchEvent(new CustomEvent('ohrbob:statechange', { detail: { state } }));
        };

        const updatePlayButton = () => {
            const btnPlay = widget.querySelector('.js-play');
            if (btnPlay) {
                const playSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`;
                const pauseSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`;
                btnPlay.innerHTML = widget.isPlaying ? pauseSvg : playSvg;
                btnPlay.style.paddingLeft = widget.isPlaying ? '0' : '2px';
            }
            updateMediaSessionPlaybackState();
        };

        audio.addEventListener('error', (e) => {
            console.error("AUDIO ERROR: ", audio.error ? audio.error.message : "unknown", audio.error ? audio.error.code : 0);
            widget.isPlaying = false;
            updatePlayButton();
        });

        const setupAudio = () => {
            if (!currentEpisodeData || !currentEpisodeData.streamUri) {
                return;
            }
            
            const fullStreamUri = currentEpisodeData.streamUri; 
            
            if (window._ohrbobCurrentLoadedUri !== fullStreamUri || !audio.src || audio.src === window.location.href) {
                window._ohrbobCurrentLoadedUri = fullStreamUri;
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
                    });
                }
            } else {
                // If it is already loaded, ensure widget.isPlaying reflects audio state!
                widget.isPlaying = !audio.paused;
                updatePlayButton();
            }
        };

        const syncState = async () => {
            const state = await apiCall('/stream/status');
            if (state) {
                applyStateToWidget(state);
                if (state.current) {
                    setupAudio();
                }
            }
        };
        window._ohrbobSyncState = syncState;

        const reportPlayhead = async () => {
            if (!widget.isPlaying || !currentEpisodeData || !activeStationId) return;
            
            await apiCall('/stream/status', 'POST', {
                episodeId: currentEpisodeData.episodeId,
                stationId: activeStationId,
                positionSeconds: Math.floor(audio.currentTime)
            });
        };

        audio.onended = async () => {
            if (nextEpisodeData && nextEpisodeData.streamUri) {
                currentEpisodeData = nextEpisodeData;
                widget.setAttribute('track', currentEpisodeData.title);
                if (currentEpisodeData.coverUrl) widget.setAttribute('cover', currentEpisodeData.coverUrl);
                
                const fullStreamUri = currentEpisodeData.streamUri;
                window._ohrbobCurrentLoadedUri = fullStreamUri;
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

        // Direct playback of a known episode object (used for prev/jump navigation).
        // Persists the selection server-side so /stream/next stays consistent.
        const playEpisodeDirect = async (episode) => {
            if (!episode || !episode.streamUri || !activeStationId) return;
            currentEpisodeData = episode;
            widget.setAttribute('track', episode.title || '');
            widget.setAttribute('artist', episode.artist || '');
            if (episode.coverUrl) widget.setAttribute('cover', episode.coverUrl);
            else widget.removeAttribute('cover');
            const episodeLink = episode.link || episode.streamUri;
            if (episodeLink && /^(https?:)?\/\//i.test(episodeLink)) widget.setAttribute('station-link', episodeLink);
            else widget.removeAttribute('station-link');
            applyEpisodeCards();

            const fullStreamUri = episode.streamUri;
            window._ohrbobCurrentLoadedUri = fullStreamUri;
            audio.src = fullStreamUri;
            audio.currentTime = episode.playheadPositionSeconds || 0;
            try { await audio.play(); widget.isPlaying = true; } catch (e) { widget.isPlaying = false; }
            updatePlayButton();

            await apiCall('/stream/status', 'POST', {
                episodeId: episode.episodeId,
                stationId: activeStationId,
                positionSeconds: Math.floor(audio.currentTime)
            });
            // Refresh to get the new next episode for the cards
            const state = await apiCall('/stream/status');
            if (state) applyStateToWidget(state);
        };

        const goToNext = async () => {
            await apiCall('/stream/next', 'POST');
            await syncState();
        };

        const goToPrev = async () => {
            // Typical player UX: restart first if playhead is beyond the threshold
            if (Number.isFinite(audio.currentTime) && audio.currentTime > PREV_RESTART_THRESHOLD_SECONDS) {
                audio.currentTime = 0;
                updateMediaSessionPosition();
                return;
            }
            await apiCall('/stream/prev', 'POST');
            await syncState();
        };
        window._ohrbobGoToNext = goToNext;
        window._ohrbobGoToPrev = goToPrev;
        // We can just use an abort controller or a flag on the widget.
        if (!widget._ohrbobListenersBound) {
            widget._ohrbobListenersBound = true;
            widget.addEventListener('play', () => {
                if (!currentEpisodeData || !currentEpisodeData.streamUri) {
                    widget.isPlaying = false;
                    updatePlayButton();
                    return;
                }

                if (window._ohrbobCurrentLoadedUri !== currentEpisodeData.streamUri || !audio.src || audio.src === window.location.href) {
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
                updatePlayButton();
            });
            
            widget.addEventListener('forward', async () => {
                await goToNext();
            });

            widget.addEventListener('back', async () => {
                await goToPrev();
            });

            widget.addEventListener('jumpnext', async () => {
                await goToNext();
            });

            widget.addEventListener('jumpprev', async () => {
                await goToPrev();
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
        }

        // Always replace the interval so we don't have multiple
        if (window._ohrbobRenderInterval) clearInterval(window._ohrbobRenderInterval);
        window._ohrbobRenderInterval = setInterval(() => {
            widget = document.getElementById('ohrbob-player');
            if (!widget) return;
            
            if (audio.readyState > 0) {
                if (!audio.paused && !widget.isPlaying) {
                    widget.isPlaying = true;
                    updatePlayButton();
                } else if (audio.paused && widget.isPlaying) {
                    widget.isPlaying = false;
                    updatePlayButton();
                }
                
                if (widget.duration !== audio.duration && Number.isFinite(audio.duration)) {
                    widget.duration = audio.duration;
                }
                if (widget.currentTime !== audio.currentTime && Number.isFinite(audio.currentTime)) {
                    widget.currentTime = audio.currentTime;
                }
            }
            updateMediaSessionPosition();
        }, 500);

        setupMediaSessionActionHandlers();
        syncState();
        
        window._ohrbobSyncInterval = setInterval(() => {
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
    
    // Support HTMX navigation
    document.body.addEventListener('htmx:afterSettle', initPlayer);
    
    // Support playhead re-evaluation sync
    document.body.addEventListener('playerSync', () => {
        if (typeof window._ohrbobSyncState === 'function') {
            window._ohrbobSyncState();
        }
    });

    window.checkDeletePlayingStation = function(stationId) {
        let widget = document.getElementById('ohrbob-player');
        if (!widget) return;
        
        let activeStationId = widget.getAttribute('data-active-station-id');
        if (activeStationId === stationId) {
            // Stop playback
            if (window._ohrbobAudio) {
                window._ohrbobAudio.pause();
                window._ohrbobAudio.src = ''; // Clear source to stop buffer/playback completely
            }
            
            widget.isPlaying = false;
            widget.removeAttribute('cover');
            widget.removeAttribute('station-link');
            widget.removeAttribute('next-episode');
            widget.removeAttribute('prev-episode');
            widget.setAttribute('track', 'Playback Stopped');
            widget.setAttribute('artist', 'Current station deleted');
            widget.duration = 0;
            widget.currentTime = 0;
        }
    };

})();

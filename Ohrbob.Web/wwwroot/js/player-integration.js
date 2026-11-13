
(function() {
    function initPlayer() {
        let widget = document.getElementById('ohrbob-player');
        if (!widget) return;
        
        const apiUrl = widget.getAttribute('data-player-api-url');
        const token = widget.getAttribute('data-stream-token');
        const stationEditUrlTemplate = widget.getAttribute('data-station-edit-url');

        if (!apiUrl || !token) return;

        // Absolute station edit URL built server-side via Razor; only the id placeholder is substituted here
        const stationEditUrl = (stationId) => {
            if (!stationEditUrlTemplate || !stationId) return null;
            return stationEditUrlTemplate.replace('__STATIONID__', encodeURIComponent(stationId));
        };
        const applyStationLink = (stationId) => {
            const url = stationEditUrl(stationId);
            if (url) widget.setAttribute('station-link', url);
            else widget.removeAttribute('station-link');
        };

         // "User paused" state: once the user explicitly pauses, playback must never
        // auto-continue (page loads, finished compiles, syncs) until they explicitly
        // play again (play button, station card, episode click, next/prev).
        const PAUSE_COOKIE = 'ohrbob_user_paused';
        const setUserPaused = () => {
            document.cookie = `${PAUSE_COOKIE}=1; path=/; max-age=31536000; samesite=lax`;
        };
        const clearUserPaused = () => {
            document.cookie = `${PAUSE_COOKIE}=0; path=/; max-age=0; samesite=lax`;
        };
        const isUserPaused = () => document.cookie.split('; ').some(c => c === `${PAUSE_COOKIE}=1`);

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
        let lastRemotePlayheadKey = null;
        let lastConnectionErrorTime = 0;
        let activeStationId = null;
        let navigating = false;
        const PREV_RESTART_THRESHOLD_SECONDS = 5;

        // Whether the player currently *intends* to be playing (i.e. it is not an explicit
        // user pause, an error or a stopped/deleted station). Used by the recovery path to
        // re-assert playback after the browser silently refused a background auto-advance.
        // Stored on the (long-lived) audio element so global helpers can clear it too.
        const setAutoPlayExpected = (value) => { audio._ohrbobAutoPlayExpected = Boolean(value); };
        const isAutoPlayExpected = () => Boolean(audio._ohrbobAutoPlayExpected);

        // Display-only sanitizer: decode entities and strip tags until neither reveals new
        // markup, so feed-provided HTML never reaches the widget markup.
        const stripHtml = (value) => {
            let text = value == null ? '' : String(value);
            for (let pass = 0; pass < 5; pass++) {
                const next = (new DOMParser().parseFromString(text, 'text/html').body.textContent || '').trim();
                if (next === text) break;
                text = next;
            }
            return text;
        };

        const sanitizeEpisode = (ep) => ep
            ? { ...ep, title: stripHtml(ep.title), artist: stripHtml(ep.artist) }
            : ep;

        const toCard = (ep) => {
            if (!ep) return null;
            return {
                track: stripHtml(ep.title) || 'Track',
                artist: stripHtml(ep.artist) || '',
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
            const stationName = stripHtml(state.stationName) || 'Ohrbob';
            navigator.mediaSession.metadata = new MediaMetadata({
                title: stripHtml(current.title) || 'Track Name',
                artist: stationName,
                album: stationName,
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
                clearUserPaused();
                setAutoPlayExpected(true);
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.then(() => {
                        widget.isPlaying = true;
                        updatePlayButton();
                    }).catch(e => console.error('Play blocked or failed:', e));
                }
            });

            navigator.mediaSession.setActionHandler('pause', () => {
                setUserPaused();
                setAutoPlayExpected(false);
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
                lastRemotePlayheadKey = null;
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
                lastRemotePlayheadKey = null;

                widget.setAttribute('station', state.stationName || 'Unknown Station');
                widget.setAttribute('track', state.isCompiling ? 'Compiling Station...' : 'No Episodes Available');
                widget.setAttribute('artist', state.isCompiling ? 'We are fetching the latest episodes.' : 'Please add some active feeds.');
                widget.removeAttribute('cover');
                applyStationLink(state.stationId);
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
                currentEpisodeData = sanitizeEpisode(state.current || {});
                nextEpisodeData = sanitizeEpisode(state.next);
                prevEpisodeData = sanitizeEpisode(state.prev || null);

                widget.setAttribute('station', state.stationName || 'Unknown Station');
                widget.setAttribute('track', currentEpisodeData.title || '');
                widget.setAttribute('artist', currentEpisodeData.artist || state.stationName || '');
                if (currentEpisodeData.coverUrl) {
                     widget.setAttribute('cover', currentEpisodeData.coverUrl);
                } else {
                     widget.removeAttribute('cover');
                }
                // Link icon -> station editing view
                applyStationLink(state.stationId);
                applyEpisodeCards();

                // Keep a paused widget in step with playback on another device (e.g. the
                // mobile app): reflect the server's playhead without starting local audio.
                const remotePlayhead = currentEpisodeData.playheadPositionSeconds;
                const remotePlayheadKey = `${currentEpisodeData.episodeId}:${remotePlayhead}`;
                if (audio.paused && Number.isFinite(remotePlayhead) && remotePlayheadKey !== lastRemotePlayheadKey) {
                    lastRemotePlayheadKey = remotePlayheadKey;
                    audio.currentTime = remotePlayhead;
                    widget.currentTime = remotePlayhead;
                }

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
                c.classList.toggle("is-active", c.getAttribute('data-station-id') === state.stationId);
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
            setAutoPlayExpected(false);
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
                
                if (isUserPaused()) {
                    // User asked to pause: load metadata but never auto-continue playback
                    setAutoPlayExpected(false);
                    widget.isPlaying = false;
                    updatePlayButton();
                    return;
                }
                
                setAutoPlayExpected(true);
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
                if (!audio.paused) setAutoPlayExpected(true);
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

        // Optimistically move to the cached next episode and start it. This is the
        // end-of-episode handoff; the server state is then advanced in the background.
        const transitionToNextEpisode = async () => {
            navigating = true;
            try
            {
                if (nextEpisodeData && nextEpisodeData.streamUri) {
                    currentEpisodeData = nextEpisodeData;
                    widget.setAttribute('track', currentEpisodeData.title);
                    if (currentEpisodeData.coverUrl) widget.setAttribute('cover', currentEpisodeData.coverUrl);
                    
                    const fullStreamUri = currentEpisodeData.streamUri;
                    window._ohrbobCurrentLoadedUri = fullStreamUri;
                    audio.src = fullStreamUri;
                    audio.currentTime = 0;
                    
                    setAutoPlayExpected(true);
                    const playPromise = audio.play();
                    if (playPromise !== undefined) {
                        playPromise.catch(e => console.error('Play blocked or failed:', e));
                    }
                    
                    await apiCall('/stream/next', 'POST');
                    
                    const state = await apiCall('/stream/status');
                    if (state) applyStateToWidget(state);
                } else {
                    setAutoPlayExpected(false);
                    widget.isPlaying = false;
                    updatePlayButton();
                }
            }
            finally
            {
                navigating = false;
            }
        };

        audio.onended = transitionToNextEpisode;

        // Mobile browsers (notably iOS Safari, see WebKit bugs 173332 / 261554) suspend the
        // page's JS and/or silently refuse to start a newly-assigned media source while the
        // screen is locked / app is backgrounded: play() resolves but no audio is heard. This
        // re-asserts playback once the page is visible/focused again (and from the periodic
        // sync), respecting an explicit user pause.
        const resumePlaybackIfInterrupted = async () => {
            if (document.hidden || navigating) return;
            if (isUserPaused() || !isAutoPlayExpected()) return;
            if (!currentEpisodeData || !currentEpisodeData.streamUri || !activeStationId) return;
            if (!audio.paused && !audio.ended) return;

            // The episode concluded while we could not observe it: advance to the next one.
            if (audio.ended) {
                await transitionToNextEpisode();
                return;
            }

            if (window._ohrbobCurrentLoadedUri === currentEpisodeData.streamUri &&
                audio.src && audio.src !== window.location.href) {
                // The correct source is loaded but playback never actually started: retry it.
                const playPromise = audio.play();
                if (playPromise !== undefined) {
                    playPromise.then(() => {
                        widget.isPlaying = true;
                        updatePlayButton();
                    }).catch(e => console.error('Recovery play failed:', e));
                } else {
                    widget.isPlaying = true;
                    updatePlayButton();
                }
                return;
            }

            // Nothing loaded for the current episode: load and start it.
            setupAudio();
        };

        // Direct playback of a known episode object (used for prev/jump navigation).
        // Persists the selection server-side so /stream/next stays consistent.
        const playEpisodeDirect = async (episode) => {
            if (!episode || !episode.streamUri || !activeStationId) return;
            episode = sanitizeEpisode(episode);
            navigating = true;
            try
            {
                clearUserPaused(); // explicit user action: play this episode
                currentEpisodeData = episode;
                widget.setAttribute('track', episode.title || '');
                widget.setAttribute('artist', episode.artist || '');
                if (episode.coverUrl) widget.setAttribute('cover', episode.coverUrl);
                else widget.removeAttribute('cover');
                applyStationLink(activeStationId);
                applyEpisodeCards();

                const fullStreamUri = episode.streamUri;
                window._ohrbobCurrentLoadedUri = fullStreamUri;
                audio.src = fullStreamUri;
                audio.currentTime = episode.playheadPositionSeconds || 0;
                setAutoPlayExpected(true);
                try { await audio.play(); widget.isPlaying = true; } catch (e) { setAutoPlayExpected(false); widget.isPlaying = false; }
                updatePlayButton();

                await apiCall('/stream/status', 'POST', {
                    episodeId: episode.episodeId,
                    stationId: activeStationId,
                    positionSeconds: Math.floor(audio.currentTime)
                });
                // Refresh to get the new next episode for the cards
                const state = await apiCall('/stream/status');
                if (state) applyStateToWidget(state);
            }
            finally
            {
                navigating = false;
            }
        };

        // Explicit "continue this episode" from the station edit page: make it the active
        // episode, persist the shown playhead and start playback.
        window._ohrbobContinueEpisode = async (stationId, episodeId, positionSeconds) => {
            if (!stationId || !episodeId) return;
            const position = Number(positionSeconds) || 0;
            clearUserPaused(); // explicit user action: play this episode
            await apiCall('/stream/status', 'POST', { episodeId, stationId, positionSeconds: position });

            const sameTrack = currentEpisodeData && currentEpisodeData.episodeId === episodeId &&
                window._ohrbobCurrentLoadedUri === currentEpisodeData.streamUri &&
                audio.src && audio.src !== window.location.href;

            // syncState loads/starts a different episode; an already loaded one is only refreshed.
            await syncState();
            if (!sameTrack) return;

            audio.currentTime = position;
            setAutoPlayExpected(true);
            try { await audio.play(); widget.isPlaying = true; } catch (e) { setAutoPlayExpected(false); widget.isPlaying = false; }
            updatePlayButton();
        };

        // Keep the loaded audio in step when the edit page changes the playhead of the episode
        // that is currently playing.
        window._ohrbobSeekIfCurrent = (episodeId, positionSeconds) => {
            if (!currentEpisodeData || currentEpisodeData.episodeId !== episodeId) return;
            if (!Number.isFinite(positionSeconds)) return;
            audio.currentTime = positionSeconds;
            updateMediaSessionPosition();
        };

        const goToNext = async () => {
            navigating = true;
            try
            {
                clearUserPaused(); // explicit user action
                await apiCall('/stream/next', 'POST');
                await syncState();
            }
            finally
            {
                navigating = false;
            }
        };

        const goToPrev = async () => {
            // Typical player UX: restart first if playhead is beyond the threshold
            if (Number.isFinite(audio.currentTime) && audio.currentTime > PREV_RESTART_THRESHOLD_SECONDS) {
                audio.currentTime = 0;
                updateMediaSessionPosition();
                return;
            }
            navigating = true;
            try
            {
                clearUserPaused(); // explicit user action
                await apiCall('/stream/prev', 'POST');
                await syncState();
            }
            finally
            {
                navigating = false;
            }
        };
        window._ohrbobGoToNext = goToNext;
        window._ohrbobGoToPrev = goToPrev;
        // We can just use an abort controller or a flag on the widget.
        if (!widget._ohrbobListenersBound) {
            widget._ohrbobListenersBound = true;
            widget.addEventListener('play', () => {
                clearUserPaused(); // explicit user action: resets to auto-play
                setAutoPlayExpected(true);
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
                setUserPaused(); // user asked to pause: never auto-continue until they play again
                setAutoPlayExpected(false);
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
                clearUserPaused(); // explicit user action: play this station
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
        
        window._ohrbobSyncInterval = setInterval(async () => {
            if (widget.isPlaying && currentEpisodeData && activeStationId) {
                // Persist the playhead, then refresh the queue (next/prev). This keeps the
                // upcoming episode in sync with the server even when the schedule changed or a
                // feed finished compiling without any explicit page interaction. Skipped while a
                // track transition (ended/next/prev/jump) is in flight to avoid a stale current.
                await reportPlayhead();
                if (!navigating) {
                    await syncState();
                }
                return;
            }

            // Not playing here: refresh from the server so playback running on another
            // device (e.g. the mobile app via the Player API) stays visible and up to date.
            if (!navigating) {
                await syncState();
            }
            await resumePlaybackIfInterrupted();
        }, 5000);

        // Recover playback as soon as the page is visible/focused again. Registered once; the
        // wrapper always calls the latest instance's handler (initPlayer runs on every nav).
        window._ohrbobResumeIfInterrupted = resumePlaybackIfInterrupted;
        if (!window._ohrbobRecoveryBound) {
            window._ohrbobRecoveryBound = true;
            document.addEventListener('visibilitychange', () => {
                if (!document.hidden) window._ohrbobResumeIfInterrupted?.();
            });
            window.addEventListener('focus', () => window._ohrbobResumeIfInterrupted?.());
            window.addEventListener('pageshow', () => window._ohrbobResumeIfInterrupted?.());
        }

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
                window._ohrbobAudio._ohrbobAutoPlayExpected = false; // non-user stop: do not auto-resume
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

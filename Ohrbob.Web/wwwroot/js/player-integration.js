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

    const setupAudio = () => {
        if (!currentEpisodeData) return;
        
        const fullStreamUri = apiUrl + currentEpisodeData.streamUri; // assuming streamUri is relative to api
        
        // If it's a new track, load it
        if (audio.src !== fullStreamUri) {
            audio.src = fullStreamUri;
            audio.currentTime = currentEpisodeData.playheadPositionSeconds || 0;
            audio.play().catch(e => console.log('Autoplay blocked', e));
            widget.isPlaying = true;
            updatePlayButton();
        }
    };

    const syncState = async () => {
        const state = await apiCall('/stream/status');
        if (state && state.current) {
            applyStateToWidget(state);
            setupAudio();
            widget.style.display = 'block';
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
            
            const fullStreamUri = apiUrl + currentEpisodeData.streamUri;
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

    // Initial sync
    syncState();
    
    // Heartbeat every 10 seconds
    syncInterval = setInterval(reportPlayhead, 10000);
});
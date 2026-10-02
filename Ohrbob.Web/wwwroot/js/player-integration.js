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

    // Helper to call player API
    const apiCall = async (endpoint, method = 'GET') => {
        try {
            const response = await fetch(`${apiUrl}${endpoint}`, { method, headers });
            if (!response.ok) throw new Error('API Error');
            
            const text = await response.text();
            return text ? JSON.parse(text) : null;
        } catch (err) {
            console.error('Player API error:', err);
            return null;
        }
    };

    // Sync widget state with server
    const syncState = async () => {
        const state = await apiCall('/stream/status');
        if (state) {
            widget.setAttribute('station', state.stationName || 'Unknown Station');
            widget.setAttribute('track', state.trackName || '');
            widget.setAttribute('artist', state.artistName || '');
            if (state.coverUrl) widget.setAttribute('cover', state.coverUrl);
            
            // Sync play state if it has changed
            if (widget.isPlaying !== state.isPlaying) {
                widget.isPlaying = state.isPlaying;
                const btnPlay = widget.querySelector('.js-play');
                if (btnPlay) {
                    const playSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`;
                    const pauseSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`;
                    btnPlay.innerHTML = widget.isPlaying ? pauseSvg : playSvg;
                    btnPlay.style.paddingLeft = widget.isPlaying ? '0' : '2px';
                }
            }
            
            widget.style.display = 'block'; // Show widget once initialized
        }
    };

    // Bind widget events to API commands
    widget.addEventListener('play', () => apiCall('/stream/play', 'POST'));
    widget.addEventListener('pause', () => apiCall('/stream/pause', 'POST'));
    widget.addEventListener('forward', async () => {
        await apiCall('/stream/next', 'POST');
        syncState();
    });
    widget.addEventListener('back', async () => {
        await apiCall('/stream/prev', 'POST');
        syncState();
    });

    // Initial sync
    syncState();
    
    // Optional: Poll for updates every 10 seconds to keep playhead/track in sync
    setInterval(syncState, 10000);
});
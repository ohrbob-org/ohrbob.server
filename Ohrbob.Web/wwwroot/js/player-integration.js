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
            return await response.json();
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
            
            if (state.isPlaying) {
                // Ensure internal widget logic knows it's playing if needed
                // widget.play(); 
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
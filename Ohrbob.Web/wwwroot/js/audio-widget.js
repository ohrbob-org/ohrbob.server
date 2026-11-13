const ICONS = {
    play: `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`,
    pause: `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`,
    back: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 19 2 12 11 5 11 19"></polygon><polygon points="22 19 13 12 22 5 22 19"></polygon></svg>`,
    forward: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="13 19 22 12 13 5 13 19"></polygon><polygon points="2 19 11 12 2 5 2 19"></polygon></svg>`,
    skipBack: `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="1 4 1 10 7 10"></polyline><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"></path><text x="12" y="15" text-anchor="middle" font-size="7" font-weight="700" fill="currentColor" stroke="none">15</text></svg>`,
    skipForward: `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="23 4 23 10 17 10"></polyline><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"></path><text x="12" y="15" text-anchor="middle" font-size="7" font-weight="700" fill="currentColor" stroke="none">30</text></svg>`,
    volume: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path><path d="M19.07 4.93a10 10 0 0 1 0 14.14"></path></svg>`,
    muted: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><line x1="23" y1="9" x2="17" y2="15"></line><line x1="17" y1="9" x2="23" y2="15"></line></svg>`,
    settings: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>`,
    arrowLeft: `<svg viewBox="0 0 24 24" width="14" height="14" stroke="currentColor" stroke-width="2.5" fill="none" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>`,
    arrowRight: `<svg viewBox="0 0 24 24" width="14" height="14" stroke="currentColor" stroke-width="2.5" fill="none" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>`
};

function clamp(value, min, max) {
    if (!Number.isFinite(value)) return min;
    return Math.min(Math.max(value, min), max);
}

function formatTime(seconds) {
    if (!Number.isFinite(seconds) || seconds <= 0) return '--:--';
    const total = Math.floor(seconds);
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    const pad = (n) => String(n).padStart(2, '0');
    return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}

class AudioWidget extends HTMLElement {
    constructor() {
        super();
        this._rendered = false;
        this._duration = 0;
        this._currentTime = 0;
        this._volume = 1;
        this._muted = false;
        this._isPlaying = false;
        this._scrubTime = null;
        
        this._isExpanded = false;
        this._nextEpisode = null;
        this._prevEpisode = null;
    }

    connectedCallback() {
        this.classList.add('audio-widget', 'glass-panel', 'glass-edge');
        this.render();
    }

    static get observedAttributes() {
        return ['station', 'track', 'artist', 'cover', 'next-episode', 'prev-episode', 'station-link'];
    }

    attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue === newValue) return;
        
        if (name === 'next-episode') {
            try {
                this._nextEpisode = newValue ? JSON.parse(newValue) : null;
            } catch (e) {
                console.error('Error parsing next-episode JSON:', e);
            }
        } else if (name === 'prev-episode') {
            try {
                this._prevEpisode = newValue ? JSON.parse(newValue) : null;
            } catch (e) {
                console.error('Error parsing prev-episode JSON:', e);
            }
        }
        
        if (this._rendered) {
            this.render();
        }
    }

    get isPlaying() { return this._isPlaying; }
    set isPlaying(value) {
        this._isPlaying = Boolean(value);
        if (this._rendered) this._applyPlaying();
    }

    get duration() { return this._duration; }
    set duration(value) {
        const next = Math.max(0, Number(value) || 0);
        if (next === this._duration) return;
        this._duration = next;
        if (this._currentTime > next) this._currentTime = next;
        if (this._rendered) {
            this._applyDuration();
            this._applyCurrentTime();
        }
    }

    get currentTime() { return this._currentTime; }
    set currentTime(value) {
        const num = Number(value);
        const next = clamp(Number.isFinite(num) ? num : 0, 0, this._duration > 0 ? this._duration : Infinity);
        if (next === this._currentTime) return;
        this._currentTime = next;
        if (this._rendered) this._applyCurrentTime();
    }

    get volume() { return this._volume; }
    set volume(value) {
        const num = Number(value);
        const next = clamp(Number.isFinite(num) ? num : 0, 0, 1);
        if (next === this._volume) return;
        this._volume = next;
        if (this._rendered) this._applyVolume();
    }

    get muted() { return this._muted; }
    set muted(value) {
        const next = Boolean(value);
        if (next === this._muted) return;
        this._muted = next;
        if (this._rendered) this._applyMuted();
    }

    get isExpanded() { return this._isExpanded; }
    set isExpanded(value) {
        const next = Boolean(value);
        if (next === this._isExpanded) return;
        this._isExpanded = next;
        if (next) {
            this.classList.add('is-expanded');
            this.dispatchEvent(new CustomEvent('expand', { bubbles: true }));
        } else {
            this.classList.remove('is-expanded');
            this.dispatchEvent(new CustomEvent('collapse', { bubbles: true }));
        }
    }

    get nextEpisode() { return this._nextEpisode; }
    set nextEpisode(value) {
        this._nextEpisode = value;
        if (this._rendered) {
            this.render();
        }
    }

    get prevEpisode() { return this._prevEpisode; }
    set prevEpisode(value) {
        this._prevEpisode = value;
        if (this._rendered) {
            this.render();
        }
    }

    get stationLink() { return this.getAttribute('station-link'); }
    set stationLink(value) {
        if (value) {
            this.setAttribute('station-link', value);
        } else {
            this.removeAttribute('station-link');
        }
    }

    render() {
        const station = this.getAttribute('station') || 'My Station';
        const track = this.getAttribute('track') || 'Track Name';
        const artist = this.getAttribute('artist') || 'Artist';
        const cover = this.getAttribute('cover');

        let stationLink = this.getAttribute('station-link') || '';
        let isExternalLink = false;
        if (stationLink) {
            try {
                const url = new URL(stationLink, window.location.href);
                if (url.protocol !== 'http:' && url.protocol !== 'https:') {
                    stationLink = '';
                } else {
                    isExternalLink = url.origin !== window.location.origin;
                }
            } catch (e) {
                stationLink = '';
            }
        }
        const escapedStationLink = stationLink.replace(/"/g, '&quot;');

        // Same-origin links stay in-tab so htmx boost can swap #main-content without interrupting playback
        const stationLinkHtml = escapedStationLink
            ? `<a href="${escapedStationLink}" ${isExternalLink ? 'target="_blank" rel="noopener noreferrer"' : ''} class="station-link-icon" aria-label="${isExternalLink ? 'Visit Station' : 'Edit Station'}">
                <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path>
                    <polyline points="15 3 21 3 21 9"></polyline>
                    <line x1="10" y1="14" x2="21" y2="3"></line>
                </svg>
               </a>`
            : '';

        const coverHtml = cover 
            ? `<img src="${cover}" alt="" class="track-cover">` 
            : `<div class="track-cover track-cover-empty">♪</div>`;

        const expandedCoverHtml = cover 
            ? `<img src="${cover}" alt="" class="expanded-cover">` 
            : `<div class="expanded-cover-empty">♪</div>`;

        // Render previous episode row block
        let prevEpisodeHtml = '';
        if (this._prevEpisode) {
            const prevCover = this._prevEpisode.cover 
                ? `<img src="${this._prevEpisode.cover}" alt="" class="episode-nav-cover">` 
                : `<div class="episode-nav-cover-empty">♪</div>`;
            prevEpisodeHtml = `
                <div class="episode-nav-card prev-episode-card">
                    <div class="episode-nav-label">Previous Episode</div>
                    <div class="episode-nav-content">
                        <button type="button" class="btn btn-glass-primary btn-sm btn-jump js-jump-prev">${ICONS.arrowLeft}Go Back</button>
                        <div class="episode-nav-cover-container">${prevCover}</div>
                        <div class="episode-nav-info">
                            <strong class="episode-nav-title">${this._prevEpisode.track || 'Track'}</strong>
                            <span class="episode-nav-artist">${this._prevEpisode.artist || 'Artist'}</span>
                        </div>
                    </div>
                </div>
            `;
        }

        // Render next episode row block
        let nextEpisodeHtml = '';
        if (this._nextEpisode) {
            const nextCover = this._nextEpisode.cover 
                ? `<img src="${this._nextEpisode.cover}" alt="" class="episode-nav-cover">` 
                : `<div class="episode-nav-cover-empty">♪</div>`;
            nextEpisodeHtml = `
                <div class="episode-nav-card next-episode-card">
                    <div class="episode-nav-label">Next Episode</div>
                    <div class="episode-nav-content">
                        <div class="episode-nav-cover-container">${nextCover}</div>
                        <div class="episode-nav-info">
                            <strong class="episode-nav-title">${this._nextEpisode.track || 'Track'}</strong>
                            <span class="episode-nav-artist">${this._nextEpisode.artist || 'Artist'}</span>
                        </div>
                        <button type="button" class="btn btn-glass-primary btn-sm btn-jump js-jump-next">Jump${ICONS.arrowRight}</button>
                    </div>
                </div>
            `;
        }

        // Side-by-side episode navigation: previous card left, next card right.
        // A lone card spans the full row width via CSS (:only-child).
        let episodeNavHtml = '';
        if (prevEpisodeHtml || nextEpisodeHtml) {
            episodeNavHtml = `
                <div class="episode-nav-row">
                    ${prevEpisodeHtml}
                    ${nextEpisodeHtml}
                </div>
            `;
        }

        this.innerHTML = `
            <div class="audio-bar">
                <div class="audio-meta">
                    ${coverHtml}
                </div>
                <div class="audio-controls">
                    <button type="button" class="btn btn-glass rounded-circle btn-icon js-back" aria-label="Previous episode">${ICONS.back}</button>
                    <button type="button" class="btn btn-glass rounded-circle btn-skip js-skipback" aria-label="Skip back 15 seconds">${ICONS.skipBack}</button>
                    <button type="button" class="btn btn-glass-primary rounded-circle btn-play js-play" aria-label="Play">${ICONS.play}</button>
                    <button type="button" class="btn btn-glass rounded-circle btn-skip js-skipforward" aria-label="Skip forward 30 seconds">${ICONS.skipForward}</button>
                    <button type="button" class="btn btn-glass rounded-circle btn-icon js-forward" aria-label="Next episode">${ICONS.forward}</button>
                </div>
                <div class="audio-progress-group">
                    <span class="time-elapsed">--:--</span>
                    <input type="range" class="audio-progress" min="0" max="0" step="0.1" value="0" aria-label="Seek">
                    <span class="time-total">--:--</span>
                </div>
                <div class="audio-volume-group">
                    <button type="button" class="btn-mute js-mute" aria-label="Mute" aria-pressed="false">${ICONS.volume}</button>
                    <input type="range" class="audio-volume" min="0" max="1" step="0.01" value="1" aria-label="Volume">
                </div>
                <button type="button" class="btn-menu js-settings" aria-label="Settings">${ICONS.settings}</button>
            </div>

            <!-- Fullscreen Blur Backdrop Overlay -->
            <div class="audio-expanded-backdrop js-close-expanded"></div>

            <!-- Slide-Up Card Overlay -->
            <div class="audio-expanded-card">
                <div class="expanded-header">
                    <button type="button" class="btn-close-expanded js-close-expanded" aria-label="Minimize player">
                        <svg viewBox="0 0 24 24" width="24" height="24" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round">
                            <polyline points="6 9 12 15 18 9"></polyline>
                        </svg>
                    </button>
                    <span class="expanded-header-title">Now Playing</span>
                    <div style="width: 24px;"></div>
                </div>
                <div class="expanded-body">
                    <div class="expanded-cover-container">
                        ${expandedCoverHtml}
                    </div>
                    <div class="expanded-info">
                        <span class="expanded-station"><span class="station-dot"></span>${station}${stationLinkHtml}</span>
                        <strong class="expanded-title">${track}</strong>
                        <span class="expanded-artist">${artist}</span>
                    </div>
                    <div class="expanded-controls-group">
                        <div class="expanded-progress-row">
                            <span class="time-elapsed js-expanded-elapsed">--:--</span>
                            <input type="range" class="audio-progress js-expanded-progress" min="0" max="0" step="0.1" value="0" aria-label="Seek">
                            <span class="time-total js-expanded-total">--:--</span>
                        </div>
                        <div class="expanded-controls">
                            <button type="button" class="btn btn-glass rounded-circle btn-skip js-expanded-skipback" aria-label="Skip back 15 seconds">${ICONS.skipBack}</button>
                            <button type="button" class="btn btn-glass-primary rounded-circle btn-play js-expanded-play" aria-label="Play">${ICONS.play}</button>
                            <button type="button" class="btn btn-glass rounded-circle btn-skip js-expanded-skipforward" aria-label="Skip forward 30 seconds">${ICONS.skipForward}</button>
                        </div>
                    </div>
                    ${episodeNavHtml}
                </div>
            </div>
        `;

        if (this._isExpanded) {
            this.classList.add('is-expanded');
        } else {
            this.classList.remove('is-expanded');
        }

        this._rendered = true;
        this.attachEvents();
        this.applyState();
    }

    applyState() {
        this._applyDuration();
        this._applyCurrentTime();
        this._applyVolume();
        this._applyMuted();
        this._applyPlaying();
    }

    _applyDuration() {
        const bar = this.querySelector('.audio-bar .audio-progress');
        const total = this.querySelector('.audio-bar .time-total');
        if (bar) bar.max = String(this._duration);
        if (total) total.textContent = formatTime(this._duration);

        // Mirrored elements
        const expBar = this.querySelector('.audio-expanded-card .js-expanded-progress');
        const expTotal = this.querySelector('.audio-expanded-card .js-expanded-total');
        if (expBar) expBar.max = String(this._duration);
        if (expTotal) expTotal.textContent = formatTime(this._duration);

        this._paintProgress();
    }

    _applyCurrentTime() {
        this._paintProgress();
    }

    _paintProgress() {
        const bar = this.querySelector('.audio-bar .audio-progress');
        const elapsed = this.querySelector('.audio-bar .time-elapsed');
        const expBar = this.querySelector('.audio-expanded-card .js-expanded-progress');
        const expElapsed = this.querySelector('.audio-expanded-card .js-expanded-elapsed');

        const shown = this._scrubTime !== null ? this._scrubTime : this._currentTime;
        const pct = this._duration > 0 ? clamp((shown / this._duration) * 100, 0, 100) : 0;

        if (bar) {
            bar.value = String(shown);
            bar.style.setProperty('--progress', `${pct}%`);
        }
        if (elapsed) elapsed.textContent = formatTime(shown);

        if (expBar) {
            expBar.value = String(shown);
            expBar.style.setProperty('--progress', `${pct}%`);
        }
        if (expElapsed) expElapsed.textContent = formatTime(shown);
    }

    _applyVolume() {
        const slider = this.querySelector('.audio-volume');
        if (!slider) return;
        slider.value = String(this._volume);
        const pct = this._volume * 100;
        slider.style.setProperty('--progress', `${pct}%`);
    }

    _applyMuted() {
        const btn = this.querySelector('.js-mute');
        if (!btn) return;
        btn.innerHTML = this._muted ? ICONS.muted : ICONS.volume;
        btn.setAttribute('aria-pressed', String(this._muted));
        btn.setAttribute('aria-label', this._muted ? 'Unmute' : 'Mute');
    }

    _applyPlaying() {
        const btn = this.querySelector('.js-play');
        const expBtn = this.querySelector('.js-expanded-play');

        if (btn) {
            btn.innerHTML = this._isPlaying ? ICONS.pause : ICONS.play;
            btn.style.paddingLeft = this._isPlaying ? '0' : '2px';
            btn.setAttribute('aria-label', this._isPlaying ? 'Pause' : 'Play');
        }

        if (expBtn) {
            expBtn.innerHTML = this._isPlaying ? ICONS.pause : ICONS.play;
            expBtn.style.paddingLeft = this._isPlaying ? '0' : '2px';
            expBtn.setAttribute('aria-label', this._isPlaying ? 'Pause' : 'Play');
        }
    }

    attachEvents() {
        // Play / Pause togglers
        const play = this.querySelector('.js-play');
        const expPlay = this.querySelector('.js-expanded-play');

        const handlePlayClick = () => {
            this.isPlaying = !this.isPlaying;
            this.dispatchEvent(new CustomEvent(this.isPlaying ? 'play' : 'pause', { bubbles: true }));
        };

        if (play) play.addEventListener('click', handlePlayClick);
        if (expPlay) expPlay.addEventListener('click', handlePlayClick);

        // Standard skips / navs
        const backBtn = this.querySelector('.js-back');
        if (backBtn) {
            backBtn.addEventListener('click', () => this.dispatchEvent(new CustomEvent('back', { bubbles: true })));
        }
        const fwdBtn = this.querySelector('.js-forward');
        if (fwdBtn) {
            fwdBtn.addEventListener('click', () => this.dispatchEvent(new CustomEvent('forward', { bubbles: true })));
        }

        // Collapsed skip handlers
        const skipback = this.querySelector('.js-skipback');
        if (skipback) {
            skipback.addEventListener('click', () => this.dispatchEvent(new CustomEvent('skipback', { bubbles: true, detail: { seconds: 15 } })));
        }
        const skipforward = this.querySelector('.js-skipforward');
        if (skipforward) {
            skipforward.addEventListener('click', () => this.dispatchEvent(new CustomEvent('skipforward', { bubbles: true, detail: { seconds: 30 } })));
        }

        // Expanded skip handlers (mirrored)
        const expSkipback = this.querySelector('.js-expanded-skipback');
        if (expSkipback) {
            expSkipback.addEventListener('click', () => this.dispatchEvent(new CustomEvent('skipback', { bubbles: true, detail: { seconds: 15 } })));
        }
        const expSkipforward = this.querySelector('.js-expanded-skipforward');
        if (expSkipforward) {
            expSkipforward.addEventListener('click', () => this.dispatchEvent(new CustomEvent('skipforward', { bubbles: true, detail: { seconds: 30 } })));
        }

        const settings = this.querySelector('.js-settings');
        if (settings) {
            settings.addEventListener('click', () => this.dispatchEvent(new CustomEvent('settings', { bubbles: true })));
        }

        const coverEl = this.querySelector('.audio-bar .track-cover');
        if (coverEl) {
            coverEl.addEventListener('click', () => {
                this.dispatchEvent(new CustomEvent('coverclick', { bubbles: true }));
                this.isExpanded = true;
            });
        }

        // Expand layout trigger when tapping on bottom bar meta
        const metaEl = this.querySelector('.audio-bar .audio-meta');
        if (metaEl) {
            metaEl.addEventListener('click', (e) => {
                // Prevent expanding if clicking some child anchor/button (none present currently, but safe)
                if (e.target.closest('button') || e.target.closest('a')) return;
                this.isExpanded = true;
            });
        }

        // Collapse close triggers (both top down-arrow and the background backdrop!)
        const closeExpandedTriggers = this.querySelectorAll('.js-close-expanded');
        closeExpandedTriggers.forEach(trigger => {
            trigger.addEventListener('click', () => {
                this.isExpanded = false;
            });
        });

        // Navigation Card Jump Event Dispatches
        const jumpPrevBtn = this.querySelector('.js-jump-prev');
        if (jumpPrevBtn) {
            jumpPrevBtn.addEventListener('click', () => {
                this.dispatchEvent(new CustomEvent('jumpprev', {
                    bubbles: true,
                    detail: { episode: this._prevEpisode }
                }));
            });
        }

        const jumpNextBtn = this.querySelector('.js-jump-next');
        if (jumpNextBtn) {
            jumpNextBtn.addEventListener('click', () => {
                this.dispatchEvent(new CustomEvent('jumpnext', {
                    bubbles: true,
                    detail: { episode: this._nextEpisode }
                }));
            });
        }

        const mute = this.querySelector('.js-mute');
        if (mute) {
            mute.addEventListener('click', () => {
                this.muted = !this.muted;
                this.dispatchEvent(new CustomEvent('mutechange', { bubbles: true, detail: { muted: this._muted } }));
            });
        }

        // Progress scrubbing (Mirrored)
        const bar = this.querySelector('.audio-bar .audio-progress');
        const handleProgressInput = (inputEl) => {
            this._scrubTime = Number(inputEl.value);
            this._paintProgress();
            this.dispatchEvent(new CustomEvent('seek', { bubbles: true, detail: { time: this._scrubTime } }));
        };
        const handleProgressChange = () => {
            this._scrubTime = null;
            this._paintProgress();
        };

        if (bar) {
            bar.addEventListener('input', () => handleProgressInput(bar));
            bar.addEventListener('change', handleProgressChange);
        }

        const expBar = this.querySelector('.audio-expanded-card .js-expanded-progress');
        if (expBar) {
            expBar.addEventListener('input', () => handleProgressInput(expBar));
            expBar.addEventListener('change', handleProgressChange);
        }

        const volume = this.querySelector('.audio-volume');
        if (volume) {
            volume.addEventListener('input', () => {
                this.volume = Number(volume.value);
                this.dispatchEvent(new CustomEvent('volumechange', { bubbles: true, detail: { volume: this._volume } }));
            });
        }
    }
}

customElements.define('audio-widget', AudioWidget);

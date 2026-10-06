var ICONS = {
    play: `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`,
    pause: `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`,
    back: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 19 2 12 11 5 11 19"></polygon><polygon points="22 19 13 12 22 5 22 19"></polygon></svg>`,
    forward: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="13 19 22 12 13 5 13 19"></polygon><polygon points="2 19 11 12 2 5 2 19"></polygon></svg>`,
    skipBack: `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="1 4 1 10 7 10"></polyline><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"></path><text x="12" y="15" text-anchor="middle" font-size="7" font-weight="700" fill="currentColor" stroke="none">15</text></svg>`,
    skipForward: `<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="23 4 23 10 17 10"></polyline><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"></path><text x="12" y="15" text-anchor="middle" font-size="7" font-weight="700" fill="currentColor" stroke="none">30</text></svg>`,
    volume: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path><path d="M19.07 4.93a10 10 0 0 1 0 14.14"></path></svg>`,
    muted: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><line x1="23" y1="9" x2="17" y2="15"></line><line x1="17" y1="9" x2="23" y2="15"></line></svg>`,
    settings: `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>`
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

if (typeof window._AudioWidgetDefined === 'undefined') {
window._AudioWidgetDefined = true;
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
    }

    connectedCallback() {
        this.classList.add('audio-widget', 'glass-panel', 'glass-edge');
        this.render();
    }

    static get observedAttributes() {
        return ['station', 'track', 'artist', 'cover'];
    }

    attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue !== newValue && this._rendered) {
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

    render() {
        const station = this.getAttribute('station') || 'My Station';
        const track = this.getAttribute('track') || 'Track Name';
        const artist = this.getAttribute('artist') || 'Artist';
        const cover = this.getAttribute('cover');

        const coverHtml = cover 
            ? `<img src="${cover}" alt="" class="track-cover">` 
            : `<div class="track-cover track-cover-empty">♪</div>`;

        this.innerHTML = `
            <div class="audio-bar">
                <div class="audio-meta">
                    ${coverHtml}
                    <div class="audio-info">
                        <span class="station-name"><span class="station-dot"></span>${station}</span>
                        <strong class="track-title">${track}</strong>
                        <small class="track-description">${artist}</small>
                    </div>
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
        `;

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
        const bar = this.querySelector('.audio-progress');
        const total = this.querySelector('.time-total');
        if (!bar || !total) return;
        bar.max = String(this._duration);
        total.textContent = formatTime(this._duration);
        this._paintProgress();
    }

    _applyCurrentTime() {
        this._paintProgress();
    }

    _paintProgress() {
        const bar = this.querySelector('.audio-progress');
        const elapsed = this.querySelector('.time-elapsed');
        if (!bar || !elapsed) return;
        const shown = this._scrubTime !== null ? this._scrubTime : this._currentTime;
        bar.value = String(shown);
        const pct = this._duration > 0 ? clamp((shown / this._duration) * 100, 0, 100) : 0;
        bar.style.setProperty('--progress', `${pct}%`);
        elapsed.textContent = formatTime(shown);
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
        if (!btn) return;
        btn.innerHTML = this._isPlaying ? ICONS.pause : ICONS.play;
        btn.style.paddingLeft = this._isPlaying ? '0' : '2px';
        btn.setAttribute('aria-label', this._isPlaying ? 'Pause' : 'Play');
    }

    attachEvents() {
        const play = this.querySelector('.js-play');
        play.addEventListener('click', () => {
            this.isPlaying = !this.isPlaying;
            this.dispatchEvent(new CustomEvent(this.isPlaying ? 'play' : 'pause', { bubbles: true }));
        });

        this.querySelector('.js-back').addEventListener('click', () => this.dispatchEvent(new CustomEvent('back', { bubbles: true })));
        this.querySelector('.js-forward').addEventListener('click', () => this.dispatchEvent(new CustomEvent('forward', { bubbles: true })));
        this.querySelector('.js-skipback').addEventListener('click', () => this.dispatchEvent(new CustomEvent('skipback', { bubbles: true, detail: { seconds: 15 } })));
        this.querySelector('.js-skipforward').addEventListener('click', () => this.dispatchEvent(new CustomEvent('skipforward', { bubbles: true, detail: { seconds: 30 } })));
        this.querySelector('.js-settings').addEventListener('click', () => this.dispatchEvent(new CustomEvent('settings', { bubbles: true })));

        const coverEl = this.querySelector('.track-cover');
        if (coverEl) {
            coverEl.addEventListener('click', () => this.dispatchEvent(new CustomEvent('coverclick', { bubbles: true })));
        }

        const mute = this.querySelector('.js-mute');
        mute.addEventListener('click', () => {
            this.muted = !this.muted;
            this.dispatchEvent(new CustomEvent('mutechange', { bubbles: true, detail: { muted: this._muted } }));
        });

        const bar = this.querySelector('.audio-progress');
        bar.addEventListener('input', () => {
            this._scrubTime = Number(bar.value);
            this._paintProgress();
            this.dispatchEvent(new CustomEvent('seek', { bubbles: true, detail: { time: this._scrubTime } }));
        });
        bar.addEventListener('change', () => {
            this._scrubTime = null;
            this._paintProgress();
        });

        const volume = this.querySelector('.audio-volume');
        volume.addEventListener('input', () => {
            this.volume = Number(volume.value);
            this.dispatchEvent(new CustomEvent('volumechange', { bubbles: true, detail: { volume: this._volume } }));
        });
    }
}

if (!customElements.get('audio-widget')) { customElements.define('audio-widget', AudioWidget); }
}
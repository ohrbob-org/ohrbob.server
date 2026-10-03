class AudioWidget extends HTMLElement {
    constructor() {
        super();
        this.isPlaying = false;
    }

    connectedCallback() {
        // Add the base classes to the custom element itself
        this.classList.add('audio-widget', 'glass-panel', 'glass-edge');
        this.render();
    }

    static get observedAttributes() {
        return ['station', 'track', 'artist', 'cover'];
    }

    attributeChangedCallback(name, oldValue, newValue) {
        if (oldValue !== newValue) {
            this.render();
        }
    }

    render() {
        const rawStation = this.getAttribute('station');
        const rawTrack = this.getAttribute('track');
        const artist = this.getAttribute('artist') || '';
        const cover = this.getAttribute('cover');

        const isEmpty = !rawStation && !rawTrack;

        const station = rawStation || 'Ohrbob';
        const track = isEmpty ? 'Nothing to play right now' : (rawTrack || 'Unknown Track');

        const coverHtml = cover 
            ? `<img src="${cover}" alt="Track Cover" class="track-cover">` 
            : `<div class="track-cover d-flex align-items-center justify-content-center" style="background: rgba(255,255,255,0.1);"><span style="opacity:0.5;">♪</span></div>`;

        const playSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`;
        const pauseSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`;
        const prevSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="11 19 2 12 11 5 11 19"></polygon><polygon points="22 19 13 12 22 5 22 19"></polygon></svg>`;
        const nextSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="13 19 22 12 13 5 13 19"></polygon><polygon points="2 19 11 12 2 5 2 19"></polygon></svg>`;

        const disabledAttr = isEmpty ? 'disabled' : '';
        const opacityStyle = isEmpty ? 'style="opacity: 0.5;"' : '';

        this.innerHTML = `
            <div class="audio-header">
                <div class="station-name" ${opacityStyle}>
                    <span style="display: inline-block; width: 8px; height: 8px; background-color: var(--bs-primary); border-radius: 50%; box-shadow: 0 0 5px var(--bs-primary);"></span>
                    ${station}
                </div>
                <button class="btn-menu js-settings" aria-label="Settings" ${disabledAttr}>
                    <svg viewBox="0 0 24 24" width="16" height="16" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="3"></circle>
                        <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path>
                    </svg>
                </button>
            </div>
            <div class="audio-body" ${opacityStyle}>
                ${coverHtml}
                <div class="audio-info">
                    <strong>${track}</strong>
                    <small>${artist}</small>
                </div>
            </div>
            <div class="audio-controls mt-2">
                <button class="btn btn-glass rounded-circle btn-icon js-prev" ${disabledAttr}>${prevSvg}</button>
                <button class="btn btn-glass-primary rounded-circle btn-play js-play" ${disabledAttr}>${this.isPlaying ? pauseSvg : playSvg}</button>
                <button class="btn btn-glass rounded-circle btn-icon js-next" ${disabledAttr}>${nextSvg}</button>
            </div>
        `;

        this.attachEvents();
    }

    attachEvents() {
        const btnPlay = this.querySelector('.js-play');
        const btnPrev = this.querySelector('.js-prev');
        const btnNext = this.querySelector('.js-next');
        const btnSettings = this.querySelector('.js-settings');
        
        const playSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><polygon points="7 4 19 12 7 20 7 4"></polygon></svg>`;
        const pauseSvg = `<svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" stroke-width="2" fill="currentColor" stroke-linejoin="round" stroke-linecap="round"><rect x="6" y="4" width="4" height="16" rx="1"></rect><rect x="14" y="4" width="4" height="16" rx="1"></rect></svg>`;

        btnPlay.addEventListener('click', () => {
            if (btnPlay.disabled) return;
            
            this.isPlaying = !this.isPlaying;
            btnPlay.innerHTML = this.isPlaying ? pauseSvg : playSvg;
            
            // Re-adjust padding optical centering based on play or pause symbol
            if (this.isPlaying) {
                btnPlay.style.paddingLeft = '0';
            } else {
                btnPlay.style.paddingLeft = '2px';
            }

            this.dispatchEvent(new CustomEvent(this.isPlaying ? 'play' : 'pause'));
        });

        btnPrev.addEventListener('click', () => {
            if (btnPrev.disabled) return;
            this.dispatchEvent(new CustomEvent('back'));
        });

        btnNext.addEventListener('click', () => {
            if (btnNext.disabled) return;
            this.dispatchEvent(new CustomEvent('forward'));
        });

        btnSettings.addEventListener('click', () => {
            if (btnSettings.disabled) return;
            this.dispatchEvent(new CustomEvent('settings'));
        });
    }
}

// Register the custom element
customElements.define('audio-widget', AudioWidget);

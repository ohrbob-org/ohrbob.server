// Language switcher for the privacy policy page.
// Default comes from the browser language; an explicit choice is remembered per device.
(function () {
    var STORAGE_KEY = 'ohrbob_privacy_lang';
    var sections = {
        de: document.getElementById('privacy-de'),
        en: document.getElementById('privacy-en')
    };
    var label = document.getElementById('privacy-language-label');
    if (!sections.de || !sections.en) return;

    var labels = { de: 'Deutsch', en: 'English' };

    function apply(lang) {
        if (!sections[lang]) lang = 'en';
        Object.keys(sections).forEach(function (key) {
            sections[key].hidden = key !== lang;
        });
        if (label) label.textContent = labels[lang];
        document.documentElement.lang = lang;
    }

    function initial() {
        var stored = null;
        try { stored = localStorage.getItem(STORAGE_KEY); } catch (e) { }
        if (stored && sections[stored]) return stored;
        var browser = (navigator.language || 'en').toLowerCase();
        return browser.indexOf('de') === 0 ? 'de' : 'en';
    }

    document.querySelectorAll('[data-privacy-lang]').forEach(function (button) {
        button.addEventListener('click', function () {
            var lang = button.getAttribute('data-privacy-lang');
            try { localStorage.setItem(STORAGE_KEY, lang); } catch (e) { }
            apply(lang);
        });
    });

    apply(initial());
})();

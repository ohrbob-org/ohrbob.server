# Ohrbob.Web Metro Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle every Ohrbob.Web screen to the Ohrbob Metro design system shared with the Avalonia app and the `ohrbob-bootstrap` theme.

**Architecture:** Shared component rules live in `ohrbob-bootstrap/scss`, are compiled to `theme.css`, and are copied into the web app by the apply script. The web app keeps one hand-written stylesheet (`wwwroot/css/site.css`) for page-specific layout, using only theme tokens. Views are rewritten to Metro markup while every JavaScript/test hook stays stable. E2E Playwright tests pin each surface.

**Tech Stack:** Bootstrap 5.3 + Dart Sass, ASP.NET Core Razor MVC, HTMX 1.9, vanilla JS, Playwright/NUnit, Docker Compose for E2E dependencies.

**Spec:** `docs/superpowers/specs/2026-11-26-web-metro-restyle-design.md`

## Global Constraints

- Zero border radius on every surface: `border-radius: 0`; no box shadows except modals (`0 18px 60px rgba(0,0,0,.4)`); no blur/backdrop-filter anywhere.
- `theme.css` is generated from `ohrbob-bootstrap/scss`; never hand-edit it. Web-only CSS goes in `wwwroot/css/site.css` and uses `var(--metro-*)` tokens only — no new raw hex values.
- Always-dark surfaces are scoped by a `metro-night` body class; they ignore `data-bs-theme`.
- Keep all ids, `data-*` attributes, htmx attributes and JS class hooks listed in spec §6, including `#ohrbob-player`, `#main-content`, `#dockable-header`, `#rotationContainer`, `#resolvedList`, `#profileDropdown`, `.card-station-play`, `.card-*`, `.audio-*`, `.expanded-*`, `.episode-nav-*`.
- Bootstrap utility classes may be replaced by semantic classes (`.status-label`, `.toast.is-success`), but text assertions (`1 podcast`, `Host Pace`, `Subscribed`, `Compiling…`, `Empty`) stay visible.
- No JavaScript in `.cshtml`; no new inline `<style>`; move existing style blocks to `site.css`.
- Self-host Inter and Bootstrap Icons; no third-party font/CDN request at runtime.
- Playback must never be interrupted by navigation. Tiles/rows keep working with `hx-preserve`/`hx-boost`.
- E2E classes are `[NonParallelizable]`; fixtures use `Guid` suffixes; media is mocked as in the existing suite.
- Apply script must never overwrite `wwwroot/js/audio-widget.js` (the web copy is a superset).
- Every task ends green: `npm test` in the kit, `dotnet build`, `dotnet test Ohrbob.Web.Tests`, and the listed E2E filter.

## Review Focus

1. **Very long station/feed/episode titles at 320–390 px** must truncate (tile) or clamp (episode row) without horizontal overflow — pinned in Tasks 5, 6 and 13.
2. **Feed-colour 4 px bars on non-playing and completed episode rows** must always show, for every feed — pinned in Tasks 2, 6 and 11.
3. **HTMX navigation and browser Back on always-dark pages** must keep the night scope and `#main-content` history intact — pinned in Tasks 3, 4 and 10.
4. **Light-mode leakage on night pages and light-on-light leaks in dark mode** must not occur — pinned in Tasks 4, 10 and 12.
5. **Expanded player with long previous/next episode titles on a 390 px viewport** must not overflow and must stack the cards — pinned in Task 13.

## File Structure

Kit (`/home/kaiwegner/Projects/ohrbob-bootstrap`):

- `scss/theme.scss` — font faces, then Bootstrap, then the Metro layers.
- `scss/_metro.scss` — tokens, `.metro-night`, typography, shell primitives, forms, buttons.
- `scss/_cards.scss` — station/feed/episode/feed-row components.
- `scss/_audio-widget.scss` — transport bar, expanded sheet, web-only episode nav.
- `css/fonts/inter-*.woff2`, `css/fonts/OFL-Inter.txt` — vendored body font.
- `scripts/apply-theme.sh` — build + copy into the web app, never the audio widget.
- `test/*.test.mjs` — jsdom/CSS assertions.

Web (`/home/kaiwegner/Projects/ohrbob2/Ohrbob.Web`):

- `wwwroot/css/theme.css` — generated; do not edit.
- `wwwroot/css/site.css` — the only hand-written stylesheet (shell + all pages).
- `wwwroot/css/profiles.css`, `wwwroot/css/collage.css` — deleted; folded into `site.css`.
- `wwwroot/lib/bootstrap-icons/` — vendored icon CSS/font.
- `Views/Shared/_Layout.cshtml`, `_Layout_Minimal.cshtml`, delete `_Layout.cshtml.css` content.
- `Views/**` — per-surface markup as listed in the tasks.
- `wwwroot/js/site.js` — nav selector, toast status classes.
- `wwwroot/js/audio-widget.js` — drop the two glass classes only.
- `Ohrbob.E2ETests/**` — selector updates + one focused test class per surface.

## E2E prerequisites (used by every E2E step)

```bash
cd /home/kaiwegner/Projects/ohrbob2
docker compose up -d mongodb testcasts ohrbob.api ohrbob.player
OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 dotnet test Ohrbob.E2ETests --filter "FullyQualifiedName~<TestClass>"
```

---

### Task 1: Kit foundations — Inter font and night tokens

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/scss/theme.scss`
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/scss/_metro.scss`
- Add: `/home/kaiwegner/Projects/ohrbob-bootstrap/css/fonts/inter-{400,500,600,700}.woff2`
- Add: `/home/kaiwegner/Projects/ohrbob-bootstrap/css/fonts/OFL-Inter.txt`
- Test: `/home/kaiwegner/Projects/ohrbob-bootstrap/test/style.test.mjs`

**Interfaces:**
- Consumes: nothing.
- Produces: font family `"Inter"` at weights 400/500/600/700; tokens `--metro-night-surface: #080d18cc`, `--metro-night-border: #ffffff26`, `--metro-night-field: #ffffff14`, `--metro-night-field-rule: #ffffff40`, `--metro-night-text: #ffffff`, `--metro-night-muted: #ffffff99`, `--metro-night-faint: #ffffff66`, `--metro-night-accent: #ff9a00`; class `.metro-night` applying the dark canvas.

- [ ] **Step 1: Write the failing tests**

Append to `test/style.test.mjs`:

```js
test('Inter is bundled for the body text', () => {
    const css = compileTheme();
    for (const weight of [400, 500, 600, 700]) {
        assert.match(css, new RegExp(
            `@font-face[^}]*font-family:\\s*['"]Inter['"][^}]*font-weight:\\s*${weight}`, 's'));
        assert.ok(css.includes(`inter-${weight}.woff2`));
    }
});

test('night scope exposes the translated dark tokens', () => {
    const css = compileTheme();
    for (const token of [
        '--metro-night-surface', '--metro-night-border', '--metro-night-field',
        '--metro-night-field-rule', '--metro-night-text', '--metro-night-muted',
        '--metro-night-faint', '--metro-night-accent',
    ]) {
        assert.ok(css.includes(token), token);
    }
    assert.match(css, /\.metro-night\s*{[^}]*--metro-canvas:\s*#0e0f13/s);
    assert.match(css, /\.metro-night\s*{[^}]*color-scheme:\s*dark/s);
    assert.ok(!css.includes('#cc080d18'), 'ARGB values must never be copied into CSS');
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /home/kaiwegner/Projects/ohrbob-bootstrap && npm test`
Expected: FAIL — missing `@font-face` for Inter and missing night tokens.

- [ ] **Step 3: Vendor Inter**

Fetch the Google Fonts CSS for `Inter:wght@400;500;600;700`, download the four latin `.woff2` files into `css/fonts/` as `inter-400.woff2` … `inter-700.woff2`, and save the OFL text as `css/fonts/OFL-Inter.txt`.

Add to the top of `scss/theme.scss`, before the existing Sulphur Point faces, one `@font-face` per weight:

```scss
@font-face {
  font-family: 'Inter';
  font-style: normal;
  font-weight: 400;
  font-display: swap;
  src: url('../css/fonts/inter-400.woff2') format('woff2');
}
```

Repeat for 500/600/700.

- [ ] **Step 4: Add the night scope and tokens**

In `scss/_metro.scss`, after the `:root` block, add the night tokens to `:root`:

```scss
--metro-night-surface: #080d18cc;
--metro-night-border: #ffffff26;
--metro-night-field: #ffffff14;
--metro-night-field-rule: #ffffff40;
--metro-night-text: #ffffff;
--metro-night-muted: #ffffff99;
--metro-night-faint: #ffffff66;
--metro-night-accent: #ff9a00;
```

After the `prefers-color-scheme` block, add:

```scss
.metro-night {
  @include metro-dark-tokens;
  --metro-night-accent: #ff9a00;
  color-scheme: dark;
}
```

- [ ] **Step 5: Run tests and build**

Run: `npm test && npm run build`
Expected: PASS; `css/theme.css` contains the new font faces and night tokens.

- [ ] **Step 6: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob-bootstrap
git add scss/theme.scss scss/_metro.scss css/fonts css/theme.css css/theme.css.map docs test/style.test.mjs
git commit -m "feat(theme): bundle Inter and add the always-dark Metro scope"
```

---

### Task 2: Kit components — feed-colour bars, widget extras, safe apply

**Files:**
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/scss/_cards.scss`
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/scss/_audio-widget.scss`
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/scripts/apply-theme.sh`
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/test/cards.test.mjs`
- Modify: `/home/kaiwegner/Projects/ohrbob-bootstrap/test/widget.test.mjs`
- Add: `/home/kaiwegner/Projects/ohrbob-bootstrap/test/apply.test.mjs`

**Interfaces:**
- Consumes: Task 1 tokens.
- Produces: `.card-episode::before` always paints `var(--card-episode-accent)`; `.card-station-feed` has `border-left: 4px solid var(--station-feed-accent)`; `.episode-nav-row`, `.station-link-icon`, squared `rounded-circle`; apply script copies fonts and never `js/audio-widget.js`.

- [ ] **Step 1: Update the failing card tests**

In `test/cards.test.mjs`, replace the test `current episode tab is orange and the custom property is overridable` with:

```js
test('every episode row carries its feed colour bar', () => {
    const window = cardWindow(`
        <div class="card-episode is-playing"></div>
        <div class="card-episode" style="--card-episode-accent: #E63946"></div>`);
    const cards = [...window.document.querySelectorAll('.card-episode')];
    assert.equal(window.getComputedStyle(cards[0]).getPropertyValue('--card-episode-accent').trim(), '#fc5d00');
    assert.equal(window.getComputedStyle(cards[1]).getPropertyValue('--card-episode-accent').trim(), '#E63946');
    const css = compileTheme();
    assert.match(css, /\.card-episode::before\s*{[^}]*background:\s*var\(--card-episode-accent\)/s);
    assert.doesNotMatch(css, /\.card-episode\.is-playing::before\s*{[^}]*background:\s*var\(--card-episode-accent\)/s);
});
```

Add:

```js
test('station editor rows keep a 4px feed colour border', () => {
    const window = cardWindow(
        `<div class="card-station-feed" style="--station-feed-accent: #E63946"></div>`);
    const row = window.document.querySelector('.card-station-feed');
    assert.equal(window.getComputedStyle(row).borderLeftWidth, '4px');
    assert.match(compileTheme(),
        /\.card-station-feed\s*{[^}]*border-left:\s*4px\s+solid\s+var\(--station-feed-accent\)/s);
});
```

- [ ] **Step 2: Add the failing widget and apply tests**

Append to `test/widget.test.mjs`:

```js
test('web-only episode navigation and station links are styled by the kit', () => {
    const css = compileTheme();
    for (const selector of ['.episode-nav-row', '.station-link-icon', '.prev-episode-card', '.next-episode-card']) {
        assert.ok(css.includes(selector), selector);
    }
    assert.match(css, /\.episode-nav-row\s*{[^}]*display:\s*grid/s);
    assert.match(css, /\.audio-widget[\s\S]*?\.rounded-circle\s*{[^}]*border-radius:\s*0/s);
});
```

Create `test/apply.test.mjs`:

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');

test('apply script preserves the web audio widget and ships the fonts', () => {
    const sh = readFileSync(path.join(root, 'scripts/apply-theme.sh'), 'utf8');
    assert.ok(!/cp\s+"?js\/audio-widget\.js/.test(sh), 'must not overwrite the web audio widget');
    assert.ok(sh.includes('css/fonts'), 'must copy the bundled fonts');
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `npm test`
Expected: FAIL on the episode-bar regex, feed-row border, widget selectors and apply script.

- [ ] **Step 4: Implement the kit rules**

In `_cards.scss`:
- Change `.card-episode::before` `background: transparent;` to `background: var(--card-episode-accent);`.
- Delete the `.card-episode.is-playing::before` background rule (keep `.is-playing` styling on the play button/hover).
- Remove the `--card-episode-accent: var(--metro-hairline);` override from `.card-episode.is-played` so completed rows keep their feed colour (keep the opacity/strikethrough).
- Add `border-left: 4px solid var(--station-feed-accent);` to `.card-station-feed` after its `border` declaration.

In `_audio-widget.scss`, outside `.audio-widget`, add:

```scss
.audio-widget .btn.rounded-circle { border-radius: 0 !important; }
.episode-nav-row { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.episode-nav-row > :only-child { grid-column: 1 / -1; }
.station-link-icon { color: var(--metro-orange); display: inline-flex; }
@media (max-width: 575.98px) { .episode-nav-row { grid-template-columns: 1fr; } }
```

In `scripts/apply-theme.sh`, replace the `cp "js/audio-widget.js"` block with a comment explaining the web copy is a superset. Leave the existing font copy logic in place (it already copies `css/fonts`).

- [ ] **Step 5: Run tests and build**

Run: `npm test && npm run build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
cd /home/kaiwegner/Projects/ohrbob-bootstrap
git add scss/_cards.scss scss/_audio-widget.scss scripts/apply-theme.sh test css/theme.css css/theme.css.map docs
git commit -m "feat(theme): always-on feed colours and web episode navigation"
```

---

### Task 3: Web foundation — shell, assets, generated theme

**Files:**
- Generate: `Ohrbob.Web/wwwroot/css/theme.css`, `wwwroot/css/fonts/inter-*.woff2`, `Ohrbob.Web/docs/bootstrap-theme.html`
- Add: `Ohrbob.Web/wwwroot/lib/bootstrap-icons/bootstrap-icons.min.css`, `.../fonts/bootstrap-icons.woff2`
- Rewrite: `Ohrbob.Web/wwwroot/css/site.css` (shell/base sections only; later tasks append)
- Delete: `Ohrbob.Web/wwwroot/css/profiles.css`, `wwwroot/css/collage.css` (fold their rules into `site.css` as-is in this task; Tasks 5 and 8 restyle them), `Ohrbob.Web/Views/Shared/_Layout.cshtml.css`
- Modify: `Ohrbob.Web/Views/Shared/_Layout.cshtml`, `_Layout_Minimal.cshtml`
- Modify: `Ohrbob.Web/Views/Shared/Components/ProfileSelector/Default.cshtml`
- Modify: `Ohrbob.Web/wwwroot/js/site.js`, `wwwroot/js/audio-widget.js`
- Test: `Ohrbob.E2ETests/MetroShellTests.cs` (new), `LandingPageTests.cs`, `NavigationTests.cs`, `AudioPlaybackTests.cs`, `CompilingStationTests.cs`, `ThemeTests.cs`

**Interfaces:**
- Consumes: theme.css from Task 2; `--metro-*` tokens.
- Produces: `.app-header`, `.app-nav`, `.app-nav-link`, `.app-actions`, `.metro-brandline`, `.app-footer`, `.metro-toast` + `.is-success`/`.is-danger`; `body.metro-night`; `ViewData["BodyClass"]`; `#main-content` stays `.metro-page`.

- [ ] **Step 1: Write the failing shell tests**

Create `MetroShellTests.cs` following the `NavigationTests` setup (unique account, `TestAuthHandler.TestAccountId`). Tests:

```csharp
[Test]
public async Task Shell_UsesFlatMetroAppBar()
{
    await Page.GotoAsync(_factory.ServerAddress);
    await Expect(Page.Locator(".app-header")).ToBeVisibleAsync();
    await Expect(Page.Locator(".floating-nav-pill")).ToHaveCountAsync(0);
    await Expect(Page.Locator(".app-nav-link")).ToHaveCountAsync(2);
    var css = await Page.EvaluateAsync<string[]>(@"() => {
        const h = getComputedStyle(document.querySelector('.app-header'));
        const l = getComputedStyle(document.querySelector('.app-nav-link'));
        return [h.backdropFilter, h.borderBottomLeftRadius, l.borderBottomLeftRadius];
    }");
    Assert.That(css[0], Is.EqualTo("none"));
    Assert.That(css[1], Is.EqualTo("0px"));
    Assert.That(css[2], Is.EqualTo("0px"));
}

[Test]
public async Task Toast_UsesMetroStatusClasses()
{
    await Page.GotoAsync(_factory.ServerAddress);
    await Page.EvaluateAsync(@"() => document.body.dispatchEvent(
        new CustomEvent('showToast', { detail: { message: 'Saved', type: 'success' } }))");
    var toast = Page.Locator("#liveToast");
    await Expect(toast).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-success\b"));
    await Expect(toast).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbg-success\b"));
}
```

- [ ] **Step 2: Run the shell tests to verify they fail**

Run: `OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 dotnet test Ohrbob.E2ETests --filter "FullyQualifiedName~MetroShellTests"`
Expected: FAIL — `.app-header` missing, toast has `bg-success`.

- [ ] **Step 3: Sync the generated theme and assets**

From the kit: `npm run apply -- /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web/`. Confirm `js/audio-widget.js` was not touched (check `git diff --stat`).
Vendor Bootstrap Icons 1.11.3 CSS + woff2 into `wwwroot/lib/bootstrap-icons/`; the CSS must reference `./fonts/bootstrap-icons.woff2`.
Delete `profiles.css`, `collage.css`, `_Layout.cshtml.css`, and remove their `<link>`/`Ohrbob.Web.styles.css` tags from both layouts. Copy the old `profiles.css` and `collage.css` rule bodies into `site.css` unchanged first (they are retrieved from `git show aac62d9:Ohrbob.Web/wwwroot/css/<file>` before deletion), so no page loses its styling between tasks.

- [ ] **Step 4: Rewrite the shell**

In `_Layout.cshtml`, replace the floating pill with:

```html
<body class="metro-app @ViewData["BodyClass"] @(User.Identity?.IsAuthenticated == true ? "with-player" : "")"
      hx-boost="true" hx-target="#main-content">
<header class="app-header">
  <div class="metro-page app-header__inner">
    <a class="metro-brandline" asp-controller="Home" asp-action="Index">…logo + wordmark…</a>
    <nav class="app-nav" aria-label="Primary">…discover / listen links with class app-nav-link…</nav>
    <div class="app-actions">…overflow dropdown, ProfileSelector or Login…</div>
  </div>
</header>
<div class="metro-page" id="main-content" hx-history-elt><main role="main">@RenderBody()</main></div>
<footer class="app-footer">…four link columns + legal row…</footer>
```

Keep every `hx-boost`, `hx-history-elt`, `#confirmationModal`, `#liveToast`, `#toast-body-text`, `#profileDropdown` hook and the `PlayerWidget`/`ChangelogPopup` invocations. Change the toast root to `class="toast metro-toast"` and add `<div class="metro-modal-accent"></div>` as the first child of `#confirmationModal .modal-content`.

In `site.js`: change the two `.floating-nav-pill-center a` selectors to `.app-nav-link`, and replace the toast `bg-success`/`bg-danger` toggle with `is-success`/`is-danger`.

In `audio-widget.js` `connectedCallback`, remove `glass-panel glass-edge` from the class list (keep `audio-widget`).

In `ProfileSelector`, keep `#profileDropdown` and `[data-profile-id]`, and use `.profile-avatar` + `.profile-menu` markup.

Write the shell/base part of `site.css`: import nothing; define `body.with-player` bottom padding, `.metro-page`, `.app-header(.is-night)`, `.app-nav(-link)`, `.app-actions`, `.profile-menu`, `.app-footer`, `.metro-toast`, `.metro-modal-accent`, the `#htmx-progress` bar, and a `.metro-night` override for header/footer. No radius, no shadows.

- [ ] **Step 5: Update the existing selectors**

- `LandingPageTests.cs`: `.floating-nav-pill` → `.app-header`.
- `NavigationTests.cs`, `AudioPlaybackTests.cs`: `.floating-nav-pill-center a` → `.app-nav-link`.
- `CompilingStationTests.cs`: `bg-danger`/`bg-success` → `is-danger`/`is-success`.

- [ ] **Step 6: Run build, unit tests and the shell E2E tests**

Run:

```bash
cd /home/kaiwegner/Projects/ohrbob2
dotnet build
dotnet test Ohrbob.Web.Tests
OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 dotnet test Ohrbob.E2ETests --filter "FullyQualifiedName~MetroShellTests|FullyQualifiedName~LandingPageTests|FullyQualifiedName~NavigationTests"
```

Expected: PASS. `Theme_ScriptRunsInHeadBeforeStylesheets` still passes (theme.js stays before `theme.css` in `<head>`).

- [ ] **Step 7: Commit**

```bash
git add Ohrbob.Web Ohrbob.E2ETests
git commit -m "feat(web): adopt the Metro shell and generated theme"
```

---

### Task 4: Landing page — dark Metro poster

**Files:**
- Modify: `Ohrbob.Web/Views/Home/Index.cshtml` (anonymous branch)
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (landing section)
- Test: `Ohrbob.E2ETests/LandingPageTests.cs`

**Interfaces:**
- Consumes: `.metro-night`, `.metro-title`, `.btn-primary`, Task 3 shell.
- Produces: `.landing-hero`, `.landing-bars`, `.landing-kicker`, `.landing-title`, `.landing-lead`, `.landing-cta`; `.radio-step`, `.step-index`, `.screenshot-card`, `.landing-tiers`, `.landing-tier-table`; `body.metro-night`.

- [ ] **Step 1: Write the failing landing tests**

In `LandingPageTests.cs`, add:

```csharp
[Test]
public async Task LandingPage_UsesDarkMetroPoster()
{
    _factory.CreateClient();
    await Page.GotoAsync(_factory.ServerAddress);

    await Expect(Page.Locator("body.metro-night")).ToHaveCountAsync(1);
    await Expect(Page.Locator(".landing-hero .landing-title#landing-title")).ToBeVisibleAsync();

    var m = await Page.EvaluateAsync<double[]>(@"() => {
        const body = getComputedStyle(document.body);
        const shot = document.querySelector('.screenshot-card');
        const title = getComputedStyle(document.querySelector('.landing-title'));
        const m = body.backgroundColor.match(/[\d.]+/g).slice(0, 3).map(Number);
        return [ (0.2126*m[0] + 0.7152*m[1] + 0.0722*m[2]) / 255,
                 parseFloat(shot ? getComputedStyle(shot).borderRadius : '0'),
                 parseFloat(title.fontSize) ];
    }");
    Assert.That(m[0], Is.LessThan(0.25), "landing background must be dark");
    Assert.That(m[1], Is.EqualTo(0), "screenshots must be square");
    Assert.That(m[2], Is.GreaterThan(56), "hero title must be display-sized");
}

[Test]
public async Task LandingPage_LongHeroFitsNarrowPhones()
{
    _factory.CreateClient();
    await Page.SetViewportSizeAsync(320, 800);
    await Page.GotoAsync(_factory.ServerAddress);
    var overflow = await Page.EvaluateAsync<bool>(
        @"() => { const el = document.querySelector('.landing-title'); return el.scrollWidth > el.clientWidth + 1; }");
    Assert.That(overflow, Is.False, "the hero headline must wrap, not scroll sideways");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 dotnet test Ohrbob.E2ETests --filter "FullyQualifiedName~LandingPageTests"`
Expected: FAIL — no `body.metro-night`, no `.landing-title`, old hero.

- [ ] **Step 3: Rewrite the anonymous branch**

Set `ViewData["BodyClass"] = "metro-night"` only when the user is anonymous. Replace the stacked logo + display-4 hero with a `.landing-hero` containing `.landing-bars` (three spans), `.metro-kicker landing-kicker`, `h1#landing-title.landing-title.metro-title`, `.landing-lead`, and `.landing-cta` (orange `btn-primary`, ghost link). Keep the `#features` anchor, `hx-boost="false"` on CTAs, all six feature steps (`radio-step`, `step-left/right`, `step-text-box`, `screenshot-card`), tier table and closing CTA. Keep the screenshot `File.Exists` guard.

- [ ] **Step 4: Write the landing CSS**

Append a landing section to `site.css`: `.metro-night` gradient page, decorative bars, `clamp(3.5rem, 9vw, 7.5rem)` title, 14–16 px lead, hairline `border-top` sections, `step-index` 44 px Sulphur in `--metro-muted`, flat 1 px hairline screenshot frames with a 46×5 orange bar, uppercase 10 px table headers, hairline row rules, orange left border on the morebob column, and a giant closing wordmark. No radius/shadow.

- [ ] **Step 5: Run the tests**

Run: same filter.
Expected: PASS, including the two older landing tests.

- [ ] **Step 6: Commit**

```bash
git add Ohrbob.Web/Views/Home/Index.cshtml Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests/LandingPageTests.cs
git commit -m "feat(web): turn the landing page into a Metro poster"
```

---

### Task 5: Listen grid, Stations index and Station create

**Files:**
- Modify: `Ohrbob.Web/Views/Home/Index.cshtml` (authenticated branch)
- Modify: `Ohrbob.Web/Views/Stations/Index.cshtml`, `Views/Stations/Create.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (stations section)
- Test: `Ohrbob.E2ETests/MetroListenTests.cs` (new), `ListenEmptyBadgeTests.cs`, `StationSharingTests.cs`, `ThemeTests.cs`

**Interfaces:**
- Consumes: Task 3 shell; theme `.station-grid`/`.card-station`; `.status-label`.
- Produces: `.page-head`, `.station-add`, `.status-label.is-empty`, `.status-label.is-pacing`, `.status-label.is-subscribed`, `.form-narrow`; visible `.card-station-count`.

- [ ] **Step 1: Write the failing listen tests**

Create `MetroListenTests.cs` (setup like `ListenEmptyBadgeTests`, with one testcast station). Tests:

```csharp
[Test]
public async Task Listen_UsesMetroStationGrid()
{
    await Page.GotoAsync(_factory.ServerAddress);
    var card = Page.Locator(".card-station").First;
    await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
    await Expect(Page.Locator(".station-grid")).ToBeVisibleAsync();
    await Expect(Page.Locator(".page-head .metro-title")).ToBeVisibleAsync();

    var m = await Page.EvaluateAsync<double[]>(@"() => {
        const card = document.querySelector('.card-station');
        const title = getComputedStyle(card.querySelector('.card-station-title'));
        const play = getComputedStyle(card.querySelector('.card-station-play'));
        const count = getComputedStyle(card.querySelector('.card-station-count'));
        return [ parseFloat(getComputedStyle(card).borderRadius),
                 parseFloat(title.fontSize), title.whiteSpace === 'nowrap' ? 1 : 0,
                 parseFloat(play.width), count.display === 'none' ? 0 : 1 ];
    }");
    Assert.That(m[0], Is.EqualTo(0), "tile radius");
    Assert.That(m[1], Is.GreaterThanOrEqualTo(27), "tile title");
    Assert.That(m[2], Is.EqualTo(1), "tile title truncates");
    Assert.That(m[3], Is.GreaterThanOrEqualTo(44), "play target");
    Assert.That(m[4], Is.EqualTo(1), "podcast count stays visible");
}

[Test]
public async Task Listen_LongStationNameStaysOnOneLine()
{
    // create a station named new string('W', 90) via StationsApiService, then:
    await Page.SetViewportSizeAsync(390, 844);
    await Page.GotoAsync(_factory.ServerAddress);
    var ok = await Page.EvaluateAsync<bool>(@"() => {
        const t = document.querySelector('.card-station-title');
        return t.scrollWidth <= t.clientWidth + 1 && t.getBoundingClientRect().right <=
               t.closest('.card-station').getBoundingClientRect().right + 1;
    }");
    Assert.That(ok, Is.True);
}
```

Update `ListenEmptyBadgeTests` and the Listen-side assertions in `StationSharingTests`: `.badge.bg-danger` → `.status-label.is-empty`; `.badge` for Host Pace/Subscribed on station cards → `.status-label`. Leave the share-page hero badge assertions for Task 11.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroListenTests|FullyQualifiedName~ListenEmptyBadgeTests|FullyQualifiedName~StationSharingTests"`
Expected: FAIL — no `.station-grid`/`.page-head`, count hidden, badges use Bootstrap classes.

- [ ] **Step 3: Rewrite the Listen branch and Stations index**

Use a `.page-head` with kicker `your stations`, `h1.metro-title.metro-title--compact` (`stations`), and an orange `new station` link to `Stations/Create`. Replace the `row/col` wrappers with a `.station-grid`; keep the card structure, `data-station-id`, `.card-station-play`, cover `onerror`, `is-active`, unsubscribe form. Move count and badges into one `.card-station-meta` line using `.status-label` (`.is-subscribed`, `.is-pacing`, `.is-compiling`, `.is-empty`). Replace the circular Discover plus with `<a class="station-add" asp-controller="Discover">`.

`Stations/Index` uses the same grid plus a hairline footer with Edit/Delete; `Stations/Create` uses `.page-head` + `.form-narrow` and keeps `#feedsContainer`, `.feed-item`, `.remove-feed`, `#addFeedBtn`.

- [ ] **Step 4: Write the station CSS**

Append to `site.css`: `.page-head` layout, `.station-grid` gap overrides, visible `.card-station-count` as 10 px uppercase muted, `.status-label` (10 px, 700, +0.08em, uppercase; `.is-empty`/`.is-danger` red, `.is-pacing`/`.is-subscribed` violet, `.is-compiling` yellow/navy), `.card-station-play` 46 px orange square (visible on hover/focus, always visible under `(hover: none)`), `.card-station.is-active` inset ring, `.station-add` square hairline tile, `.form-narrow` max 560 px. Restyle the collage rules already folded into `site.css` in Task 3: square cells, 2 px hairline gaps, violet fallback, hover changes border colour only.

- [ ] **Step 5: Run the tests**

Run: same filter.
Expected: PASS, including existing playback tests that click `.card-station-play`.

- [ ] **Step 6: Commit**

```bash
git add Ohrbob.Web/Views Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro station grid, index and create pages"
```

---

### Task 6: Station editor

**Files:**
- Modify: `Ohrbob.Web/Views/Stations/Edit.cshtml` (remove the inline `<style>` block, adjust classes)
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (editor section)
- Test: `Ohrbob.E2ETests/MetroEditorTests.cs` (new), `StationsEditTests.cs`, `PlaybackOrderTests.cs`, `EpisodePlayheadTests.cs`

**Interfaces:**
- Consumes: kit feed/episode rows from Task 2.
- Produces: `.editor-header`, `.editor-tabs`, `.editor-pane`, `.editor-sharebar`; keeps `#dockable-header`, `#dock-sentinel`, `#rotationContainer`, `#resolvedList`, `.pill-nav`, all modal ids.

- [ ] **Step 1: Write the failing editor tests**

Create `MetroEditorTests.cs` (setup like `StationsEditTests`, one feed, one episode) with:

```csharp
[Test]
public async Task EditorHeaderAndTabsAreFlat()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
    var m = await Page.EvaluateAsync<string[]>(@"() => {
        const h = getComputedStyle(document.querySelector('#dockable-header'));
        const t = getComputedStyle(document.querySelector('#rotation-tab'));
        return [h.backdropFilter, h.backgroundColor, h.borderBottomWidth,
                t.borderBottomWidth, t.borderBottomColor];
    }");
    Assert.That(m[0], Is.EqualTo("none"));
    Assert.That(m[3], Is.EqualTo("2px"));
    Assert.Matches("rgb\\(252, 93, 0\\)", m[4]);
}

[Test]
public async Task FeedAndEpisodeRowsCarryFeedColours()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
    var feed = await Page.EvaluateAsync<string>(
        @"() => getComputedStyle(document.querySelector('.card-station-feed')).borderLeftWidth");
    Assert.That(feed, Is.EqualTo("4px"));

    await Page.SetViewportSizeAsync(375, 812);
    await Page.ClickAsync("#resolved-tab");
    var bar = await Page.EvaluateAsync<string>(@"() => {
        const el = document.querySelector('#resolvedList .card-episode');
        return getComputedStyle(el, '::before').backgroundColor;
    }");
    Assert.That(bar, Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "non-playing rows must still show a feed bar");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroEditorTests"`
Expected: FAIL — glass header has a backdrop filter, no 4 px feed border, transparent episode bar.

- [ ] **Step 3: Restyle the editor**

Delete lines 33–112 (`<style>` block) from `Edit.cshtml` and move the rules to `site.css`. Restyle `#dockable-header` as `.editor-header` (canvas, 1 px bottom hairline, no blur/shadow/radius, sticky 90/62 px preserved via the `docked` class), `.pill-nav` as `.editor-tabs` (uppercase 11 px, 2 px orange active underline, no pill), both panes on canvas with a 1 px divider, feed rows/episode rows as before with the theme classes, and all modals with the Metro modal shell. Replace remaining inline colours (`#6c757d`, `rgba(255,255,255,.95)`, `#ccc`, `#ff9a00` fallbacks) with tokens, keeping `--station-feed-accent`/`--card-episode-accent`. Change the episode row inline style so `--card-episode-accent` is set for **every** row, including completed ones (`style="--card-episode-accent: @feedColor;"`), while `data-feed-color` stays.

- [ ] **Step 4: Run the editor tests**

Run: `... --filter "FullyQualifiedName~MetroEditorTests|FullyQualifiedName~StationsEditTests|FullyQualifiedName~PlaybackOrderTests|FullyQualifiedName~EpisodePlayheadTests"`
Expected: PASS; drag order, span inputs, playhead and modals unchanged.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/Views/Stations/Edit.cshtml Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro station editor"
```

---

### Task 7: Discover

**Files:**
- Modify: `Ohrbob.Web/Views/Discover/Index.cshtml`, `_PodcastCard.cshtml`, `_SearchResults.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (discover section)
- Test: `Ohrbob.E2ETests/MetroDiscoverTests.cs` (new), `DiscoverSearchNavigationTests.cs`, `CompilingStationTests.cs`

**Interfaces:**
- Consumes: kit `.card-feed`; Task 3 shell.
- Produces: `.discover-search`, `.discover-lane__title`, `.category-chip`; keeps `#discover-search`, `#recommendation-lanes`, `#category-roster`, `#search-results`, `.podcast-card`, `.podcast-details-toggle`, `data-*` hooks.

- [ ] **Step 1: Write the failing Discover tests**

Create `MetroDiscoverTests.cs`:

```csharp
[Test]
public async Task Discover_UsesFlatCardsAndUnderlineSearch()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
    await Expect(Page.Locator(".page-head .metro-title")).ToBeVisibleAsync();
    var m = await Page.EvaluateAsync<string[]>(@"() => {
        const s = getComputedStyle(document.querySelector('#discover-search'));
        const c = document.querySelector('.discover-lane .card-feed');
        const add = c && c.querySelector('.card-feed-add');
        return [s.borderBottomLeftRadius, s.borderBottomWidth,
                parseFloat(getComputedStyle(c).borderRadius),
                add ? parseFloat(getComputedStyle(add).width) : 0];
    }");
    Assert.That(m[0], Is.EqualTo("0px"));
    Assert.That(m[1], Is.EqualTo("2px"));
    Assert.That(m[2], Is.EqualTo(0));
    Assert.That(m[3], Is.GreaterThanOrEqualTo(44));
}

[Test]
public async Task Discover_LongFeedTitleTruncates()
{
    await Page.SetViewportSizeAsync(390, 844);
    await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
    var ok = await Page.EvaluateAsync<bool>(@"() => {
        const t = document.querySelector('.discover-lane .card-feed-title');
        return !t || t.scrollWidth <= t.clientWidth + 1;
    }");
    Assert.That(ok, Is.True);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroDiscoverTests"`
Expected: FAIL — search has no 2 px rule / old rounded cards.

- [ ] **Step 3: Restyle Discover**

Replace the hero search card with `.page-head` + a single `.discover-search` input row (icon + underline field). Restyle lanes as hairline shelves with `.discover-lane__title`, `.card-feed` cards with square covers, a 44 px orange add button, a flat ink details overlay; category roster as `.category-chip`. Keep every htmx/`data-*`/modal hook.

- [ ] **Step 4: Run the Discover tests**

Run: `... --filter "FullyQualifiedName~MetroDiscoverTests|FullyQualifiedName~DiscoverSearchNavigationTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/Views/Discover Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro discover page"
```

---

### Task 8: Profiles and colour picker

**Files:**
- Modify: `Ohrbob.Web/Views/Profiles/Index.cshtml`, `Create.cshtml`, `Edit.cshtml`, `_ColorPicker.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (profiles section; rules already folded in Task 3)
- Test: `Ohrbob.E2ETests/MetroProfilesTests.cs` (new), `ProfilesTests.cs`

**Interfaces:**
- Consumes: kit station mosaics; theme tokens.
- Produces: `.profile-card`, `.profile-active`, `.profile-color-swatch` (32 px), `.profile-color-preview` (96 px), `.profile-station-tile`; keeps `data-profile-id`, `data-profile-color`, `data-color*`, `btn-check` + label hooks and `profile-color.js`.

- [ ] **Step 1: Write the failing profile tests**

Create `MetroProfilesTests.cs` (copy the setup from `ProfilesTests`):

```csharp
[Test]
public async Task ProfileCardsAndPickerAreMetro()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Profiles");
    var card = Page.Locator(".profile-card").First;
    await Expect(card).ToBeVisibleAsync();
    var m = await Page.EvaluateAsync<double[]>(@"() => {
        const card = document.querySelector('.profile-card');
        const avatar = card.querySelector('.profile-avatar');
        return [parseFloat(getComputedStyle(card).borderRadius),
                parseFloat(getComputedStyle(avatar).width),
                parseFloat(getComputedStyle(avatar).borderTopLeftRadius)];
    }");
    Assert.That(m, Is.EqualTo(new[] { 0d, 64d, 0d }));

    await Page.GotoAsync($"{_factory.ServerAddress}/Profiles/Create");
    var swatch = await Page.EvaluateAsync<double[]>(@"() => {
        const s = document.querySelector('.profile-color-swatch');
        const p = document.querySelector('.profile-color-preview');
        return [parseFloat(getComputedStyle(s).width), parseFloat(getComputedStyle(s).borderRadius),
                parseFloat(getComputedStyle(p).width)];
    }");
    Assert.That(swatch[0], Is.EqualTo(32));
    Assert.That(swatch[1], Is.EqualTo(0));
    Assert.That(swatch[2], Is.EqualTo(96));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `... --filter "FullyQualifiedName~MetroProfilesTests"`
Expected: FAIL — no `.profile-card`, rounded avatar/swatches.

- [ ] **Step 3: Restyle profiles**

Index: wrap each profile in `.profile-card` (tile surface, 1 px hairline), `.profile-card .profile-avatar` at 64 px square (the header avatar stays 34 px), 24 px Sulphur name, `.profile-active` uppercase orange label, mono token, `.profile-card__actions` footer. Edit: two-column layout, `_ColorPicker` with 32 px square swatches, 2 px orange `is-selected` outline, square conic custom swatch, 96 px square preview. Allowed stations: `.profile-station-tile` using the station mosaic; checked = 2 px orange inset ring + 24 px check square.

- [ ] **Step 4: Run the profile tests**

Run: `... --filter "FullyQualifiedName~MetroProfilesTests|FullyQualifiedName~ProfilesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/Views/Profiles Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro profiles and colour picker"
```

---

### Task 9: Account

**Files:**
- Modify: `Ohrbob.Web/Views/Account/Index.cshtml`, `Delete.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (account section)
- Test: `Ohrbob.E2ETests/MetroAccountTests.cs` (new), `ThemeTests.cs`, `AccountBillingTests.cs`, `AccountDeletionTests.cs`

**Interfaces:**
- Consumes: Task 3 shell/modals.
- Produces: `.account-tile`, `.account-panel`, `.theme-choice`, `.morebob-panel`, `.api-key-row`, `.danger-panel`; keeps `#accountTabs*`, `.account-tab-link`, `#themeSystem/#themeLight/#themeDark`, QR ids and `showConnectionQr`.

- [ ] **Step 1: Write the failing account tests**

Create `MetroAccountTests.cs` (setup like `ThemeTests`, with a **free-tier** account so the upgrade panel renders):

```csharp
[Test]
public async Task AccountTabsAndUpgradeCardAreMetro()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Account");
    await Expect(Page.Locator(".account-tile")).ToHaveCountAsync(5);
    var m = await Page.EvaluateAsync<double[]>(@"() => {
        const tile = document.querySelector('.account-tile');
        const panel = document.querySelector('.morebob-panel');
        return [parseFloat(getComputedStyle(tile).borderRadius),
                panel ? 1 : 0,
                panel ? getComputedStyle(panel).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number)[0] : 0,
                panel ? getComputedStyle(panel).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number)[1] : 0];
    }");
    Assert.That(m[0], Is.EqualTo(0));
    Assert.That(m[1], Is.EqualTo(1));
    Assert.That(m[2], Is.EqualTo(253));
    Assert.That(m[3], Is.EqualTo(178));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `... --filter "FullyQualifiedName~MetroAccountTests"`
Expected: FAIL — old card markup, gradient upgrade card.

- [ ] **Step 3: Restyle Account**

Replace the overview cards with five `.account-tile` elements inside a 5/3/2/1 grid; active tile gets an orange bottom rule. Panes use `.account-panel` sections with hairline rules, uppercase 11 px labels, hairline tables, mono keys, square buttons. Appearance becomes three `.theme-choice` tiles. Replace the upgrade gradient with `.morebob-panel` (`#fdb200`, navy text, 46×5 orange bar, orange CTA, no watermark). QR modal uses the Metro shell. Delete page uses `.danger-panel` with a red 4 px left border.

- [ ] **Step 4: Run the account tests**

Run: `... --filter "FullyQualifiedName~MetroAccountTests|FullyQualifiedName~ThemeTests|FullyQualifiedName~AccountBillingTests|FullyQualifiedName~AccountDeletionTests"`
Expected: PASS, including theme switching and contrast checks.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/Views/Account Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro account dashboard"
```

---

### Task 10: Onboarding, Login, Connect and Handoff

**Files:**
- Modify: `Ohrbob.Web/Views/Home/Login.cshtml` (remove inline `<style>` and colours)
- Modify: `Ohrbob.Web/Views/Onboarding/Index.cshtml`, `Feeds.cshtml`, `Station.cshtml`, `_FeedSearchResults.cshtml`
- Modify: `Ohrbob.Web/Views/Connect/App.cshtml`, `Handoff.cshtml`
- Modify: `Ohrbob.Web/Views/Shared/_Layout_Minimal.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (night/auth/onboarding/connect sections)
- Test: `Ohrbob.E2ETests/MetroAuthTests.cs` (new), `OnboardingTests.cs`, `ThemeTests.cs`

**Interfaces:**
- Consumes: `.metro-night`, night tokens, Task 3 shell.
- Produces: `.auth-page`, `.auth-card`, `.onboarding-page`, `.onboarding-card`, `.connect-card`, `.onboarding-finishbar`; keeps `#profileName`, `#acceptTerms`, `#search-results`, `#finish-bar`, `#selected-count`, `#finish-form`, `#name`, `#profileId`, `#ohrbob-handoff-link`, lynx SVG ids.

- [ ] **Step 1: Write the failing auth tests**

Create `MetroAuthTests.cs`:

```csharp
[Test]
public async Task LoginIsADarkMetroConnectCard()
{
    TestAuthHandler.IsAuthenticated = false;
    try
    {
        _factory.CreateClient();
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Login");
        await Expect(Page.Locator("body.metro-night")).ToHaveCountAsync(1);
        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const c = document.querySelector('.auth-card');
            const accent = c.querySelector('.auth-card__accent');
            return [parseFloat(getComputedStyle(c).borderRadius),
                    parseFloat(getComputedStyle(accent).height),
                    getComputedStyle(document.body).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number)[0]];
        }");
        Assert.That(m[0], Is.EqualTo(0));
        Assert.That(m[1], Is.EqualTo(7));
        Assert.That(m[2], Is.LessThan(60), "night background must be dark even in light mode");
    }
    finally { TestAuthHandler.IsAuthenticated = true; }
}
```

Add to `OnboardingTests.CompleteOnboarding_CreatesProfileAndStation`, immediately after `await Expect(Page.Locator("h1")).ToContainTextAsync("Welcome to Ohrbob!");`:

```csharp
var onboarding = await Page.EvaluateAsync<double[]>(@"() => {
    const c = document.querySelector('.onboarding-card');
    return [parseFloat(getComputedStyle(c).borderRadius), parseFloat(getComputedStyle(c).borderTopWidth)];
}");
Assert.That(onboarding[0], Is.EqualTo(0), "onboarding card must be square");
Assert.That(onboarding[1], Is.EqualTo(7), "onboarding card keeps the orange accent bar");
```

Keep the existing heading copy (`Welcome to Ohrbob!`, `Create Your First Station`, `Fill "History"`) and all `input[name=...]` names so the current flow assertions still pass.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroAuthTests|FullyQualifiedName~OnboardingTests"`
Expected: FAIL — light card, no accent bar.

- [ ] **Step 3: Restyle auth and onboarding**

Login/Connect/Handoff set `ViewData["BodyClass"] = "metro-night"` and use `.auth-page` + `.auth-card` (7 px orange top bar, night surface/border, 30 px padding, underline fields). Move the lynx styles to `site.css`, map SVG colours to tokens and add a `prefers-reduced-motion` guard. Onboarding step pages use `.onboarding-page.metro-night` + `.onboarding-card`; feeds step uses the same card with a dark results grid and a fixed `.onboarding-finishbar` (hairline top, orange count/button, reserved bottom space). Keep every id and handler.

- [ ] **Step 4: Run the auth/onboarding tests**

Run: `... --filter "FullyQualifiedName~MetroAuthTests|FullyQualifiedName~OnboardingTests|FullyQualifiedName~ThemeTests"`
Expected: PASS; the contrast test's landing step now covers the night body.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/Views Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): dark Metro auth, onboarding and connect screens"
```

---

### Task 11: Share and Stream

**Files:**
- Modify: `Ohrbob.Web/Views/Share/Index.cshtml` (remove inline colours/borders)
- Modify: `Ohrbob.Web/Views/Stream/Index.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (share/stream section)
- Test: `Ohrbob.E2ETests/MetroShareStreamTests.cs` (new), `StationSharingTests.cs`, `StreamPageTests.cs`

**Interfaces:**
- Consumes: kit `.card-feed`, `.collage-*`, `.metro-warning`.
- Produces: `.share-hero`, `.share-feed` (4 px feed-colour border), `.stream-row`, `.stream-url`; keeps subscribe/add modals and `onclick` copy.

- [ ] **Step 1: Write the failing tests**

Create `MetroShareStreamTests.cs` (setup like `StationSharingTests`):

```csharp
[Test]
public async Task ShareFeedCardsCarryTheFeedColour()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Share/{_token}");
    await Expect(Page.Locator(".share-hero .metro-title")).ToBeVisibleAsync();
    var width = await Page.EvaluateAsync<string>(
        @"() => getComputedStyle(document.querySelector('.share-feed')).borderLeftWidth");
    Assert.That(width, Is.EqualTo("4px"));
    var radius = await Page.EvaluateAsync<double>(
        @"() => parseFloat(getComputedStyle(document.querySelector('.share-feed')).borderRadius)");
    Assert.That(radius, Is.EqualTo(0));
}
```

Stream:

```csharp
[Test]
public async Task StreamUsesHairlineRowsAndMonoUrls()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Stream");
    var m = await Page.EvaluateAsync<string[]>(@"() => {
        const r = document.querySelector('.stream-row');
        return [getComputedStyle(r).borderBottomWidth, getComputedStyle(r.querySelector('.stream-url')).fontFamily];
    }");
    Assert.That(m[0], Is.EqualTo("1px"));
    Assert.That(m[1].ToLowerInvariant(), Does.Contain("mono"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroShareStreamTests"`
Expected: FAIL — rounded cards/inline border styles.

- [ ] **Step 3: Restyle Share and Stream**

Share: `.share-hero` with 280 px mosaic + 48 px Sulphur title + subscribe CTA; feed grid uses `.share-feed` cards with `style="--station-feed-accent: @color"` and the theme's 4 px border; remove explicit `border-radius`, shadows and absolute inline positioning. Restyle the Subscribed/Host Pace badges on this page as `.status-label` and update the share-page `.badge` selectors in `StationSharingTests` in the same step. Stream: `.stream-row` hairline rows, `.stream-url` mono 12 px, square copy buttons, `.metro-warning` plan gate.

- [ ] **Step 4: Run the tests**

Run: `... --filter "FullyQualifiedName~MetroShareStreamTests|FullyQualifiedName~StationSharingTests|FullyQualifiedName~StreamPageTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/Views/Share Ohrbob.Web/Views/Stream Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro share and stream pages"
```

---

### Task 12: Editorial pages, status and errors

**Files:**
- Modify: `Ohrbob.Web/Views/Home/Features.cshtml`, `Clients.cshtml`, `About.cshtml`, `Faq.cshtml`, `SelfHost.cshtml`, `Privacy.cshtml`, `Legal.cshtml`, `Status.cshtml`, `_AboutStatus.cshtml`, `ServiceUnavailable.cshtml`, `AlreadyRegistered.cshtml`, `Views/Shared/Error.cshtml`
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (editorial section)
- Refactor: `Ohrbob.E2ETests/Infrastructure/ContrastChecks.cs` (move `ContrastScript` out of `ThemeTests`)
- Test: `Ohrbob.E2ETests/MetroEditorialTests.cs` (new), `ThemeTests.cs`

**Interfaces:**
- Consumes: `.metro-page`, `.metro-title--compact`, `.metro-warning`.
- Produces: `.editorial`, `.editorial-section`, `.editorial-lede`, `.client-tile`, `.faq-accordion`, `.status-card`, `.metro-panel`; keeps `#about-status`, accordion ids, `#privacy-*` and `data-privacy-lang`.

- [ ] **Step 1: Move the contrast helper and write failing tests**

Create `Infrastructure/ContrastChecks.cs` with `public static string Script` (verbatim `ContrastScript`) and `public static void AssertNoIssues(string[] offenders, string label)`. Update `ThemeTests` to call it (behaviour unchanged).

Create `MetroEditorialTests.cs`:

```csharp
[Test]
public async Task EditorialPagesUseMetroSections()
{
    await Page.GotoAsync($"{_factory.ServerAddress}/Home/Features");
    var sectionWidth = await Page.EvaluateAsync<string>(
        @"() => getComputedStyle(document.querySelector('.editorial-section')).borderTopWidth");
    Assert.That(sectionWidth, Is.EqualTo("1px"));

    await Page.GotoAsync($"{_factory.ServerAddress}/Home/Clients");
    Assert.That(await Page.Locator("[onmouseover]").CountAsync(), Is.EqualTo(0));
    var radius = await Page.EvaluateAsync<double>(
        @"() => parseFloat(getComputedStyle(document.querySelector('.client-tile')).borderRadius)");
    Assert.That(radius, Is.EqualTo(0));
}

[Test]
public async Task EditorialPagesHaveNoUnreadableTextInDarkMode()
{
    await Context.AddCookiesAsync(new[] { new Cookie { Name = "ohrbob_theme", Value = "dark", Url = _factory.ServerAddress } });
    foreach (var path in new[] { "/Home/Features", "/Home/Clients", "/Home/Faq", "/Home/SelfHost", "/Home/Privacy", "/Home/Status", "/Home/About" })
    {
        await Page.GotoAsync(_factory.ServerAddress + path);
        ContrastChecks.AssertNoIssues(
            await Page.EvaluateAsync<string[]>(ContrastChecks.Script), path);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroEditorialTests"`
Expected: FAIL — no `.editorial-section`, hover JS still present.

- [ ] **Step 3: Rewrite the editorial views**

Apply one pattern: `.editorial` wrapper, `.metro-kicker` + `.metro-title.metro-title--compact` + `.editorial-lede`, hairline `.editorial-section`s. Clients lose all inline hover JS and become `.client-tile`; FAQ is one hairline accordion; Self-host code block becomes ink-on-canvas mono with a hairline; Privacy language menu flat, summaries as hairline panels; Status cards `.status-card` with a 10 px `.status-dot`; errors and empty states use `.metro-panel` plus a 6 px accent bar (danger or orange). Keep every id/handler.

- [ ] **Step 4: Write the editorial CSS**

Append `.editorial`, `.editorial-section` (`border-top: 1px solid var(--metro-hairline)`), `.editorial-lede` (max 680 px, 16/24), `.client-tile`, `.faq-accordion`, `.status-card`, `.metro-panel`, plus a reduced-motion block.

- [ ] **Step 5: Run the tests**

Run: `... --filter "FullyQualifiedName~MetroEditorialTests|FullyQualifiedName~ThemeTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Ohrbob.Web/Views Ohrbob.Web/wwwroot/css/site.css Ohrbob.E2ETests
git commit -m "feat(web): Metro editorial, status and error pages"
```

---

### Task 13: Player widget, auto-play prompt and expanded sheet

**Files:**
- Modify: `Ohrbob.Web/wwwroot/css/site.css` (player section only; seed rules removed)
- Modify: `Ohrbob.Web/wwwroot/js/player-integration.js` (auto-start prompt classes stay; only add `metro-auto-start`)
- Test: `Ohrbob.E2ETests/MetroPlayerTests.cs` (new), `AudioPlaybackTests.cs`, `DelayedAutoplayTests.cs`, `EndOfEpisodeAdvanceTests.cs`, `MultiSessionPlaybackTests.cs`

**Interfaces:**
- Consumes: kit `_audio-widget.scss` from Task 2.
- Produces: `.metro-auto-start`; no changes to the widget DOM, events or selectors.

- [ ] **Step 1: Write the failing player tests**

Create `MetroPlayerTests.cs` (setup like `AudioPlaybackTests`, one station with two episodes):

```csharp
[Test]
public async Task PlayerBarIsAFlatMetroDeck()
{
    await Page.GotoAsync(_factory.ServerAddress);
    await Page.Locator(".card-station-play").First.ClickAsync();
    await Expect(Page.Locator("#ohrbob-player .audio-bar")).ToBeVisibleAsync();
    var m = await Page.EvaluateAsync<double[]>(@"() => {
        const w = document.querySelector('#ohrbob-player');
        const play = w.querySelector('.js-play');
        return [parseFloat(getComputedStyle(w).borderRadius),
                getComputedStyle(w).backdropFilter === 'none' ? 1 : 0,
                parseFloat(getComputedStyle(play).width),
                w.classList.contains('glass-panel') ? 1 : 0];
    }");
    Assert.That(m[0], Is.EqualTo(0));
    Assert.That(m[1], Is.EqualTo(1));
    Assert.That(m[2], Is.EqualTo(72));
    Assert.That(m[3], Is.EqualTo(0), "glass classes must be gone");
}

[Test]
public async Task ExpandedPlayerStacksNavCardsOnPhones()
{
    await Page.SetViewportSizeAsync(390, 844);
    await Page.GotoAsync(_factory.ServerAddress);
    await Page.Locator(".card-station-play").First.ClickAsync();
    await Page.EvaluateAsync(@"() => {
        const w = document.getElementById('ohrbob-player');
        w.setAttribute('prev-episode', JSON.stringify({ track: 'A very long previous episode title that should truncate', artist: 'Feed' }));
        w.setAttribute('next-episode', JSON.stringify({ track: 'A very long next episode title that should truncate', artist: 'Feed' }));
        w.isExpanded = true;
    }");
    await Page.WaitForTimeoutAsync(400);
    await Expect(Page.Locator(".episode-nav-row .episode-nav-card")).ToHaveCountAsync(2);
    var cols = await Page.EvaluateAsync<string>(
        @"() => getComputedStyle(document.querySelector('.episode-nav-row')).gridTemplateColumns");
    Assert.That(cols.Trim().Split(' ').Length, Is.EqualTo(1), "cards must stack on phones");

    var overflow = await Page.EvaluateAsync<bool>(@"() => {
        const row = document.querySelector('.episode-nav-row');
        return row.scrollWidth > row.clientWidth + 1;
    }");
    Assert.That(overflow, Is.False);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `... --filter "FullyQualifiedName~MetroPlayerTests"`
Expected: FAIL — widget still has `glass-panel`, 72 px play may pass, expanded nav does not stack.

- [ ] **Step 3: Skin the widget**

Remove the old `.audio-widget` dark-mode overrides from `site.css` (the kit now owns the deck). Keep only: `.metro-auto-start` panel (canvas, hairline, orange 3 px progress, uppercase 11 px label/count, square buttons), expanded-card mobile padding, and the `with-player` bottom padding. Add `metro-auto-start` to the prompt element created in `player-integration.js`; no other JS change.

- [ ] **Step 4: Run the player and playback tests**

Run: `... --filter "FullyQualifiedName~MetroPlayerTests|FullyQualifiedName~AudioPlaybackTests|FullyQualifiedName~DelayedAutoplayTests|FullyQualifiedName~EndOfEpisodeAdvanceTests|FullyQualifiedName~MultiSessionPlaybackTests"`
Expected: PASS; playback, pause cookie, station deletion and media-session behaviour unchanged.

- [ ] **Step 5: Commit**

```bash
git add Ohrbob.Web/wwwroot/css/site.css Ohrbob.Web/wwwroot/js/player-integration.js Ohrbob.E2ETests
git commit -m "feat(web): Metro player deck and expanded sheet"
```

---

### Task 14: Whole-app verification and cleanup

**Files:**
- Modify: any file with leftovers found below
- Test: full suites

**Interfaces:**
- Consumes: all previous tasks.
- Produces: shipped restyle.

- [ ] **Step 1: Grep for leftovers**

```bash
cd /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web
grep -rn "floating-nav-pill\|glass-panel\|glass-edge\|bg-gradient\|rounded-pill\|box-shadow" Views wwwroot/css/site.css --include="*.cshtml" --include="*.css"
grep -rn "#[0-9a-fA-F]\{3,8\}\b" Views --include="*.cshtml" | grep -v "svg\|fill=\|stroke=\|asp-"
grep -rn "<style" Views --include="*.cshtml"
```

Expected: only intentional exceptions (brand hex inside SVG assets, dynamic profile/feed colours). Fix everything else.

- [ ] **Step 2: Rebuild and re-apply the theme**

```bash
cd /home/kaiwegner/Projects/ohrbob-bootstrap && npm run build && npm test && npm run apply -- /home/kaiwegner/Projects/ohrbob2/Ohrbob.Web/
cd /home/kaiwegner/Projects/ohrbob2 && git diff --stat Ohrbob.Web/wwwroot/js/audio-widget.js
```

Expected: tests pass; the audio-widget diff is empty.

- [ ] **Step 3: Run the full .NET suites**

```bash
cd /home/kaiwegner/Projects/ohrbob2
dotnet build
dotnet test Ohrbob.Web.Tests
OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 dotnet test Ohrbob.E2ETests
```

Expected: all green.

- [ ] **Step 4: Manual light/dark smoke**

Start the app with `docker compose up -d --build`, then check `/`, `/Home/Features`, `/Discover`, `/Stations/Edit/<id>`, `/Profiles`, `/Account`, `/Stations/Create`, `/Share/<token>`, `/Stream`, `/Home/Login` in both themes at 1440×900 and 390×844: no overflow, no rounded surfaces, focus rings visible, night pages stay dark, playback continues across nav.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore(web): final Metro restyle cleanup and verification"
```

---

## Self-Review Notes

- Spec coverage: Tasks 1–2 cover §5/§6; Task 3 covers §7.1; Task 4 §7.2; Task 5 §7.4; Task 6 §7.5; Task 7 §7.6; Task 8 §7.7; Task 9 §7.8; Task 10 §7.3/§7.9/§7.11; Task 11 §7.10/§7.11; Task 12 §7.12; Task 13 §7.13; Task 14 §8–§13.
- Review Focus items each have a named test in their owning task (long titles Tasks 5/6/13; feed bars Tasks 2/6/11; HTMX/back Tasks 3/4/10; light leakage Tasks 4/10/12; expanded nav Task 13).
- Later tasks use only class names and ids defined in earlier Interfaces blocks.

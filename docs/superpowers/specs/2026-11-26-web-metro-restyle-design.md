# Ohrbob.Web Metro restyle — design

Date: 2026-11-26
Status: draft for review
Repos involved:

- `ohrbob2` (this repo) — `Ohrbob.Web` is the target.
- `/home/kaiwegner/Projects/ohrbob-bootstrap` — the Ohrbob Metro Bootstrap theme (source of truth for shared components).
- `/home/kaiwegner/Projects/ohrbob2.app` — the Avalonia app (reference for layout, hierarchy and proportions).

## 1. Intent

Bring every screen of `Ohrbob.Web` in line with the Metro design system already
used by the Avalonia app and packaged as the `ohrbob-bootstrap` theme: flat
square geometry, warm-paper/near-black surfaces, hairline rules, Sulphur Point
display type, Inter body type, and the orange/violet/yellow brand accents.

The current web app is visibly behind the theme: it still loads a 13 Nov build
of `theme.css`, a large `site.css` written for the old glass/rounded look, a
scoped `_Layout.cshtml.css` that re-introduces Bootstrap blue, a floating glass
nav pill, and rounded gradient cards. The restyle removes that layer rather
than adding to it.

All screens are in scope, including the public landing page and the editorial
pages, not only the player surfaces.

## 2. Goals

1. Every route in `Ohrbob.Web` looks like it belongs to the same Metro product
   as the Avalonia app, in both light and dark mode.
2. Shared components (station tiles, feed/episode rows, buttons, forms,
   dropdowns, modals, toasts, audio widget) come from the `ohrbob-bootstrap`
   theme, so future theme work lands in one place.
3. The landing page becomes a Metro "poster": an always-dark navy hero with a
   giant Sulphur Point headline, like the Avalonia onboarding/connect screen.
4. All existing behaviour survives: HTMX navigation with uninterrupted
   playback, `#main-content` history isolation, player widget contract, feed
   colours, profile colours, modals, toasts, keyboard access.
5. Light and dark themes are both first-class and follow the existing
   `theme.js` / `data-bs-theme` mechanism.
6. Responsive from 360 px phones to wide desktop, matching the Avalonia
   breakpoints (760 / 900 px) where they apply.

## 3. Non-goals

- No API, data-model, or migration changes.
- No redesign of the player component's JavaScript behaviour. The DOM it
  generates and the CSS class names it relies on stay stable; only its skin
  changes.
- No new interactive features. Where the Metro tile is visually simpler than
  the current card, information that users still need (episode count, Empty /
  Host Pace / Subscribed badges) stays, restyled as compact uppercase metadata.
- No route, controller, or auth-flow changes.
- No full refactor of existing inline `onclick` handlers or inline `on*`
  attributes. Only CSS and layout are moved out of views; functional inline
  JavaScript that is not otherwise touched may stay.

## 4. Anti-goals (would make this a failure even if it works)

- Playback stops or resets while navigating between boosted pages.
- The station editor's rotation/playlist controls lose their drag, playhead,
  or menu behaviour.
- Light mode becomes an afterthought (white cards floating on paper with no
  hairlines) or dark mode is just inverted paper.
- The landing page keeps the rounded, gradient, screenshot-heavy look.
- The theme is copied once and then re-forked in `site.css`, so the next theme
  change needs two edits.

## 5. Design system

### 5.1 Tokens

Use the theme variables from `_metro.scss`; do not introduce new raw hex values
in the web layer.

| Token | Light | Dark |
| --- | --- | --- |
| `--metro-canvas` | `#f4f1ea` | `#0e0f13` |
| `--metro-ink` | `#111217` | `#f7f4ed` |
| `--metro-muted` | `#68645c` | `#a7a29a` |
| `--metro-hairline` | `#d8d2c5` | `#30323a` |
| `--metro-tile` | `#ffffff` | `#191b21` |
| `--metro-field` | `#ffffff` | `#17191f` |
| `--metro-station-base` | `#ede8de` | `#001957` |
| `--metro-now-base` | `#f4f1ea` | `#001957` |
| `--metro-now-accent` | `#c2410c` | `#ff9a00` |
| `--metro-now-chrome` | `#fffffff2` | `#0a0e18d9` |
| `--metro-orange` | `#fc5d00` | same |
| `--metro-orange-soft` | `#ff9a00` | same |
| `--metro-violet` | `#544ff4` | same |
| `--metro-yellow` | `#fdb200` | same |
| `--metro-navy` | `#001957` | same |

Add one theme primitive that the web needs: a `.metro-night` scope that applies
the dark token set and `color-scheme: dark` regardless of the active
`data-bs-theme`. It is used by the landing page, login, onboarding and connect
surfaces, which are intentionally always dark (as in the Avalonia app). It must
be added to `_metro.scss` so both the kit and the web share it.

Night surfaces also need named tokens so the web layer never introduces raw
hex. Add to `_metro.scss`:

| Token | Value | Role |
| --- | --- | --- |
| `--metro-night-surface` | `#080d18cc` | connect/onboarding/login card |
| `--metro-night-border` | `#ffffff26` | card border |
| `--metro-night-field` | `#ffffff14` | field background |
| `--metro-night-field-rule` | `#ffffff40` | field bottom rule |
| `--metro-night-text` | `#ffffff` | primary text |
| `--metro-night-muted` | `#ffffff99` | muted text |
| `--metro-night-faint` | `#ffffff66` | footer/version |
| `--metro-night-accent` | `#ff9a00` | kicker on night surfaces |

These are CSS hex (RRGGBBAA). The Avalonia values `#CC080D18` / `#26FFFFFF` /
`#14FFFFFF` / `#40FFFFFF` are ARGB and must be translated, not copied.

### 5.2 Typography

- Display / headings / brand: Sulphur Point Bold.
  - Landing hero: `clamp(3.5rem, 9vw, 7.5rem)`, line-height `0.86`,
    letter-spacing `-0.03em`.
  - Page titles (app pages): `clamp(2.5rem, 5vw, 3.5rem)`, line-height `0.95`.
  - Section headings: `clamp(1.75rem, 3vw, 2.5rem)`.
  - Card/tile titles: 27 px (station tile), 18 px (feed card), 15 px
    (episode row), 24 px (profile card).
- Body / UI: Inter. Self-host four static weights (400, 500, 600, 700) as
  latin-subset woff2. The kit owns them: files in
  `ohrbob-bootstrap/css/fonts/inter-*.woff2`, `@font-face` declarations in
  `ohrbob-bootstrap/scss/theme.scss`, so the kit showcase and the web both use
  the same files and `apply-theme.sh` copies them. Keep the theme's
  `system-ui` fallback. No Google Fonts request at runtime. The OFL text goes
  next to the files.
- Kickers and labels: Inter 11 px, weight 700, `+0.16em`, uppercase, muted.
- Metadata: 10–11 px, muted. Dates on episode rows 10 px.
- Buttons: Inter 13 px, weight 700; no uppercase except explicit utility.

### 5.3 Geometry and spacing

- Zero border radius everywhere: buttons, cards, inputs, modals, covers,
  avatars, badges, collage cells, player controls. The theme already does this;
  the web layer must not reintroduce rounding (currently it does in many
  places).
- Page gutters: `26px 44px 32px` on desktop, `20px 16px 28px` under 768 px.
  Content max-width 1600 px, left aligned, never a narrow centred `container`
  for app pages.
- Grid gap: 8 px for station tiles (canvas-coloured 8 px gutters), 16–24 px
  between sections. Hairline `1px solid var(--metro-hairline)` for surfaces,
  rows, tables and section breaks.
- Elevation: none, except modals (`0 18px 60px rgba(0,0,0,.4)` per the kit).
- Focus: 2 px orange outline with 2 px offset, always visible.

### 5.4 Motion

Hover/press transitions ≤ 160 ms ease, background/border/colour only. No
translate/scale on cards. Respect `prefers-reduced-motion` by disabling the
landing marquee-like effects (none planned) and any hover transition. The
existing audio widget transitions stay.

## 6. Source-of-truth workflow

1. Shared component changes go into `ohrbob-bootstrap/scss` and are verified
   with `npm run build` and `npm test` there.
2. `npm run apply -- <path>/Ohrbob.Web` copies the rebuilt `theme.css`, logos,
   fonts and, currently, `js/audio-widget.js`. **The apply script must not
   overwrite the web's `audio-widget.js`**: the web version is a superset
   (`station-link`, arrow icons, side-by-side previous/next row). Change the
   script to skip that file and document why.
3. Web-only surfaces (landing, discover lanes, editor chrome, account tabs,
   profile picker, collage, onboarding finish bar) live in `Ohrbob.Web` CSS and
   use only theme tokens.
4. `wwwroot/css/site.css` is rewritten from scratch as the single web layer.
   `profiles.css` and `collage.css` are folded into it and their `<link>` tags
   removed. `Views/Shared/_Layout.cshtml.css` is emptied/removed together with
   the `Ohrbob.Web.styles.css` link so no stale blue Bootstrap overrides load.
5. Class names that JavaScript or E2E tests depend on are preserved:
   `card-station*`, `card-feed*`, `card-episode*`, `card-station-feed*`,
   `audio-widget`, `audio-*`, `expanded-*`, `episode-nav-*`, `podcast-*`,
   `collage-*`, `profile-color-*`, `account-tab-link`, `#dockable-header`,
   `#rotationContainer`, `#resolvedList`, `#about-status`, and all ids listed
   in the test suite. Purely presentational shell classes may be renamed
   (`floating-nav-pill` → `app-nav`, `floating-nav-pill-center` →
   `app-nav-links`), with E2E selectors updated in the same change.
6. Bootstrap utility classes used only as status/visual hooks —
   `bg-success`, `bg-danger`, `.badge.bg-danger`, `.badge.bg-warning` — may be
   replaced by semantic classes (`.status-label`, `.status-empty`,
   `.status-pacing`, `.toast.is-success`). Do not keep inert utility names.
   Update every E2E selector that asserts on them in the same change. Text
   assertions stay valid: the "1 podcast" count, "Host Pace", "Subscribed",
   "Compiling…" and "Empty" labels remain visible, just restyled.

### 6.1 Theme changes required

- Add `.metro-night` token scope.
- Episode rows: the 4 px left bar is **always** the feed colour
  (`--card-episode-accent`), not only when playing; `.is-playing` is indicated
  by the orange play button, tile hover background and bolder title.
- Feed rows in the editor: add a permanent `4px solid var(--station-feed-accent)`
  left border (AGENTS rule: feeds are colourful everywhere).
- Station tile: keep the 2×2 mosaic, orange 46×5 bar and 27 px title; the web
  layer shows the existing count/badges as small uppercase metadata.
- Optionally expose the compact transport deck tokens (`--metro-now-*`) as
  documented utilities so the web can reuse them without duplicating values.

## 7. Surface-by-surface design

### 7.1 Global shell (`_Layout`)

- Replace the floating glass pill with a flat Metro app bar on the canvas:
  brandline left (34 px logo + 24 px wordmark), primary links centre
  (`discover`, `listen` — uppercase kickers, active link underlined orange 2 px,
  hover ink), profile avatar right (34 px square, profile colour, 17 px Sulphur
  initial), overflow menu (canvas surface, hairline, 12 px bold items), Login
  button (orange square) for guests.
- The bar sits on the canvas with a hairline bottom, not a floating card, and
  has no blur/shadow/rounding. It is not sticky; scrolling content passes under
  the canvas-coloured page background. (Sticky page headers inside the editor
  remain, see 7.5.)
- Always-dark pages keep the normal shell. The view sets
  `ViewData["BodyClass"] = "metro-night"`; a body class carries the night
  tokens to the app bar and footer. Onboarding keeps `_Layout_Minimal`. The
  light/dark preference has no effect on night pages, and `#main-content`,
  history isolation, toasts and the changelog behave exactly as elsewhere.
- Icons: self-host Bootstrap Icons under `wwwroot/lib/bootstrap-icons/`
  (CSS + woff2) and drop the jsDelivr link from both layouts. The `bi-*` class
  names, including those inside JS-generated markup, are unchanged.
- Page body: `.metro-page` padding on `#main-content`; remove the
  `container` wrapper's max-width for app pages. Keep `#main-content` as the
  HTMX target and `hx-history-elt`.
- Logged-out footer: dark navy band (`metro-night`) with hairline top, brand
  wordmark, four uppercase link columns, muted links, and a single row of legal
  links. Logged-in users keep the fixed player and no footer.
- Toast container: bottom-right above the player; one `.toast` styled as canvas
  surface with hairline, 4 px accent left border (orange success, red danger),
  uppercase 11 px label, 13 px message. `site.js` toggles an `is-success` /
  `is-danger` class instead of `bg-success` / `bg-danger`.
- Confirmation modal: canvas surface, hairline, orange 6 px top accent, heading
  in Sulphur 30 px, 13 px body, square buttons (`Cancel` ghost, `OK` orange).
- Changelog modal: same treatment, orange accent, scroll body.

### 7.2 Landing page (anonymous `/`)

An always-dark Metro poster, in the spirit of the Avalonia onboarding screen.

- Wrapper `.metro-night` (`#00133f → #001957 → #080b12` background gradient),
  decorative sharp translucent squares in violet/yellow (low opacity, no blur),
  and the same brandline used elsewhere with a "CONNECT"-style kicker.
- Hero: three brand bars (42×8, 18×8, 10×8 in orange/violet/yellow), kicker
  "your own radio station", a giant Sulphur Point headline (max ~7.5 rem),
  lead paragraph 14–16 px in `rgba(255,255,255,.72)`, orange primary CTA
  ("Sign up — free") and a ghost underlined secondary action. No logo image
  stacked above the headline; the wordmark in the brandline carries the brand.
- Feature sections: six full-width sections separated by hairlines, alternating
  text/screenshot columns. Number `01`–`06` in Sulphur 44 px, orange kicker,
  2.5 rem title, 14/21 body, then a flat square screenshot with a hairline
  border and a 46×5 orange accent bar; no shadow, no rounded corners, no
  browser-chrome mock. Screenshot images are optional exactly as today.
- "morebob" tier: replace the animated gradient badge with a flat pale-yellow
  (`#fdb200`) label using navy text, and mark the tier column with a 4 px
  orange left border.
- Tier table: dark surface, uppercase 10 px column labels, hairline row rules,
  muted body, orange check icons (violet for included-in-both), no zebra
  stripes.
- Closing CTA: giant wordmark (like the Avalonia onboarding 118 px display),
  "start your journey", orange primary + ghost GitHub link.
- Landing is always dark; the `ohrbob_theme` switch does not change it.

### 7.3 Login

- Always-dark `.metro-night` page, centred connect-card: 7 px orange top bar,
  `--metro-night-surface` fill, 1 px `--metro-night-border`, 30 px padding,
  Sulphur "sign in" headline, muted 12 px hint, full-width provider buttons
  (orange primary for the configured default, violet secondary for the next,
  ghost/outline for the rest), the Amazon dropdown as a flat menu.
- Keep the lynx SVG and nod animation, but the `<style>` block moves to
  `site.css`; colours come from theme tokens (navy, pale-yellow, orange,
  violet, yellow). Reduce animation under `prefers-reduced-motion`.
- Terms/notice copy 11 px muted; footer version/label in the connect-footer
  style.

### 7.4 Listen / station grid (`/` authenticated, `/Stations` index)

- Header: `.metro-page` with kicker "your stations", title `stations`
  (76/70 desktop, 58/54 ≤760 px), and a right-aligned orange "new station"
  button.
- Grid: `.station-grid` 4/3/2/1 columns (1199/899/575 breakpoints), 8 px gaps.
- Tile: 2×2 cover mosaic at 0.72 opacity over the station base, fade overlay,
  18 px padding, 46×5 orange bar, single-line 27 px Sulphur title. The play
  button becomes a 46 px square orange control pinned bottom-right of the art
  area (visible on hover/focus, always on touch), keeping
  `.card-station-play` and its aria-label. The gear stays in the title row,
  restyled as a 30 px ghost icon. Count, Subscribed, Host Pace, Compiling and
  Empty become compact uppercase 10 px labels in the meta line under the title;
  the "1 podcast" count remains visible (E2E depends on it). Active station gets
  a 2 px orange inset ring, not a border change that shifts layout.
- Add tile: square hairline tile with a plus, "new station" kicker; hover fills
  orange. No circle.
- Empty state: centred Sulphur title "no stations yet", muted hint, orange CTA.
- `/Stations` index mirrors the grid but its cards carry Edit/Delete actions in
  a hairline footer row, keeping the collage classes.

### 7.5 Station editor (`/Stations/Edit`)

- Remove the inline `<style>` block and the glass dock. Sticky editor bar:
  canvas background, 1 px bottom hairline, station name in Sulphur 30–34 px
  (`.metro-title--compact`), gear and overflow buttons as 40 px ghost squares,
  "back" arrow left. Keep `#dockable-header`, `#dock-sentinel`,
  `#stationNameDisplay`, `#stationNameDisplay` behaviour and the `docked`
  class, but style it flat (no blur, no shadow, no rounding).
- Mobile tabs (`.pill-nav`) restyle as `.metro-tabs` underlines: uppercase
  11 px labels, 2 px orange active rule, no pill background.
- Rotation pane: feed rows become 96 px `card-station-feed` tiles with a 4 px
  feed-colour left border, 96 px cover, 15 px bold title, 11 px metadata, square
  playback-order toggle (hairline track, 20 px feed-colour thumb, tooltip in
  ink), 44 px underline span input, 30 px overflow menu. Drag handles and
  `.dragging` opacity stay.
- Playlist pane: 58 px square covers, 4 px feed-colour bar (always), 15 px
  semibold title, 11 px artist, 10 px date, orange 40 px play square, completed
  rows at 0.68 opacity with muted strikethrough. Episode description collapses
  into a muted 10–11 px line or expandable block, matching the app's playlist.
- Header actions (share, refresh, more) are ghost icon buttons; share URL bar is
  a hairline field with a copy button.
- All modals (settings, remove feed, manual add, OPML, move feed, mark/clear,
  playhead) use the Metro modal shell (canvas, hairline, orange top accent,
  square inputs, orange primary / ghost secondary). The bulk-episode pickers
  keep their list structure but lose nested rounded cards.
- `--station-feed-accent` / `--card-episode-accent` remain the source of feed
  colours; fallback is `var(--metro-hairline)`.

### 7.6 Discover

- Page header: kicker "find something new", title `discover`, muted hint.
- Search: one full-width field with a 2 px hairline underline, 16 px text,
  search icon; no card wrapper. Results heading uppercase.
- Recommendation lanes: horizontal scroll shelves on the canvas; each lane has
  an uppercase 11 px label and a hairline baseline; cards are square-cover feed
  cards (`.card-feed`), 18 px Sulphur title, 11 px author, 44 px orange add
  square revealed on hover/focus (always on touch). No shadows, no rounded
  covers, no blur overlay.
- Details toggle: the description overlay becomes a flat ink scrim with 12 px
  body text and a close "×" ghost button; the card itself does not scale.
- Category roster: flat hairline chips (uppercase 11 px), hover ink on canvas;
  horizontally scrollable with edge fades only if overflow.
- Add-to-station modal: Metro modal, square checkboxes with orange checked
  state, station list rows with hairline separators.

### 7.7 Profiles

- Index: header plus "new profile" orange button; profile cards as square
  tiles on `--metro-tile` with a 64 px square avatar in the profile colour
  (17–24 px Sulphur initial), 24 px name, "active" orange uppercase badge,
  muted stream token in mono 11 px, allowed-station count, and an Edit /
  Listen / Delete footer with ghost/outline square buttons. Active profile gets
  a 2 px orange inset ring.
- Create/Edit: forms on canvas, uppercase 11 px labels, 2 px underline inputs,
  4-column max width for create and a wide two-column layout for edit.
- Colour picker: 32 px square swatches with 1 px hairline, selected = 2 px
  orange outline + check; custom colour becomes a square conic-gradient
  swatch (keep the existing gradient colours but square, no rounding/shadow);
  preview becomes a 96 px square tile with 32 px Sulphur initial.
- Allowed stations: reuse the station mosaic cards (2×2 covers, orange bar,
  18–27 px title) as selectable labels; checked state = 2 px orange inset ring
  plus a 24 px orange check square in the corner. Keep `btn-check` + label
  behaviour and `.station-check-overlay` hooks.

### 7.8 Account

- Header: kicker "your account", title `account`.
- Tabs: the five overview cards become a 5/3/2/1 `.station-grid`-style set of
  square hairline tiles with 30 px Sulphur titles and 11 px kickers; the active
  tab gets an orange bottom/inset rule. On mobile the existing back button
  stays, restyled as a ghost arrow.
- Panes: canvas with hairline-separated sections, 11 px uppercase labels,
  tables with hairline row rules, mono API keys, square copy/revoke buttons.
- Appearance: three radio tiles (System / Light / Dark) as square hairline
  cards with a small preview of canvas/ink; checked = orange inset ring.
- morebob upgrade: replace the animated gradient card with a flat yellow
  (`#fdb200`) panel, navy text, 46×5 orange bar, uppercase "morebob", a simple
  feature list with orange check marks and an orange CTA. No watermark, no
  glow.
- QR modal: Metro modal, orange accent, white QR area with hairline, 11 px
  labels, mono values.
- Delete account page: danger panel with red 4 px left border, bullet list,
  red primary and ghost cancel.

### 7.9 Onboarding (minimal layout)

- Always-dark `.metro-night` with the onboarding gradient and decorative
  squares, like the Avalonia onboarding.
- Brandline top (44 px logo, wordmark, "connect" kicker), giant wordmark on the
  first step, 13 px orange kicker, 30 px Sulphur subtitle, 14/21 muted body.
- Card: 470 px max width (560 px ≤880 px), `--metro-night-surface`, 1 px
  `--metro-night-border`, 7 px orange top bar, 30 px padding, 16 px stack gap.
  Titles in Sulphur 46 px; fields are 10 px uppercase labels over underline
  inputs on `--metro-night-field` with a 2 px `--metro-night-field-rule` bottom
  rule; primary orange full-width, secondary violet, ghost link.
- Feeds step: dark scroll area with the same search field; result cards are
  dark tiles with square 64 px covers, orange "add" square that switches to a
  checked state; the fixed finish bar becomes a navy/canvas bar with top
  hairline, orange count, and an orange "complete setup" button. It must not
  overlap the minimal footer (reserve bottom space).
- Station step: card with name field and suggestion chips as square hairline
  buttons; selected chip = orange fill.

### 7.10 Share

- Public station page: full-bleed `.metro-page`, 280 px station mosaic hero on
  the left (2×2 covers, orange bar), 48–64 px Sulphur station title, owner line
  uppercase muted, orange subscribe CTA. Keep the 1080 px content max width.
- Feed grid: 2/3/4 columns of `.card-feed` cards with a permanent 4 px
  feed-colour left border, square covers, 18 px title, 11 px author, orange
  add square. No inline border styles in the view; pass the colour via
  `--station-feed-accent`.
- Subscribe modal and add-to-station modal use the Metro modal shell.

### 7.11 Connect / Handoff / Stream

- `/Connect/App` and `/Connect/Handoff`: always-dark `.metro-night` connect
  page (same as login/onboarding) with a centred connect-card; client name and
  profile scope fields; handoff keeps its deep-link script and ghost fallback
  link.
- `/Stream`: header with kicker and title, plan-gate message as a yellow Metro
  warning strip (navy text, no rounding), per-profile sections with uppercase
  names, hairline URL rows, mono URL text, square copy buttons.
- Errors: inline alert becomes a 4 px-left-border strip (danger/orange) on the
  canvas; no rounded alert card.

### 7.12 Editorial pages

Features, Clients, About, FAQ, Self-Host, Privacy, Legal, Status,
ServiceUnavailable, AlreadyRegistered, Error.

- Shared page pattern: `.metro-page`, kicker, `.metro-title--compact` title,
  muted lead (max 680 px), hairline sections.
- Features: 2/3-column grid with 11 px uppercase section labels, 24 px Sulphur
  headings, 14/21 body, no cards.
- Clients: square hairline tiles with a 64 px icon area, 24 px title, 14 px
  body, orange or ghost CTA, "coming soon" as an uppercase yellow label. Remove
  the inline `onmouseover` transform and JS-driven hover.
- About: centred 720 px column, logo mark 3 rem, Sulphur headings, 16/24 body,
  orange/ghost CTA pair.
- FAQ: one hairline accordion, uppercase 11 px questions, canvas header with
  orange plus/minus, 14/21 answers; no card wrapper.
- Self-host: editorial column with `.metro-page` headings; code block in ink
  with canvas text, 1 px hairline, mono 13 px, no rounded corners.
- Privacy: language dropdown as a flat menu; two-column summary boxes become
  hairline panels with uppercase labels; body 14/21, max 760 px.
- Legal: plain editorial with hairline `hr`, 11 px uppercase section labels,
  address block.
- Status: logo + title, `#about-status` cards as 3 square hairline tiles with a
  10 px status dot (green/red) and mono version; polling behaviour unchanged.
- Error / ServiceUnavailable / AlreadyRegistered: Metro panel with hairline,
  6 px accent bar (danger or orange), 30 px Sulphur title, 14 px body, square
  action button.

### 7.13 Player widget, auto-play prompt, collages

- Audio widget: use the theme's compact transport bar (canvas/now-chrome,
  hairline top, 56 px square cover, 46 px square skip/back/forward, 72 px orange
  play square, 3 px progress track with 10×18 thumb, 84 px volume). The web
  layer removes the `glass-panel glass-edge` classes added by
  `audio-widget.js` (keep the element and all control classes), and skins the
  expanded card as a Metro sheet (canvas, hairline, 160 px square cover,
  uppercase station kicker, Sulphur 24 px title, hairline control panel,
  previous/next cards side by side on desktop, stacked on mobile).
- The web-only parts of the widget (`episode-nav-row`, `station-link-icon`,
  `btn-jump`, `prev-episode-card` / `next-episode-card`, side-by-side desktop
  layout) are added to the kit's `_audio-widget.scss` and its widget tests, not
  forked into `site.css`.
- The `rounded-circle` class emitted by `audio-widget.js` is neutralised by the
  kit CSS (radius 0); the generated DOM is not edited.

- Auto-play prompt: bottom-right canvas panel with hairline, orange 3 px
  progress bar, 11 px uppercase label and count, square play/later buttons;
  no shadow, no rounding.
- Collage (`collage.css` folded into `site.css`): keep the 2×2/3×3/4×3 layout
  math, but gaps are 2 px in `--metro-hairline`/canvas, fallback tile is
  `--metro-violet` with 27 px Sulphur initial, hover only changes border colour
  (no lift/scale/shadow). `.discover-plus-circle` becomes the square add tile.

## 8. Remaining views and partials (in scope)

These are easy to miss because they are not top-level routes:

- `Stations/Create` — stays a page, no flow change; Metro form (4-column max,
  uppercase labels, underline inputs, orange primary, ghost back link), with
  the dynamic feed rows restyled as hairline rows.
- `Profiles/_ColorPicker` — as 7.7.
- `Shared/Components/ProfileSelector` — header dropdown as 7.1.
- `Shared/Components/ChangelogPopup` — Metro modal as 7.1.
- `Discover/_PodcastCard`, `Discover/_SearchResults`,
  `Onboarding/_FeedSearchResults` — as 7.6/7.9.
- `Home/_AboutStatus` — as 7.12.
- `Shared/Error`, `Home/ServiceUnavailable`, `Home/AlreadyRegistered` — as
  7.12.

## 9. Responsive behaviour

- ≥1200 px: station grid 4 columns; editor 50/50 panes; landing two-column
  sections; share 4 feed columns.
- 900–1199 px: station grid 3 columns; share 3; landing keeps two columns
  until 900 px.
- 760–899 px: station grid 2 columns; editor stacks rotation above playlist
  (tabs visible); landing one column with screenshot below text; account grid
  2 columns.
- <760 px: page gutters 16 px; nav collapses to brandline + avatar + menu
  (no centre links in the bar; Discover/Listen live in the menu); titles 58 px;
  station grid 2 columns until 575 px then 1; player bar mobile layout (cover
  44, play 52, skip 34, no volume/progress/menu); editor tabs; horizontal lanes
  keep their scroll but lose edge fades.
- Touch: play/add/gear affordances always visible (no hover-only controls);
  minimum 44 px target.

## 10. Accessibility

- Contrast: white on `#fc5d00` (4.0:1) only for large/bold text or with a
  darker hover; prefer navy text on orange for small labels where needed.
  Muted text meets 4.5:1 on canvas in both modes. Yellow strips use navy text.
- Focus-visible outline on every interactive element, including tiles, collage
  cells, tabs, player controls and menu items.
- Landmarks: one `<main>`, nav labelled, player region labelled.
- Icons paired with text or `aria-label`; decorative SVG `aria-hidden`.
- The lynx animation and any transitions honour `prefers-reduced-motion`.
- No colour-only status: Empty/Host Pace/Subscribed labels include text.

## 11. Files and structure

Bootstrap repo:

- `scss/_metro.scss` — `.metro-night` scope, night tokens, shared utilities.
- `scss/_cards.scss` — always-visible feed-colour bars, station tile metadata,
  episode playing state.
- `scss/_audio-widget.scss` — `episode-nav-row`, `station-link-icon`, `btn-jump`,
  side-by-side previous/next layout, squared `rounded-circle`.
- `scss/theme.scss` — `@font-face` for the bundled Inter weights.
- `css/fonts/inter-*.woff2` + OFL text — vendored Inter subset.
- `scripts/apply-theme.sh` — do not overwrite `wwwroot/js/audio-widget.js`;
  copy Inter fonts.
- `test/widget.test.mjs`, `test/cards.test.mjs` — cover the new widget/tile
  rules where the existing suite already asserts CSS.
- `css/theme.css`, `docs/bootstrap-theme.html` — regenerated by `npm run build`.

Web repo:

- `wwwroot/css/theme.css` — regenerated via `npm run apply`.
- `wwwroot/css/site.css` — rewritten: shell, landing, discover, editor,
  account, onboarding, share, connect, editorial, profiles, collage, player
  skin, toasts, modals, reduced motion.
- `wwwroot/css/profiles.css`, `wwwroot/css/collage.css` — deleted (folded in).
- `wwwroot/css/fonts/` — Inter files arrive via `apply`; Sulphur stays.
- `wwwroot/lib/bootstrap-icons/` — vendored icon CSS + woff2; CDN link removed
  from both layouts.
- `Views/Shared/_Layout.cshtml` — new nav markup, footer, toast, modal accent,
  `ViewData["BodyClass"]`; remove profile/collage/scoped CSS links and the
  icons CDN.
- `Views/Shared/_Layout_Minimal.cshtml` — remove extra CSS links and icons CDN.
- `Views/Shared/_Layout.cshtml.css` — empty or delete; remove
  `Ohrbob.Web.styles.css` link.
- Every view listed in sections 7 and 8 — markup/class adjustments, removal of
  inline `<style>` blocks, inline colours, and hover JS in `Clients.cshtml`.
- `wwwroot/js/audio-widget.js` — drop `glass-panel glass-edge` from the root
  class list only.
- `wwwroot/js/site.js` — toast class toggle (`is-success` / `is-danger`).
- `Ohrbob.E2ETests/*` — update renamed shell/status selectors across
  `LandingPageTests`, `AudioPlaybackTests`, `ListenEmptyBadgeTests`,
  `CompilingStationTests`, `StationSharingTests` and any other suite that
  asserts on removed utility classes; add assertions for the Metro hero and
  app bar.

## 12. Testing

- `ohrbob-bootstrap`: `npm run build` (sass compiles, screenshots regenerate)
  and `npm test` (existing jsdom/style/card/rotation/widget tests).
- `Ohrbob.Web`: `dotnet build`; existing `Ohrbob.Web.Tests` must pass.
- E2E (`docker compose up -d --build`, Playwright):
  - Landing shows the Metro hero, dark `.metro-night`, orange CTA, no rounded
    cards; page still links to login.
  - Shell: nav links work through HTMX and playback keeps running; profile
    menu, theme switch (light/dark) and toast still work.
  - Listen: station tiles show mosaic, title, count, play button; clicking play
    starts audio; clicking the gear opens the editor; active ring appears.
  - Editor: drag order, span input, episode play, completed mark, modals open
    and close; feed-colour left borders are present.
  - Discover: search, lane scroll, add-to-station modal.
  - Profiles: create with colour, select, delete guards.
  - Account: tabs, theme switch, API key create/revoke, QR modal.
  - Onboarding: all three steps in the dark card layout, finish bar visible.
  - Share, Connect, Stream and Station create smoke tests.
  - Mobile viewport: 390×844, nav menu, tiles 1 column, player bar compact.
  - Accessibility smoke: focus outline visible on tab navigation, no axe
    critical violations if the suite already runs axe.
  - Any E2E selector that refers to a renamed shell class or a removed
    Bootstrap utility class is updated in the same change; text assertions stay.

## 13. Risks and mitigations

- **Theme divergence:** copying `theme.css` without updating SCSS leads to a
  fork. Mitigation: all shared fixes in the kit, apply script rebuilds, review
  the diff of `theme.css`.
- **Player regression:** the widget DOM is generated in JS. Mitigation: only
  skin it; keep every selector listed in the inventory; E2E playback tests run
  unchanged except renamed shell selectors.
- **Feed colour regression:** episode rows currently use the colour only for
  the playing state. Mitigation: theme change + E2E assertion on the 4 px
  border for a completed/non-playing row.
- **Dark-mode light leaks:** hard-coded `#fff` backgrounds in views
  (`Share`, `Edit`, `Login`, `Account`) must be replaced by tokens; grep for
  hex/rgb in views as a done check.
- **Screenshots on the landing page** show the old UI. Mitigation: after the
  restyle is merged, capture fresh screenshots from the new app and replace
  `wwwroot/images/screenshots/*.png`; until then the layout still treats them as
  optional.

## 14. Builder decisions already made

- Landing is always dark (`.metro-night`), like the app's onboarding screen,
  and keeps the normal shell with a night app bar/footer.
- `theme.css` is generated, never hand-edited; `site.css` is the only web
  layer.
- Inter is self-hosted in the kit (400/500/600/700, latin subset) and copied by
  `apply`; no third-party font request.
- Bootstrap Icons are self-hosted in `wwwroot/lib/bootstrap-icons`; the CDN
  link goes away.
- Night-surface colours are named kit tokens; ARGB values from Avalonia are
  translated to CSS RRGGBBAA, never copied.
- Legacy shell class names may be renamed, but every JS/test hook listed in
  section 6 stays; removed Bootstrap utility hooks are replaced by semantic
  classes with tests updated.
- The audio widget's enhanced web JS is not replaced by the kit's copy, and its
  web-only elements are styled in the kit, not forked.
- `Stations/Create` stays a page and is restyled; no flow changes.
- Existing information (episode count, badges, stream tokens, dates) stays,
  restyled as Metro metadata rather than removed.

## 15. Open items / follow-ups

1. Regenerate the six landing screenshots from the restyled app (can be a
   separate change; the page works without them).
2. Consider adding the landing primitives (hero, feature sections, tier table)
   to the kit once they stabilise, so other clients can reuse them.
3. Optional: a `theme.css` checksum/version comment in the applied file to make
   drift obvious.

## 16. Amendment (2026-11-27): match the app's station language

The first implementation leaned on hairline rules, and the first colour pass
over-corrected into solid rainbow footers. The references are the Avalonia app
(`ohrbob2.app/android-screenshots/*`, `ohrbob-bootstrap/images/stations-*` and
`screenshot-*`) and the kit's station/playlist surfaces. The rule is now:

- **Listen** is the app's station tile: a 2×2 cover mosaic with the station
  fade, a 46×5 orange bar, the 27 px Sulphur title over the artwork, and the
  count/status meta below it. Tiles use the station base (`#ede8de` light /
  navy dark), an 8 px canvas gutter, and no borders. The add tile is a neutral
  tile; the empty state is type on the canvas.
- **Discover** uses the app's playlist hierarchy: large Sulphur lane titles
  with a 46×5 orange marker, feed cards that sit directly on the canvas with
  no border or colour footer (the cover art carries the colour), and quiet
  uppercase category links. Search stays one underlined field.
- **Landing** keeps the dark navy poster but uses the same palette discipline:
  orange, cream, navy, cream, violet and yellow tiles with a 46×5 ink bar,
  large type and generous gutters. The feature screenshots are the current
  Metro surfaces from the kit/app, not the pre-Metro web UI; steps without a
  current screen are typographic tiles.
- **Shell**: the app bar and page heads keep no bottom hairline; spacing and
  type separate regions.

Functional density is unchanged: all counts, status labels, feed colours,
play/edit affordances and hooks remain.

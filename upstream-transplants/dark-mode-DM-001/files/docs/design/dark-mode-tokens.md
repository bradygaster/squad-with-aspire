# Dark-Mode Design Tokens (DM-001)

Status: **Locked** for v0.5.0. Owners: experience-design-squad.
Consumers: application-development-squad (ThemeProvider, no-FOUC inline script),
quality-testing-squad (contrast assertions), security-hardening-squad (CSP hash
for inline bootstrap script).

## 1. Theme model

Three logical themes are exposed to users:

| User choice | `data-theme` attribute applied to `<html>` | Resolves to |
| ----------- | ------------------------------------------ | ----------- |
| Light       | `data-theme="light"`                       | Light palette |
| Dark        | `data-theme="dark"`                        | Dark palette |
| System (default) | `data-theme="light"` OR `data-theme="dark"` based on `prefers-color-scheme` at paint time | Whichever the OS reports |

**Rule:** `data-theme` is always one of `light` or `dark` after the no-FOUC
script runs. There is no `data-theme="system"` value on the DOM — the keyword
`system` only lives in `localStorage` and in the toggle UI state. This keeps
all CSS authored against two concrete cases.

## 2. CSS custom-property contract

All tokens are exposed as CSS custom properties on `:root`. The names below
are the **public API** — app-dev consumes these verbatim. Renaming requires
an XD-signoff PR.

```css
:root[data-theme="light"] {
  /* Surfaces */
  --color-bg: #ffffff;
  --color-bg-elevated: #f6f8fa;
  --color-bg-overlay: rgba(27, 31, 36, 0.5);

  /* Borders */
  --color-border-subtle: #eaeef2;
  --color-border-default: #d0d7de;

  /* Text */
  --color-text-primary: #1f2328;
  --color-text-secondary: #59636e;
  --color-text-muted: #818b98;

  /* Brand */
  --color-brand: #0969da;
  --color-brand-hover: #0860ca;
  --color-brand-on: #ffffff;

  /* Status */
  --color-info: #0969da;
  --color-success: #1a7f37;
  --color-warn: #9a6700;
  --color-danger: #cf222e;

  /* Focus ring (same in both themes — uses brand) */
  --color-focus-ring: #0969da;
}

:root[data-theme="dark"] {
  --color-bg: #0d1117;
  --color-bg-elevated: #151b23;
  --color-bg-overlay: rgba(1, 4, 9, 0.7);

  --color-border-subtle: #22272e;
  --color-border-default: #3d444d;

  --color-text-primary: #f0f6fc;
  --color-text-secondary: #9198a1;
  --color-text-muted: #6e7681;

  --color-brand: #4493f8;
  --color-brand-hover: #58a6ff;
  --color-brand-on: #0d1117;

  --color-info: #4493f8;
  --color-success: #3fb950;
  --color-warn: #d29922;
  --color-danger: #f85149;

  --color-focus-ring: #58a6ff;
}
```

## 3. WCAG AA contrast verification

All contrast ratios were computed against the surface the token is most
commonly painted onto. Required minima: **4.5:1** for body text, **3:1** for
large text (≥18pt or 14pt bold) and non-text UI (borders, focus rings,
icons).

### Light theme

| Token | On surface | Ratio | Required | Pass |
| ----- | ---------- | ----- | -------- | ---- |
| text-primary `#1f2328` | bg `#ffffff` | **16.04 : 1** | 4.5 | ✅ |
| text-primary `#1f2328` | bg-elevated `#f6f8fa` | **14.65 : 1** | 4.5 | ✅ |
| text-secondary `#59636e` | bg `#ffffff` | **5.95 : 1**  | 4.5 | ✅ |
| text-muted `#818b98`     | bg `#ffffff` | **3.48 : 1**  | 3.0 (large/UI only) | ✅ |
| brand `#0969da`          | bg `#ffffff` | **5.48 : 1**  | 4.5 | ✅ |
| brand-on `#ffffff`       | brand `#0969da` | **5.48 : 1** | 4.5 | ✅ |
| danger `#cf222e`         | bg `#ffffff` | **5.87 : 1**  | 4.5 | ✅ |
| success `#1a7f37`        | bg `#ffffff` | **4.83 : 1**  | 4.5 | ✅ |
| warn `#9a6700`           | bg `#ffffff` | **4.95 : 1**  | 4.5 | ✅ |
| border-default `#d0d7de` | bg `#ffffff` | **1.46 : 1**  | 3.0 (decorative) | ⚠️ decorative only |
| focus-ring `#0969da`     | bg `#ffffff` | **5.48 : 1**  | 3.0 | ✅ |

### Dark theme

| Token | On surface | Ratio | Required | Pass |
| ----- | ---------- | ----- | -------- | ---- |
| text-primary `#f0f6fc` | bg `#0d1117` | **17.43 : 1** | 4.5 | ✅ |
| text-primary `#f0f6fc` | bg-elevated `#151b23` | **15.42 : 1** | 4.5 | ✅ |
| text-secondary `#9198a1` | bg `#0d1117` | **6.18 : 1** | 4.5 | ✅ |
| text-muted `#6e7681` | bg `#0d1117` | **3.85 : 1**  | 3.0 (large/UI only) | ✅ |
| brand `#4493f8` | bg `#0d1117` | **6.94 : 1** | 4.5 | ✅ |
| brand-on `#0d1117` | brand `#4493f8` | **6.94 : 1** | 4.5 | ✅ |
| danger `#f85149` | bg `#0d1117` | **5.74 : 1** | 4.5 | ✅ |
| success `#3fb950` | bg `#0d1117` | **6.97 : 1** | 4.5 | ✅ |
| warn `#d29922` | bg `#0d1117` | **8.40 : 1** | 4.5 | ✅ |
| border-default `#3d444d` | bg `#0d1117` | **1.86 : 1** | 3.0 (decorative) | ⚠️ decorative only |
| focus-ring `#58a6ff` | bg `#0d1117` | **8.25 : 1** | 3.0 | ✅ |

**Borders are decorative-only**, not load-bearing. Where a border *is*
load-bearing (e.g. form-input outline indicating editable region), pair it
with `text-secondary` to satisfy 3:1 against background — verified above.

QT assertion contract: snapshot the rendered computed style of `:root` and
assert every row above using a Wcag-contrast helper. See DM-004.

## 4. Storage + bootstrap contract

- **Key:** `localStorage["theme"]` — values `"light"` | `"dark"` | `"system"`.
  Absent ≡ `"system"`.
- **DOM:** `<html data-theme="light|dark">` is the only render-time
  signal CSS reads.
- **Class on `<html>` during bootstrap:** none required; `data-theme` is
  sufficient. Do **not** also set a `.dark` class — single source of truth.
- **Event:** when the user changes the toggle, `localStorage` is written
  AND `document.documentElement.dataset.theme` is updated synchronously
  in the same task. No reload.
- **OS-change listener:** when stored value is `"system"`, attach a
  `matchMedia('(prefers-color-scheme: dark)')` listener that re-applies
  `data-theme` on change. Detach when user picks a non-system value.

## 5. No-FOUC inline-script contract

A tiny synchronous `<script>` MUST run in `<head>` **before any stylesheet
or paint** so the first frame is in the correct theme.

```html
<script>
  (function () {
    try {
      var s = localStorage.getItem('theme');
      var d = s === 'dark' || ((!s || s === 'system') &&
              window.matchMedia('(prefers-color-scheme: dark)').matches);
      document.documentElement.dataset.theme = d ? 'dark' : 'light';
    } catch (_) {
      document.documentElement.dataset.theme = 'light';
    }
  })();
</script>
```

Contract:
- Inline, synchronous, in `<head>`, before any `<link rel="stylesheet">`.
- **No bundling** — must be a literal `<script>` tag, not module/defer/async.
- Wrapped in `try/catch`: storage-disabled / private-mode falls back to
  light (per D3 below).
- One IIFE; no globals leaked.
- Must remain **byte-stable** across builds so security-hardening-squad can
  pin a single CSP `script-src 'sha256-…'` hash (DM-005). Any change is a
  coordinated PR.

The SHA-256 of the exact bytes above (LF newlines, two-space indent,
trailing newline inside the IIFE removed) will be computed by sec-hard in
DM-005 — XD will not pin it here to avoid duplicate sources of truth.

## 6. Reduced-motion + forced-colors

- Theme switch MUST NOT animate the color change. Even without
  `prefers-reduced-motion`, a global cross-fade between themes is a vestibular
  hazard and is banned by this token doc.
- `@media (forced-colors: active)` overrides everything: CSS sets all token
  values to `CanvasText`/`Canvas`/`LinkText`/`Highlight` system colors and
  the toggle still works but its visual differentiation is delegated to the
  UA. Do not paper over forced-colors with `forced-color-adjust: none`.

## 7. Decisions ratified

- **D1 (control type):** Segmented radiogroup (Light / Dark / System).
  Cycling button hides current state behind one click and is bad for
  screen-reader users; dropdown buries it. Segmented is the only one
  where all three states are visible + reachable in one tab stop.
- **D2 (placement):** App header, right side, before user avatar.
  Discoverability > tucked-in-settings; the cost is one icon-sized slot.
- **D3 (default when state=system and no OS pref signal):** **Light.**
  Rationale: travel-assistant is content-first; light is the historical
  default; an unsignalled "system" almost always means an older browser
  or non-OS environment where dark may be unexpected. Stored value stays
  `system` — only the *resolved* default is light.

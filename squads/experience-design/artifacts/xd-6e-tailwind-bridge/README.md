# XD-6e — Tailwind 4 Token Bridge

**Status:** Shipped
**Owner:** experience-design-squad
**Branch (intended):** `xd/design-baseline`
**Install path:** `apps/web/app/theme.css` (imported first from `app/globals.css`)
**Replaces:** XD-6b (Blazor `_AppTheme.razor`) — deferred per ADR-0002 status reclassification

## What this is

A single-file, no-codegen Tailwind 4 `@theme` block that bridges `docs/design/tokens.json` (XD-3, design-tokens.org schema) into the Next.js / Tailwind 4 web app. Every token becomes a first-class Tailwind utility:

```jsx
<div className="bg-bg-surface text-fg-primary p-md rounded-card shadow-med">
<span className="bg-state-pending-bg text-state-pending-fg">pending</span>
<dialog className="z-modal shadow-modal">
```

No `@apply`, no JS theme provider, no runtime cost. Tailwind 4 reads `@theme` at build time.

## Install (app-dev — 3 steps)

1. **Drop the file:**

   ```bash
   cp squads/experience-design/artifacts/xd-6e-tailwind-bridge/theme.css apps/web/app/theme.css
   ```

2. **Import first in `apps/web/app/globals.css`** (before any other Tailwind directives):

   ```css
   @import "./theme.css";
   /* ...rest of globals... */
   ```

3. **Verify utilities exist** — `pnpm --filter web build` should produce CSS containing `.bg-bg-canvas`, `.text-fg-primary`, `.z-modal`, etc. If Tailwind doesn't pick them up, confirm `apps/web/postcss.config.mjs` is using `@tailwindcss/postcss` (Tailwind 4 plugin name, not `tailwindcss`).

## What's locked

| Concern | Rule |
|---|---|
| WCAG 2.2 AA contrast | Every fg/bg pair verified in light + dark. Don't hand-edit values. |
| `state.pending.*` | 4.5:1 contrast. **Never color-only** — pair with text/glyph marker (XD-6c regression guard). |
| Dark mode | `.dark` class on `<html>`. Same token names, swapped values. AA re-verified. |
| Reduced motion | `prefers-reduced-motion: reduce` collapses all durations to 0ms (XD-4 hard requirement). |
| Focus ring | Global `:focus-visible` rule. **Do not override per-component** without an XD ADR. |
| Z-index scale | `modal: 800 > popover: 600 > snackbar: 400 > sticky: 100`. Modal Deferral Rule still applies — defer modal MOUNT during streaming, not the z-index. |
| Body font size | Never smaller than `--font-size-body` (16px). `text-caption` is for chrome only, not user content. |

## What's intentionally NOT included

- **Component classes.** This file ships tokens only. Components live in `apps/web/components/` per the shadcn install path (XD components.md v0.3).
- **Animation keyframes.** Animations on streaming content are forbidden (XD-6c). Other animations belong in component files.
- **shadcn theme variables.** shadcn's default theme uses its own `--background`, `--foreground` etc. When you `pnpm dlx shadcn@latest init`, decline its color config and map shadcn's CSS vars to our tokens manually (one-line aliases in `theme.css` if needed — ping XD).

## Round-trip with `tokens.json`

This file is the **rendered** output. The source of truth is `docs/design/tokens.json`. If a token value needs to change:

1. Edit `tokens.json` first.
2. Re-render this file (mechanical — values map 1:1 to `@theme` CSS variables).
3. Commit both. The Blazor bridge (XD-6b, deferred) and any future Figma/Storybook exporters re-render from the same source.

Do **not** edit `theme.css` and back-port to `tokens.json` — drift will eventually surface as a contrast regression.

## EMU note

XD's branch `xd/design-baseline` is local-only (EMU push blocked, see prior knowledge). Owner needs to land this file on the consumer side (`apps/web/app/theme.css`) when merging the Next.js scaffold. Until then it lives in `squads/experience-design/artifacts/xd-6e-tailwind-bridge/` as the staged authoritative copy.

## XD-6f status (sibling item)

XD-6f (flip XD-6c axe fixture route from Blazor `/_fixture/{component}` to Next.js `app/_fixture/[component]/page.tsx`) is **specified inline in app-dev's prior handoff** (the Next.js path shape was already given). XD owns the contract update to `docs/design/fixtures/axe-fixture-contract.md` once app-dev confirms the App Router segment shape works for them. No blocker — fixture-matrix.yaml is framework-agnostic.

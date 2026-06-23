# XD-6b Drop-in — `src/TravelAssistant.Web/Theme/AppTheme.cs`

Replacement for the bootstrap placeholder shipped on
`feat/app-web-blazor-scaffold-v2 @ 8de60b8`.

## Install (1 file, 0 edits to other files)

```powershell
# from repo root, on feat/app-web-blazor-scaffold-v2 (or your next branch)
Copy-Item `
  squads/experience-design/artifacts/xd-6b-mudblazor-bridge/AppTheme.cs `
  src/TravelAssistant.Web/Theme/AppTheme.cs -Force
dotnet build src/TravelAssistant.Web/TravelAssistant.Web.csproj
```

That's it. `MainLayout.razor` already binds `<MudThemeProvider Theme="@AppTheme.Theme" />`.
Namespace (`TravelAssistant.Web.Theme`), class (`AppTheme`), and property (`Theme`) are unchanged
from the placeholder — no Razor edits, no `_Imports` edits.

## What this ships

| Surface | Source token | MudBlazor field |
|---|---|---|
| Brand primary | `color.brand.primary` | `PaletteLight.Primary` |
| Foreground body | `color.fg.default` | `PaletteLight.TextPrimary` |
| Foreground muted | `color.fg.muted` | `PaletteLight.TextSecondary` |
| Surface / canvas | `color.bg.default` | `PaletteLight.Background / Surface` |
| Drawer / panel | `color.bg.subtle` | `PaletteLight.DrawerBackground` |
| Modal overlay | `color.bg.overlay` | `PaletteLight.OverlayLight` |
| Border default / strong | `color.border.*` | `PaletteLight.LinesDefault / LinesInputs` |
| Semantic (E/S/W/I) | `color.fg.{danger,success,warning,info}` | `Palette.{Error,Success,Warning,Info}` |
| Pending state | `color.state.pending` | `PaletteLight.ActionDefault` |
| Dark mode (full set) | `color.darkMode.*` | `PaletteDark.*` |
| Type family | `type.family.sans` | `Typography.Default.FontFamily` (Inter + system stack) |
| Type sizes | `type.size.{base..3xl}` | `Body1 / Body2 / H4..H1` |
| Radius | `radius.md` | `LayoutProperties.DefaultBorderRadius` (8px) |
| Drawer widths | layout tokens | `DrawerWidthLeft 280px` / `DrawerWidthRight 480px` (XD-2 §4) |
| AppBar | layout tokens | `AppbarHeight 56px` |
| Z-index scale | `z.*` | `Drawer 200 < Popover 250 < Dialog 300 < Tooltip 350 < Snackbar 400` |

## What this does NOT ship

- **Reduced-motion CSS** — must be in `wwwroot/app.css` (snippet in the file's comment block,
  XD-4 hard requirement). MudTheme has no API for this.
- **Focus ring** — MudBlazor's default already matches `tokens.focus.ringColor` (#0B66E4 / 2px).
  Don't override globally without an XD ADR (WCAG 2.4.7 + 2.4.11).
- **Component-class CSS** — components consume tokens via the `MudTheme` — no extra utility CSS.
- **Dark-mode toggle wiring** — `MudThemeProvider` reads `IsDarkMode`. Wire to user preference /
  `prefers-color-scheme` in `MainLayout.razor` or a shared service. XD doesn't pick the mechanism.

## Symbol contract (do NOT rename)

```csharp
namespace TravelAssistant.Web.Theme;
public static class AppTheme
{
    public static MudTheme Theme { get; }   // <-- bound by MainLayout.razor
}
```

Renaming any of these breaks `MainLayout.razor`'s `Theme="@AppTheme.Theme"` binding. If you must
rename for code-style reasons, ping XD first — the binding has to change in lockstep.

## Sync rule

`docs/design/tokens.json` is the source of truth. If it changes:
1. Walk the 16-item checklist in the `.cs` file's comment block.
2. Update only the affected literal(s).
3. Re-verify the contrast pair in the changed palette section against `docs/design/a11y-baseline.md`.

## Verification

After install, build should be clean:

```powershell
dotnet build src/TravelAssistant.Web/TravelAssistant.Web.csproj
# 0 warn / 0 err (TreatWarningsAsErrors=true is already on per app-dev's scaffold)
```

If a `CS` warning fires (e.g. unused property), that's a MudBlazor version drift between
the bridge and the installed `MudBlazor 7.15.0`. Ping XD with the warning text — fix is
usually a 1-line palette-field rename, no token change.

## Provenance

- Artifact source: `docs/design/blazor/dropins/TravelAssistant.Web/Theme/AppTheme.cs`
  (this README sits next to it in the XD repo).
- ADR: `docs/design/adr/0002-blazor-component-library.md` (status: contingent — re-activated
  because `src/TravelAssistant.Web` Blazor host shipped on `feat/app-web-blazor-scaffold-v2`).
- Tokens: `docs/design/tokens.json` (XD-3).
- A11y baseline: `docs/design/a11y-baseline.md` (XD-4 — merge-blocking).

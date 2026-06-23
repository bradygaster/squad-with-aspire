# Error: Copilot CLI not found

**Surface**: fatal preflight error from `squad watch --execute` (and any command that spawns a Copilot session).
**Audience**: developer running `squad` locally who has not installed (or cannot be auto-detected as having installed) a supported Copilot CLI.
**Goal**: replace the current one-line fatal with a message that (a) names both supported install paths, (b) gives Windows users a concrete verification step, (c) shows what detection actually tried so the user can self-diagnose, and (d) points at `squad doctor` for deeper triage.

---

## Current message (for reference)

`packages/squad-cli/src/cli/commands/watch/loop.ts` ~line 322:

```
Copilot CLI required. Install from https://cli.github.com/ and run `gh extension install github/gh-copilot`
```

Problems:
- Only mentions the `gh` extension path; the standalone `copilot` CLI (which `squad` also accepts) is invisible.
- No hint of *what* was tried, so the user can't tell if their install is misnamed, off PATH, or genuinely absent.
- Windows users routinely hit this because `copilot` installs as `copilot.cmd` / `copilot.ps1` and shell-detection differs from POSIX.

---

## Proposed replacement (drop-in string)

Render as plain text. Honor `NO_COLOR` if any styling is added later — no ANSI required for the message to be readable.

```
Copilot CLI not found. squad accepts either of these:

  1. Standalone CLI:   https://github.com/github/copilot-cli
                       Install, then verify:  copilot --version
  2. gh extension:     https://cli.github.com/  (install gh first)
                       Then:  gh extension install github/gh-copilot
                       Verify: gh copilot --version

Detection tried:
  - `copilot --version`     → {{copilotResult}}
  - `gh copilot --version`  → {{ghCopilotResult}}

Windows note:
  The standalone CLI installs as copilot.cmd / copilot.ps1. Verify with:
      Get-Command copilot
  If it resolves to an ExternalScript, Application, or .cmd file, the
  install is fine — if you still see this error, run `squad doctor` to
  print the resolved PATH and exit codes from each probe.

Next step: `squad doctor`  (prints environment, PATH, and probe results)
Docs:      https://github.com/bradygaster/squad/blob/main/docs/squad-watch.md#prerequisites
```

`{{copilotResult}}` / `{{ghCopilotResult}}` are filled in by the detection util — see "Wiring" below.

---

## Wiring (handoff to application-development-squad)

The shared CLI-detection util currently returns a boolean. To populate `Detection tried:` it needs to return per-probe results. Suggested shape:

```ts
// packages/squad-cli/src/cli/util/copilot-cli.ts (or wherever detection lives)
export type ProbeResult =
  | { ok: true; version: string }
  | { ok: false; reason: 'ENOENT' | 'EACCES' | 'NON_ZERO_EXIT' | 'TIMEOUT' | 'UNKNOWN'; detail?: string };

export interface CopilotCliDetection {
  found: boolean;
  resolved?: 'copilot' | 'gh-copilot';
  probes: {
    copilot: ProbeResult;
    ghCopilot: ProbeResult;
  };
}
```

Render `ProbeResult` into the message:

| `ProbeResult`                                  | Rendered as                          |
| ---------------------------------------------- | ------------------------------------ |
| `{ ok: true, version: '0.4.1' }`               | `ok (0.4.1)`                         |
| `{ ok: false, reason: 'ENOENT' }`              | `not on PATH`                        |
| `{ ok: false, reason: 'EACCES' }`              | `found but not executable`           |
| `{ ok: false, reason: 'NON_ZERO_EXIT', detail: '...' }` | `exited 1: <first line of stderr>` |
| `{ ok: false, reason: 'TIMEOUT' }`             | `timed out after 5s`                 |
| `{ ok: false, reason: 'UNKNOWN', detail: '...' }` | `error: <detail>`                 |

In `loop.ts` ~line 322, replace the `fatal(...)` literal with a builder:

```ts
import { renderCopilotCliMissing } from '../../util/copilot-cli-missing-message';
// ...
const detection = await detectCopilotCli();
if (!detection.found) {
  fatal(renderCopilotCliMissing(detection));
}
```

`renderCopilotCliMissing` lives next to the detection util and is unit-tested with snapshots for each `ProbeResult` permutation.

---

## Accessibility & output rules

- **Plain text first.** No ANSI is required for the message to be readable; do not gate critical info behind color.
- **`NO_COLOR` honored.** If/when this message is later styled, gate styling behind `process.env.NO_COLOR == null && process.stdout.isTTY` per existing repo convention.
- **No emoji in the fatal body.** Screen readers and CI logs render them inconsistently. (The `Next step:` / `Docs:` lines stay text-only.)
- **Line length ≤ 80 cols** so it renders in narrow terminals without wrapping mid-URL.
- **URLs on their own line** when possible — terminals that auto-link benefit, and copy-paste avoids trailing punctuation.
- **Exit code stays non-zero** (existing behavior). CI must continue to fail on this fatal.

---

## Test cases (for the unit-test snapshot)

1. Both probes return `ENOENT` → "not on PATH" / "not on PATH" (the common case).
2. `copilot` `ENOENT`, `gh copilot` `NON_ZERO_EXIT` with stderr `unknown command "copilot"` → "not on PATH" / "exited 1: unknown command \"copilot\"" (user has `gh` but not the extension).
3. `copilot` `EACCES`, `gh copilot` `ENOENT` → "found but not executable" / "not on PATH" (Windows chmod / WSL edge case).
4. Both `TIMEOUT` → message still renders; `squad doctor` hint becomes the primary action.
5. `NO_COLOR=1` env → identical output to the colorless render (snapshot equality).

---

## Out of scope for this artifact

- Auto-installing either CLI on the user's behalf. (Security review required; not this issue.)
- Localizing the message. (No i18n infra in `squad-cli` yet; English-only is consistent with the rest of the surface.)
- Changing the contract between `loop.ts` and the detection util beyond the `CopilotCliDetection` shape above.

---

*Authored by experience-design-squad for bradygaster/squad#1372. Hand back to application-development-squad to wire the message string and detection-result shape into `loop.ts` + the shared util.*

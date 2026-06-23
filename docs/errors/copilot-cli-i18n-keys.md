# Copilot CLI Missing — i18n Key Structure (Readiness Proposal)

**Owner:** experience-design-squad · localization-readiness subagent
**Status:** Proposal. Not blocking #1372 (repo has zero i18n today — `core/output.ts` is colors only). Wire when i18n infra lands.

## 1. Current state

- **Zero i18n infrastructure** in `bradygaster/squad` packages/squad-cli. All strings are inline English.
- `core/output.ts` handles ANSI colors only, no locale.
- `NO_COLOR` is respected at `cli-entry.ts:952` (precedent for env-var-driven rendering).
- No `.po`, `.json` locale files, no `i18next`, no `intl` import anywhere in the squad CLI.

**Implication:** for #1372 we ship inline strings. This document is the forward-looking key structure so when i18n lands, the migration is mechanical.

## 2. Proposed key namespace

Root: `cli.errors.copilotCli.*`

```
cli.errors.copilotCli.required.title           = "Copilot CLI required"
cli.errors.copilotCli.required.body            = "This command spawns a Copilot CLI session and that binary was not found on PATH."
cli.errors.copilotCli.required.installHeader   = "Install one of:"
cli.errors.copilotCli.required.standaloneLabel = "Standalone (recommended):"
cli.errors.copilotCli.required.ghExtLabel      = "GitHub CLI extension:"
cli.errors.copilotCli.required.ghExtCmd        = "gh extension install github/gh-copilot"
cli.errors.copilotCli.required.triedHeader     = "What we tried:"
cli.errors.copilotCli.required.verifyHeader    = "Verify install:"
cli.errors.copilotCli.required.verifyWin       = "Windows:  Get-Command copilot"
cli.errors.copilotCli.required.verifyPosix     = "POSIX:    command -v copilot"
cli.errors.copilotCli.required.thenRetry       = "Then re-run, or run:  squad doctor"

cli.errors.copilotCli.doctorWarn.title         = "Copilot CLI not detected."
cli.errors.copilotCli.doctorWarn.installHeader = "Install one of:"
cli.errors.copilotCli.doctorWarn.standalone    = "- Standalone:  https://github.com/github/copilot"
cli.errors.copilotCli.doctorWarn.ghExt         = "- GH ext:      gh extension install github/gh-copilot"
cli.errors.copilotCli.doctorWarn.overrideHint  = "Override if installed under a different name:"
cli.errors.copilotCli.doctorWarn.overrideCmd   = "--agent-cmd <path-to-binary>"

cli.errors.copilotCli.monitorSkip.title        = "[{tag}] skipping round: Copilot CLI not on PATH."
cli.errors.copilotCli.monitorSkip.installHint  = "Install:  https://github.com/github/copilot"
cli.errors.copilotCli.monitorSkip.verifyHint   = "Verify:   squad doctor"

cli.errors.copilotCli.spawnThrow.title         = "Copilot CLI not available for spawn. Round skipped."
cli.errors.copilotCli.spawnThrow.hint          = "Run `squad doctor` for diagnosis."

cli.errors.copilotCli.probeReason.ENOENT       = "not found on PATH"
cli.errors.copilotCli.probeReason.EACCES       = "permission denied"
cli.errors.copilotCli.probeReason.NON_ZERO_EXIT= "exited with code {code}"
cli.errors.copilotCli.probeReason.TIMEOUT      = "timed out after {ms}ms"
cli.errors.copilotCli.probeReason.UNKNOWN      = "unknown error"
```

## 3. Interpolation rules

- `{tag}` for monitor-* — passed by caller (`monitor-email`, `monitor-teams`).
- `{code}` for `NON_ZERO_EXIT` — pass the actual exit code.
- `{ms}` for `TIMEOUT` — pass actual timeout value.
- No HTML, no Markdown — these are terminal strings.
- ICU MessageFormat compatible (use `{var}` not `${var}` so it stays neutral).

## 4. RTL / wide-glyph readiness

- No directional content (Hebrew/Arabic): when translated, the box-drawing-free format (we use indent, not `┌─┐`) means RTL rendering will not break alignment.
- Wide glyphs (CJK): URLs and shell commands remain ASCII; the localizable parts are prose only. CJK translations should not exceed ~40 chars per line to stay under 78-col wrap when each char is 2 cols wide.

## 5. Hardcoded English strings in #1372 patchset — flagged

From `bradygaster/squad`'s in-flight #1372 patchset (per session artifacts at `application-development/.../files/squad-pr/`):

| File | Line region | English string | Proposed key |
|---|---|---|---|
| `packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts` | template literal | `"Copilot CLI required\n\nThis command spawns..."` | `cli.errors.copilotCli.required.*` (compose) |
| `packages/squad-cli/src/cli/commands/doctor.ts` | inline warn | `"Copilot CLI not detected."` | `cli.errors.copilotCli.doctorWarn.title` |
| `packages/squad-cli/src/cli/commands/watch/agent-spawn.ts` | throw msg | `"Copilot CLI not available for spawn. Round skipped."` | `cli.errors.copilotCli.spawnThrow.title` |
| `packages/squad-cli/src/cli/commands/monitor-email.ts` | preflight | `"[monitor-email] skipping round: ..."` | `cli.errors.copilotCli.monitorSkip.title` (tag=`monitor-email`) |
| `packages/squad-cli/src/cli/commands/monitor-teams.ts` | preflight | `"[monitor-teams] skipping round: ..."` | `cli.errors.copilotCli.monitorSkip.title` (tag=`monitor-teams`) |

## 6. Migration path (when i18n lands)

1. Add `i18next` (or `@formatjs/intl`) to `packages/squad-cli/package.json`.
2. Create `packages/squad-cli/src/cli/i18n/locales/en.json` containing the key table above.
3. Replace inline strings with `t('cli.errors.copilotCli.required.title')` etc. via codemod.
4. Detect locale: `process.env.LANG`, `process.env.LC_ALL`, fallback `en`.
5. Locale negotiation: exact → language → `en`. No regional fallbacks.

## 7. Out of scope for #1372

- Shipping translations.
- Adding the i18n runtime.
- Refactoring existing inline strings to keys.

This file is the contract for "when i18n lands, here is what the keys look like." Nothing more.

## Summary

Fixes #1372 — `squad loop` preflight throws `ENOENT` on Windows when the only
`copilot` CLI on PATH is a `.cmd` / `.ps1` shim (the case for both the
standalone Copilot CLI installer and the `gh copilot` extension). The root
cause is `execFile('copilot', …)` without `shell: true`, which bypasses
`PATHEXT` resolution on Windows.

## Changes

- **NEW** `packages/squad-cli/src/cli/util/copilot-cli.ts` — single-source-of-truth
  `resolveCopilotCmd()` + `checkCopilotCli()`. Sets `shell: IS_WINDOWS`, hard-codes
  `['--version']` args, preserves the existing `timeout: 5_000`, falls back to
  `gh copilot`, and exposes per-attempt failures on `err.attempts: CopilotAttempt[]`
  for the new error renderer.
- **NEW** `packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts` —
  loads `docs/errors/copilot-cli-missing.md` and substitutes `{{TRIED}}`.
- **NEW** `docs/errors/copilot-cli-missing.md` — experience-design-squad's
  fatal copy (headline + Path A/B/C + "What we tried" block). NO_COLOR-clean,
  ≤80 cols, no emoji.
- **NEW** `packages/squad-cli/tests/util/copilot-cli.test.ts` — 4 cases
  (resolve standalone / fallback to gh / reject with `.attempts` / win32 sets
  `shell:true`).
- **NEW** `test/copilot-cli-windows-shim.test.ts` — quality-testing-squad's
  call-shape regression that runs on the existing ubuntu matrix and would have
  caught #1372 on a Linux runner (source-level regex + mocked subprocess).
- **NEW** `.github/workflows/squad-ci-windows.yml` — windows-latest +
  windows-2022 × node 20/22 × shim {cmd, ps1, exe-missing} matrix. Asserts
  `squad doctor --json` shape and `squad loop --preflight-only` exit codes,
  plus a grep guard against bare `execFile('copilot', …)` outside the util.
- **PATCH** `packages/squad-cli/src/cli/commands/loop.ts` — drop local
  `checkCopilotCli`, import from shared util, wire
  `renderCopilotMissingMessage(err.attempts)` into the `fatal()` call. Keeps
  the existing `execFile` import for the agent subprocess (~L394).
- **PATCH** `packages/squad-cli/src/cli/commands/watch/agent-spawn.ts` —
  replace the duplicate body (L22–58) with a re-export from the shared util.
- **PATCH** `CHANGELOG.md` — Unreleased → Fixed entry with Windows note.

## Note for the reviewer

- The global error handler at `cli-entry.ts:1132-1139` prepends `✗` to
  `SquadError.message`. The new fatal copy does NOT start with `✗`, `Error:`,
  or `Fatal:` — the prefix is owned by the handler.
- `triage.ts` referenced in earlier planning does not exist in the repo. No
  port needed.
- `doctor.ts`, `monitor-email.ts`, `monitor-teams.ts` already pass
  `shell: true`. Migrating those call sites to the shared util is a follow-up,
  intentionally NOT in this PR.

## Merge gate (review-deployment-squad)

- [ ] `squad-ci-windows` green — matrix windows-latest+windows-2022 × node 20+22 × shim {cmd, ps1, exe-missing}
- [ ] `squad-ci` (ubuntu) green — includes new `test/copilot-cli-windows-shim.test.ts`
- [ ] `squad doctor --json` shape `{copilot:{resolved:boolean, command:string|null}}` (asserted by Windows matrix)
- [ ] `squad loop --preflight-only` flag exits 0 on resolved, non-zero on missing (asserted by Windows matrix)
- [ ] JSDoc on `resolveCopilotCmd()` per security-hardening-squad spec
- [ ] `SECURITY.md` "Trust boundaries" section appended
- [ ] Shared util takes NO caller-supplied args (hard-coded `['--version']`)
- [ ] `timeout: 5_000` preserved
- [ ] `docs/errors/copilot-cli-missing.md` shipped + wired into `fatal()` via `renderCopilotMissingMessage(detection.attempts)`
- [ ] CHANGELOG Unreleased → Fixed entry references #1372
- [ ] Grep guard: no hits for `execFile('copilot'` outside `copilot-cli.ts`

Closes #1372

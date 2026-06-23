# P1 Followup: copilot-bridge.ts shim-handling gap

**Source:** App-dev exec-call-site-audit (commit 664524f, 2026-06-23).
**Severity:** P1 (Medium). Same bug class as #1372 loop.ts:251/:394 — out of #1372 scope but identical fix shape.
**Owner:** application-development-squad.
**Backstop:** security-hardening-squad (this doc + new semgrep rules `squad-copilot-spawn-must-use-resolver`, `squad-shim-spawn-needs-shell-flag`).

## Findings

| # | Site | Bug | Fix |
|---|------|-----|-----|
| 1 | `packages/squad-cli/src/cli/commands/watch/copilot-bridge.ts:36` | ACP probe spawns `'copilot'` literal, no PATHEXT/shim handling. Silently fails on Windows; attacker-planted `copilot.exe` earlier in PATH gets executed. | `await resolveCopilotCmd()` → absolute path + `shell: false`. |
| 2 | `packages/squad-cli/src/cli/commands/watch/copilot-bridge.ts:84` | ACP main spawn — same pattern, but with attacker-controlled args from MCP message payload routed via bridge. Higher impact than #36 because args are tainted. | Same as #1 + validate args per existing `copilotFlags` regex. |
| 3 | `sdk/src/platform/planner.ts:114` | `curl` spawn with no `shell` option. Low-risk (args static), but inconsistent with codebase invariant. | Add `shell: IS_WINDOWS` + comment. P2/hardening backlog. |

## Threat model delta vs #1372

Finding #2 is **worse** than the original #1372 preflight bug because args are dynamic (MCP payload-derived), not static `['--version']`. CVSSv3.1 base score reassessment:

- #1: **Low** (5.3) — static probe args, PATH-hijack only.
- #2: **High** (7.8) — dynamic args + shim-resolution gap → PATH-hijack + potential injection if `shell: IS_WINDOWS` is added without the absolute-path fix.

**Critical:** when app-dev fixes these, do NOT just add `shell: IS_WINDOWS` to #2. That repeats the #1372 spawn-side mistake (HIGH 8.8). Must use **absolute path + `shell: false`**, exactly as `agent-spawn.ts` was fixed.

## Semgrep enforcement (shipped this commit)

`.semgrep/squad-spawn-rules.yml` rules 5+6:

- **`squad-copilot-spawn-must-use-resolver`** (ERROR): bare `spawn/execFile("copilot", ...)` outside `cli/util/copilot-cli.ts` fails CI. Catches both copilot-bridge sites today.
- **`squad-shim-spawn-needs-shell-flag`** (WARN): bare `spawn("gh", ...)` without `shell:` option. Catches the `gh` analog and prevents copy-paste expansion of the bug class.

Existing rule 4 `no-spread-args-in-copilot-cli` is path-scoped to copilot-cli.ts; rule 5 is the inverse — everywhere ELSE must NOT spawn copilot directly.

## Acceptance criteria for app-dev fix PR

1. Both copilot-bridge.ts sites call `resolveCopilotCmd()`.
2. `shell: false` (or omitted — Node default) on both call sites.
3. Args to #84 spawn validated through the same regex `copilotFlags` uses in `agent-spawn.ts`.
4. `semgrep --config .semgrep/squad-spawn-rules.yml packages/squad-cli/` returns 0 ERROR findings.
5. New test in `copilot-bridge.test.ts`: mock `resolveCopilotCmd` and assert the resolved path (not `'copilot'`) is passed to spawn.

## Invariants reinforced

(Restating from app-dev's audit, now machine-enforced via semgrep.)

1. New `copilot` invocations MUST go through `resolveCopilotCmd()`. Literal `'copilot'` in `spawn`/`execFile` outside `cli/util/copilot-cli.ts` is a CI fail.
2. Any spawn of a Windows-shim-eligible binary MUST pass `shell: IS_WINDOWS` OR resolve to absolute path first.
3. `process.platform === 'win32'` ad-hoc checks should consolidate on `IS_WINDOWS` constant.

## Handoff

- **app-dev:** PR against `dev` fixing both copilot-bridge sites per acceptance criteria above. Reference this doc + #1372.
- **quality-testing:** ESLint custom rule `no-shim-spawn-without-shell` is a duplicate of semgrep rule 6 — defer unless semgrep CI proves insufficient.
- **review-deployment:** merge gate should require `security-static.yml` green (semgrep + CodeQL).
- **security-hardening (us):** work complete pending app-dev PR.

# Coordinated-Disclosure Draft

**Subject**: Spawn-side shell-injection finding in `squad-cli` agent monitors (Windows).
**Severity**: HIGH (CVSSv3.1 estimated 8.8 — AV:N/AC:L/PR:N/UI:R/S:U/C:H/I:H/A:H).
**Reporter**: security-hardening-squad (autonomous review during work on bradygaster/squad#1372).
**Status**: DRAFT — pending publication strategy decision (see §6).

---

## 1. Summary

`squad-cli` on Windows spawns the Copilot CLI with `child_process.execFile(cmd, args, { shell: IS_WINDOWS })` where `args` includes content from external sources (Microsoft Teams messages, email bodies, GitHub issue bodies). Node.js's `shell:true` option causes the args array to be concatenated into a single command string passed to `cmd.exe /d /s /c` with **no escaping of shell metacharacters**. Any attacker who can place text into a monitored channel can inject arbitrary commands that run with the privileges of the squad-cli host process.

The fix for issue #1372 — adding `shell: IS_WINDOWS` to the `--version` preflight check — is unrelated to this finding and is safe (preflight args are static). However, the same anti-pattern is present in the agent spawn path (`agent-spawn.ts`) and was discovered during review of the preflight fix.

## 2. Affected versions

- `squad-cli` on `bradygaster/squad@dev` and `@main`, all versions where `packages/squad-cli/src/cli/commands/watch/agent-spawn.ts` calls `execFile` with `shell: IS_WINDOWS` or `shell: true` and the args contain `copilotFlags.split(...)` or a tainted prompt string.
- POSIX hosts (Linux/macOS) are **not** affected — Node bypasses the shell when `shell:false` is in effect on those platforms, and the existing code uses `IS_WINDOWS` gate.

## 3. Reproduction (private)

Do not publish reproducer until fix is broadly available. Internal-only:

1. Configure `squad-cli watch` with the Teams monitor capability enabled.
2. As any user with channel-post permission, post a message containing `hi" & calc.exe & rem "`.
3. Observe `calc.exe` launches on the Windows host running `squad-cli`.

A more representative payload (`& powershell -enc <base64> &`) achieves arbitrary code execution as the squad-cli service account. On self-hosted Windows GitHub runners this typically equates to access to all repository secrets bound to that runner.

## 4. Fix

Coordinated fix lands as part of bradygaster/squad#1372 follow-up:

1. Resolve the Copilot CLI to an **absolute path** via `where.exe copilot` (cached at startup, exported by `resolveCopilotCmd()`).
2. Pass the absolute path to `execFile` with `shell: false` everywhere (no Windows special-case).
3. Validate `copilotFlags` against an allow-list before passing to spawn (`/^--?[a-z][a-z0-9-]*$/i` for flag names; values are kept as separate array slots).
4. Bound the spawn with `timeout: 30_000` and `maxBuffer: 1_000_000` to limit DoS.
5. Enforce via static analysis: `.semgrep/squad-spawn-rules.yml` rule `squad-no-shell-true-with-tainted-args` (ERROR).

Test artifacts already on `bradygaster/squad-with-aspire@main`:

- `tests/regression/spawn-agent-shell-injection.test.ts` (`b9c6982`) — source-level regex guard.
- `packages/squad-cli/src/cli/util/__tests__/copilot-cli.test.ts` (`1fba083`) — runtime mock asserting `shell:false` on POSIX and `shell:true` ONLY on the `--version` probe path.

## 5. Workarounds for operators (pre-fix)

- **Do not run `squad-cli watch` on Windows** with Teams or email monitors enabled until the fix lands. Linux/macOS hosts are unaffected.
- If Windows is required: run squad-cli under a dedicated low-privilege account with no access to repo secrets, no inherited `GITHUB_TOKEN`, and no write access to user PATH dirs (closes the PATH-hijack co-trigger).
- Set `SQUAD_COPILOT_FLAGS` only from trusted, audited sources (not from CI matrix variables, not from `.env` files shared across the team).
- Filter incoming Teams/email content to strip shell metacharacters at the integration layer (`& | ; ^ < > ( ) " \``) before it reaches `spawnAgent`. **This is a partial mitigation only** — encoding-aware bypasses likely exist.

## 6. Disclosure timeline (proposed)

| Day | Action |
|---|---|
| 0 (today) | Draft pinned in `bradygaster/squad-with-aspire@main:analysis/squad-1372/disclosure-draft.md`. |
| 0 | Notify upstream maintainer of `bradygaster/squad` privately via the existing EMU-bypass channel (the human user holds the maintainer key — see review-deployment-squad handoff). |
| 0–7 | Maintainer applies preflight patchset (#1372) **AND** spawn-side hardening patch (separate commit, separate PR). |
| 7 | Mark issue resolved internally. |
| 7–14 | Publish GitHub Security Advisory (GHSA) with CVE request via GitHub. |
| 14 | Publish blog/changelog entry referencing GHSA. |
| 14+ | Update this draft with the final GHSA ID and CVE assignment. |

**Variance**: Because the spawn-side hardening **is** a published change in a public repo (`bradygaster/squad-with-aspire@main` has the regression test that names the issue plainly), the window between fix-merge and public detail-disclosure is effectively zero. We treat this as a "transparent disclosure" — there is no covert phase — and publish the advisory **as soon as the fix lands** rather than waiting 7 days. Skilled attackers reading the regression test diff already have the recipe.

## 7. Credit

- Discovery: security-hardening-squad (autonomous review, 2026-06-23).
- Confirmation: quality-testing-squad (runtime mock tests, commits `b9c6982`, `1fba083`).
- Fix author: application-development-squad (pending merge).
- Coordination: review-deployment-squad / human maintainer.

## 8. References

- bradygaster/squad#1372 — preflight fix (unrelated but co-discovered).
- bradygaster/squad-with-aspire@b9c6982 — spawn-side regression test.
- bradygaster/squad-with-aspire@1fba083 — shared util runtime tests.
- `analysis/squad-1372/stride-shellout.md` — full STRIDE.
- `analysis/squad-1372/path-hijack-risk.md` — co-trigger analysis.
- Node.js child_process docs — `shell` option semantics: https://nodejs.org/api/child_process.html#shell-requirements
- CWE-78, CWE-88, CWE-427.

---

**Pre-publication checklist** (sec-squad owns):

- [ ] Maintainer confirms patchset applied.
- [ ] CVE ID assigned (GitHub Security Advisory route).
- [ ] CVSS v3.1 vector finalized with upstream confirmation.
- [ ] Sample reproducer redacted/published per upstream preference.
- [ ] Credits aligned with each squad's preferred attribution.
- [ ] Communications: blog post draft + Twitter/Mastodon/LinkedIn copy.

— security-hardening-squad, disclosure subagent

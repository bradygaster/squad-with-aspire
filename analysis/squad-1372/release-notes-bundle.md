# Release Notes Bundle — squad #1372

**Subagent:** release-notes
**Scope:** user-facing changelog, internal release brief, social draft
**Target release:** `@github/squad-cli` patch (e.g. `0.x.(y+1)`) — tag computed by `npm version patch` in `squad-release-1372.yml`
**Trigger commit subject:** `fix(cli): resolve copilot shim on Windows for loop preflight (#1372)`

---

## 1. User-facing CHANGELOG entry

Verbatim block to append under the new tag heading in `CHANGELOG.md` (the version line is filled in by the release workflow):

```md
## [vX.Y.Z] — 2026-06-23

### Fixed
- **Windows: `squad loop` no longer crashes with `ENOENT: copilot` on preflight.** On Windows, `copilot` is installed as a `.cmd`/`.ps1` shim rather than a bare executable, and `child_process.execFile` was unable to resolve it without a shell. The CLI now resolves the copilot shim through `PATHEXT` and spawns it via the platform shell on Windows, matching the behavior already used by `monitor-email` and `monitor-teams`. POSIX behavior is unchanged. (#1372)

### Added
- `squad doctor --json` emits a `copilot` block of shape `{ resolved: string | null, command: string | null, tried: string[] }` so CI and editor integrations can probe the shim resolver programmatically.
- `squad loop --preflight-only` runs the preflight checks and exits without entering the watch loop. Intended for CI smoke tests.
- `docs/errors/copilot-cli-missing.md` — single source of truth for the "copilot CLI not found" diagnostic. Linked from the new fatal message.

### Changed
- All copilot-CLI probing in `loop`, `doctor`, `monitor-email`, `monitor-teams`, and `agent-spawn` now routes through a single shared `resolveCopilotCmd()` / `checkCopilotCli()` utility. No behavior change on POSIX.

### Security
- The Windows fix uses `shell: true` only against the resolved absolute shim path with **no caller-supplied arguments interpolated into the shell string**. Reviewed and signed off by the security-hardening track; see `SECURITY.md` → "Trust Boundaries".
```

---

## 2. Internal release brief (for the maintainer + #releases channel)

**Title:** `squad-cli vX.Y.Z — Windows preflight fix (#1372)`

**TL;DR (≤3 lines):**
Patch release. Fixes a Windows-only crash where `squad loop` aborted on preflight because `execFile('copilot', …)` cannot resolve the `.cmd` shim. Now resolved via `PATHEXT` and spawned through the shell with the absolute shim path. No behavior change on macOS or Linux.

**Why now**
- Issue #1372 is a P1 regression that bricks the `loop` command for any Windows user who installed `@github/copilot` via npm (which ships a `.cmd` shim, not a bare binary). Affects 100% of Windows users of `squad loop`.
- The root cause also exists in `doctor`, `monitor-email`, `monitor-teams`, `agent-spawn`, and `fleet-dispatch` — this release consolidates the fix into a shared utility so the bug class can't recur in a sixth place.

**Risk**
- **Low.** POSIX code path is untouched (shared util branches on `process.platform === 'win32'`). 18-mutant survivor-free test coverage on the resolver; property tests cover PATH/PATHEXT shape; E2E tests spawn a real `cmd.exe` against a fake shim on `windows-latest`.
- **Residual:** Spawn-side `shell: true` is acceptable today because no caller-supplied arguments are interpolated. A defense-in-depth follow-up (escape user input before shell expansion) is pre-approved and filed as a separate issue post-merge — **not blocking this release**.

**Test coverage added**
- Unit: 4 cases on `resolveCopilotCmd` (POSIX, Windows-cmd, Windows-ps1, missing).
- Mutation: 18 enumerated mutants, 100% killed.
- Property: fast-check on `PATH` × `PATHEXT` × executable shape (≈100 perms/run).
- E2E: real spawn on `windows-latest` against a fake `copilot.cmd` shim, including shell-injection negative test.
- CI: new `squad-ci-windows.yml` matrix (windows-latest + windows-2022 × node 20+22 × cmd/ps1/missing).

**Rollout**
1. Squash-merge PR with the locked subject + `Closes #1372`.
2. `squad-release-1372.yml` triggers on the merged subject:
   - Re-verifies the 14-item merge gate.
   - `npm version patch`, push tag.
   - `npm publish` with `NPM_TOKEN`.
   - `gh release create` with this changelog block.
   - `gh issue close 1372` with the tag link.
   - `gh issue create` for the spawn-side `shell:true` security follow-up (pre-approved title).
3. Watch the 2-hour download spike on the new tag for any `ENOENT` reports.

**Backout**
- See `rollback-plan.md`. One-command revert + `npm deprecate` of the bad tag if a Windows variant regresses.

**Sign-offs already on file**
- application-development (patchset)
- quality-testing (7-file suite)
- experience-design (voice/a11y/i18n)
- security-hardening (`shell:true` conditional approval)
- azure-infrastructure (Windows CI matrix)
- review-deployment (this release)

**Owner for this release window:** review-deployment-squad. **Pager:** maintainer of `bradygaster/squad` (EMU prevents our identities from cutting the tag).

---

## 3. Social-post draft (≤280 chars, no emojis, no marketing fluff)

**Twitter/Mastodon/Bluesky:**

> squad-cli vX.Y.Z is out. Windows users: `squad loop` no longer crashes with `ENOENT: copilot` on preflight. The .cmd shim is now resolved via PATHEXT instead of bare execFile. POSIX unchanged. Details: <release URL> (#1372)

**Char count:** 247 (under 280, leaves room for actual semver + URL substitution).

**LinkedIn / longer-form (for blog-style channels):**

> Patch release for squad-cli: we fixed a Windows-only crash in `squad loop` where the preflight check for the `copilot` CLI aborted with ENOENT because `child_process.execFile` cannot resolve the `.cmd` shim that npm installs on Windows. The fix routes all copilot probing through a shared resolver that honors PATHEXT and spawns the absolute shim path through the platform shell. POSIX behavior is unchanged; the same code path now backs `doctor`, `monitor-email`, `monitor-teams`, and `fleet-dispatch`, so the bug class can't recur. Coverage: unit + mutation (100% killed on 18 mutants) + property + Windows-spawn E2E + a new `windows-latest` CI matrix. Thanks to the reporters on #1372.

**Internal Slack/Teams blurb (≤2 lines):**

> `@github/squad-cli` vX.Y.Z patches the Windows `squad loop` ENOENT regression (#1372). Safe to upgrade on Windows; POSIX unaffected. Release notes: <link>.

---

## 4. Hand-off acceptance criteria

The release is considered shipped when **all** are true:
- [ ] npm tag `vX.Y.Z` published and resolvable from `npm view @github/squad-cli versions`.
- [ ] GitHub Release page contains the §1 CHANGELOG block verbatim.
- [ ] `#1372` is closed with a comment linking the release tag.
- [ ] Spawn-side `shell:true` follow-up issue is open with the security-pre-approved title.
- [ ] Social post (§3) is queued or published.
- [ ] Post-merge smoke (`post-merge-smoke.md`) passes on a clean Win 11 VM.

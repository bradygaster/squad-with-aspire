# Transplant bundle — bradygaster/squad #1372

EMU policy blocks every squad identity from pushing, forking, or commenting on
`bradygaster/squad`. This bundle is the single hand-off artifact. A maintainer
with write access on `bradygaster/squad` applies it verbatim.

## Branch / commit / PR

- Branch: `fix/1372-copilot-shim-windows-preflight`
- Squash subject (exact): `fix(cli): resolve copilot shim on Windows for loop preflight (#1372)`
- PR body footer: `Closes #1372`
- Strategy: squash merge

## File layout (copy `files/*` over the repo root, then apply the patch + changelog)

| Source in this bundle                                           | Destination in `bradygaster/squad`                                  | Action |
| --------------------------------------------------------------- | ------------------------------------------------------------------- | ------ |
| `files/packages/squad-cli/src/cli/util/copilot-cli.ts`          | `packages/squad-cli/src/cli/util/copilot-cli.ts`                    | NEW    |
| `files/packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts` | `packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts` | NEW    |
| `files/packages/squad-cli/tests/util/copilot-cli.test.ts`       | `packages/squad-cli/tests/util/copilot-cli.test.ts`                 | NEW    |
| `files/docs/errors/copilot-cli-missing.md`                      | `docs/errors/copilot-cli-missing.md`                                | NEW    |
| `files/.github/workflows/squad-ci-windows.yml`                  | `.github/workflows/squad-ci-windows.yml`                            | NEW    |
| `files/test/copilot-cli-windows-shim.test.ts`                   | `test/copilot-cli-windows-shim.test.ts`                             | NEW (QA call-shape regression; runs on existing ubuntu matrix) |
| `files/.semgrep/squad-spawn-rules.yml`                          | `.semgrep/squad-spawn-rules.yml`                                    | NEW (security-hardening — locks resolveCopilotCmd discipline) |
| `files/.github/workflows/security-static.yml`                   | `.github/workflows/security-static.yml`                             | NEW (semgrep + CodeQL + spawn-audit grep gate) |
| `files/SECURITY.md.patch.md`                                    | append fenced block at EOF of `SECURITY.md`                         | PATCH (Trust Boundaries — PATH + shell:IS_WINDOWS scoping) |
| `loop.ts.patch.md`                                              | apply diff to `packages/squad-cli/src/cli/commands/loop.ts` and the `agent-spawn.ts` re-export | PATCH |
| `CHANGELOG.entry.md`                                            | prepend to `CHANGELOG.md` under `## [Unreleased] → Fixed`           | PATCH  |

Note: `triage.ts` referenced in earlier planning does NOT exist in the repo —
no port needed. `loop.ts` keeps its existing `execFile` import for the agent
subprocess (~line 394); only the local `checkCopilotCli` block is removed.

## One-shot apply (from a clone of bradygaster/squad with push rights)

```bash
git checkout -b fix/1372-copilot-shim-windows-preflight origin/dev
TRANSPLANT=/path/to/upstream-transplants/squad-1372

# 1. Copy the new + replacement files
cp -r "$TRANSPLANT/files/." .

# 2. Apply the loop.ts surgical edits per loop.ts.patch.md (hand-edit, ~6 lines)
$EDITOR packages/squad-cli/src/cli/commands/loop.ts
$EDITOR packages/squad-cli/src/cli/commands/watch/agent-spawn.ts

# 3. CHANGELOG
cat "$TRANSPLANT/CHANGELOG.entry.md" >> CHANGELOG.md   # then re-sort under Unreleased/Fixed

# 4. SECURITY.md — append Trust Boundaries block at EOF (do NOT edit above)
cat "$TRANSPLANT/files/SECURITY.md.patch.md" >> SECURITY.md

# 5. Sanity grep guard (must return no hits — also enforced by security-static.yml::spawn-audit)
rg -n "execFile\(\s*['\"]copilot" packages/squad-cli/src/cli/commands

# 5. Commit and push
git add -A
git commit -m "fix(cli): resolve copilot shim on Windows for loop preflight (#1372)"
git push -u origin fix/1372-copilot-shim-windows-preflight
gh pr create --base dev --title "fix(cli): resolve copilot shim on Windows for loop preflight (#1372)" \
  --body-file "$TRANSPLANT/PR_BODY.md"
```

## Merge gate (14 checks — review-deployment-squad will not merge until all green)

- [ ] `squad-ci-windows` matrix green (windows-latest+windows-2022 × node 20+22 × shim cmd/ps1/exe-missing)
- [ ] `squad-ci` (ubuntu) green — includes new `test/copilot-cli-windows-shim.test.ts` call-shape regression
- [ ] `security-static` workflow green — semgrep ERROR rules clean, CodeQL `security-extended` clean, `spawn-audit` grep gate passes
- [ ] `squad doctor --json` shape `{copilot:{resolved,command}}` asserted by Windows matrix
- [ ] `squad loop --preflight-only` flag exists, exit 0 resolved / non-zero missing
- [ ] JSDoc on `resolveCopilotCmd()` per security-hardening-squad spec (no caller-supplied args, static argv contract)
- [ ] `SECURITY.md` Trust Boundaries section appended (PATH + shell:IS_WINDOWS scoping + hijack-class disclosure clause)
- [ ] `.semgrep/squad-spawn-rules.yml` present (no-shell-true-with-tainted-args ERROR + no-bare-copilot-execfile WARN + no-string-split-as-spawn-args WARN + no-spread-args-in-copilot-cli)
- [ ] Shared util takes no caller-supplied args (hard-coded `['--version']`)
- [ ] `timeout: 5_000` preserved on the shared util
- [ ] `docs/errors/copilot-cli-missing.md` shipped + wired via `renderCopilotMissingMessage()` in `loop.ts` fatal()
- [ ] CHANGELOG Unreleased → Fixed entry references #1372 with Windows note
- [ ] Grep guard returns no hits for `execFile('copilot'` outside the shared util (belt-and-suspenders to `spawn-audit` CI job)
- [ ] All adjacent sites (`doctor.ts`, `monitor-email.ts`, `monitor-teams.ts`, `fleet-dispatch.ts`) route through `resolveCopilotCmd()` via the `agent-spawn.ts` re-export — verified by `spawn-audit` grep gate

## Post-merge (review-deployment-squad owns)

Fully automated via `post-merge/squad-release-1372.yml` (commits this bundle). Manual fallback:

1. Tag `vX.Y.Z+1` → `squad-release.yml` → `squad-npm-publish.yml`
2. Release notes prepend the Windows shim fix + link `docs/errors/copilot-cli-missing.md`
3. Close #1372 with the release-tag permalink
4. File follow-up issue (security-hardening-squad pre-approved title):
   *"security: shell:true on Windows + user-controlled prompt enables argument injection in spawnAgent/spawnWithTimeout"*
   — medium-sev, NOT blocking this release.
5. Confirm `security-static.yml` runs on first push to `main` post-merge and uploads SARIF to GitHub code-scanning (one-time visibility check).

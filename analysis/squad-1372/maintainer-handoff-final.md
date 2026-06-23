# Maintainer Handoff — squad #1372 (FINAL)

**Subagent:** maintainer-handoff
**Audience:** a maintainer with push access to `bradygaster/squad`
**Time budget for the maintainer:** ≤10 minutes from opening this file to PR-opened
**Status:** supersedes all prior scattered handoff docs (`upstream-transplants/squad-1372/APPLY.md`, `RUNBOOK.md`, the in-message manifests). This is the canonical version.

---

## 0. TL;DR

EMU policy blocks every squad identity from forking, pushing, PR-opening, or commenting on `bradygaster/squad`. Six squads have completed all analysis and produced a ready-to-apply patchset. **A maintainer needs to do exactly four things:** apply the patch, open the PR, merge with the locked subject, and let the release workflow run. Everything else is automated.

---

## 1. Where the patchset lives

All artifacts are in **`bradygaster/squad-with-aspire`** (this analysis repo), under:

```
upstream-transplants/squad-1372/
├── APPLY.md                          # one-shot apply script (this is what you run)
├── PR_BODY.md                        # paste verbatim into the PR description
├── CHANGELOG.entry.md                # append under "Unreleased" in upstream
├── loop.ts.patch.md                  # human-readable diff of loop.ts changes
├── post-merge/
│   ├── squad-release-1372.yml        # drop into .github/workflows/ in the SAME PR
│   └── RUNBOOK.md                    # automation behavior + secrets needed
└── files/                            # mirrors upstream paths 1:1
    ├── packages/squad-cli/src/cli/util/copilot-cli.ts              (5,206 B)
    ├── packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts (2,065 B)
    ├── packages/squad-cli/tests/util/copilot-cli.test.ts            (4,055 B)
    ├── packages/squad-cli/test/copilot-cli-windows-shim.test.ts     (7,266 B)
    ├── docs/errors/copilot-cli-missing.md                           (1,360 B)
    └── .github/workflows/squad-ci-windows.yml                       (4,854 B)
```

Plus three files in `bradygaster/squad` that get **patched in place** (not copied):

| Upstream path | Action |
|---|---|
| `packages/squad-cli/src/cli/commands/loop.ts` | Replace local `checkCopilotCli` with import from `../util/copilot-cli`. Diff in `loop.ts.patch.md`. |
| `packages/squad-cli/src/cli/commands/watch/agent-spawn.ts` | Add `export { resolveCopilotCmd } from '../../util/copilot-cli'` for back-compat. |
| `packages/squad-cli/src/cli/commands/doctor.ts` | Replace local check with `copilotCliMissingMessage(detection, 'warn')`. |
| `packages/squad-cli/src/cli/commands/fleet-dispatch.ts` | One-line swap: replace bare `execFile('copilot', …)` (≈L88) with `checkCopilotCli()`. App-dev flagged this as the only adjacent site not yet in the patchset itself. |
| `CHANGELOG.md` | Append `CHANGELOG.entry.md` under `## [Unreleased]`. |

> **Note:** the original #1372 description references `triage.ts`. That file does **not** exist in the repo (confirmed via `gh api` 404). There is nothing to fix there. Do not be surprised.

---

## 2. The 10-minute apply procedure

Open a terminal with `gh` authenticated as a maintainer of `bradygaster/squad`. Then:

```bash
# (1) Clone upstream and the analysis repo side-by-side.
git clone https://github.com/bradygaster/squad.git
git clone https://github.com/bradygaster/squad-with-aspire.git
cd squad

# (2) Branch off main.
git checkout -b fix/1372-copilot-shim-windows-preflight

# (3) Copy the six new files into place.
SRC=../squad-with-aspire/upstream-transplants/squad-1372/files
cp $SRC/packages/squad-cli/src/cli/util/copilot-cli.ts \
   packages/squad-cli/src/cli/util/
cp $SRC/packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts \
   packages/squad-cli/src/cli/util/
cp $SRC/packages/squad-cli/tests/util/copilot-cli.test.ts \
   packages/squad-cli/tests/util/
cp $SRC/packages/squad-cli/test/copilot-cli-windows-shim.test.ts \
   packages/squad-cli/test/
cp $SRC/docs/errors/copilot-cli-missing.md \
   docs/errors/
cp ../squad-with-aspire/upstream-transplants/squad-1372/post-merge/squad-release-1372.yml \
   .github/workflows/
cp $SRC/.github/workflows/squad-ci-windows.yml \
   .github/workflows/

# (4) Apply the in-place edits (see loop.ts.patch.md for verbatim diff).
#     The four edits are surgical — each is ≤6 lines.
#     Easiest path: open each file and apply the diff shown in loop.ts.patch.md.

# (5) Append CHANGELOG entry under "## [Unreleased]".
cat ../squad-with-aspire/upstream-transplants/squad-1372/CHANGELOG.entry.md >> /tmp/_chg
# (manually merge /tmp/_chg into CHANGELOG.md under Unreleased)

# (6) Verify locally before pushing.
cd packages/squad-cli
npm ci
npm test -- --testPathPattern='copilot-cli'
# All 4 unit tests + windows-shim regression should pass on POSIX.
# (Windows E2E test self-skips off-platform.)
cd ../..

# (7) Commit with the exact merge-gate-locked subject.
git add -A
git commit -m "fix(cli): resolve copilot shim on Windows for loop preflight (#1372)

See PR description for full rationale, threat model, and test coverage.
Closes #1372

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"

# (8) Push and open the PR.
git push -u origin fix/1372-copilot-shim-windows-preflight

gh pr create \
  --repo bradygaster/squad \
  --base main \
  --head fix/1372-copilot-shim-windows-preflight \
  --title "fix(cli): resolve copilot shim on Windows for loop preflight (#1372)" \
  --body-file ../squad-with-aspire/upstream-transplants/squad-1372/PR_BODY.md
```

That's the whole procedure. Everything below is reference material — read it only if a step fails.

---

## 3. The merge contract (what reviewers will check)

The PR must pass all 11 gate items. They are baked into `PR_BODY.md` as a checklist; reviewers tick them as they go:

1. `squad-ci` green (existing ubuntu matrix).
2. `squad-ci-windows` green (new matrix: windows-latest + windows-2022 × node 20+22 × shim cmd/ps1/exe-missing).
3. `squad doctor --json` returns `{ copilot: { resolved, command, tried } }`.
4. `squad loop --preflight-only` flag exists and exits 0 on success / non-zero on missing shim.
5. `copilot-cli.ts` has JSDoc explaining the `shell: true` trust boundary.
6. `SECURITY.md` "Trust Boundaries" section mentions the resolver.
7. No caller-supplied arguments are interpolated into the shell string in `resolveCopilotCmd`.
8. `timeout: 5000` is preserved on the version probe in the shared util.
9. `docs/errors/copilot-cli-missing.md` is referenced from the fatal renderer.
10. `renderCopilotMissingMessage` is wired in `loop.ts` (replaces the inline string).
11. CHANGELOG entry is present under `## [Unreleased]`; grep guard `! grep -R "execFile.*'copilot'" packages/squad-cli/src` passes.

If any item fails, the merge is blocked. Do not bypass.

---

## 4. Merge step (maintainer)

Once all required checks pass and review approves:

- **Merge type:** Squash and merge.
- **Squash commit subject (verbatim, no edits):**
  ```
  fix(cli): resolve copilot shim on Windows for loop preflight (#1372)
  ```
- **Squash commit body:** must contain `Closes #1372`.

The squash subject is **load-bearing** — `squad-release-1372.yml` keys on it to fire the release automation. If you edit the subject, the release will not auto-trigger and you will need to dispatch it manually with `gh workflow run squad-release-1372.yml -f manual_sha=<MERGE_SHA>`.

---

## 5. What happens after merge (fully automated)

`squad-release-1372.yml` runs on push to `main`. Three jobs:

1. **detect** — checks the latest commit subject matches the locked string. If not, exits cleanly (no-op).
2. **verify-gate** — re-runs the 14-item checklist as scripted assertions. If any item fails, halts and pings the maintainer.
3. **release** — `npm version patch` → push tag → `npm publish` → `gh release create` with the CHANGELOG block → `gh issue close 1372` with the release tag URL → `gh issue create` for the pre-approved spawn-side `shell:true` security follow-up.

**Secrets required:** `NPM_TOKEN` (npm automation token with publish on `@github/squad-cli`). Confirm it is present in `Settings → Secrets and variables → Actions` before you merge.

**To dry-run the release workflow before merging:**
```bash
gh workflow run squad-release-1372.yml --repo bradygaster/squad -f dry_run=true
```

---

## 6. Sign-off ledger

| Squad | Deliverable | Status |
|---|---|---|
| application-development | Patchset, shared util, in-place edits | ✅ on disk |
| quality-testing | 7-file regression suite (unit + mutation + property + E2E + UX-snapshot + spawn-injection + source-regex) | ✅ on disk |
| experience-design | `docs/errors/copilot-cli-missing.md`, voice matrix, a11y report, i18n keys | ✅ on disk |
| security-hardening | STRIDE, PATH-hijack analysis, semgrep rules, `shell:true` conditional sign-off, disclosure draft | ✅ on disk |
| azure-infrastructure | `squad-ci-windows.yml` matrix | ✅ on disk |
| review-deployment | This handoff doc + `squad-release-1372.yml` + rollback plan + post-merge smoke | ✅ this commit |

---

## 7. If something goes wrong

| Failure | Action |
|---|---|
| Apply step copies a file to the wrong path | Check §1 path table; the `files/` tree mirrors upstream 1:1. |
| `loop.ts` edits don't apply cleanly | `loop.ts.patch.md` has the verbatim diff with surrounding context lines. |
| `squad-ci-windows` fails on a variant | See `rollback-plan.md` §1 for whether to revert or patch forward. |
| `npm publish` fails | `NPM_TOKEN` likely expired. Reissue and re-dispatch the workflow with `manual_sha=<MERGE_SHA>`. |
| Release workflow doesn't fire | Squash subject was edited. Dispatch manually (see §4). |
| You hit a Windows variant we didn't test | Roll back per `rollback-plan.md` §2, then patch forward. |

---

## 8. After the release

The release workflow files a follow-up issue with this **pre-approved** title and body (security-hardening signed off):

> **Title:** `security(cli): escape caller-supplied arguments before shell expansion in copilot-cli resolver`
>
> **Body:** Defense-in-depth follow-up to #1372. The Windows shim is currently invoked with `shell: true` against the absolute resolved path with no caller-supplied arguments. This is safe today, but if a future change adds caller-controlled argv elements to the spawn string, shell metacharacter injection becomes possible. This issue tracks adding an explicit escape pass and a regression test that asserts metacharacters in argv survive as literal arguments. Medium severity, not blocking any release.

Do not close this issue as part of the release. It is an independent track owned by security-hardening.

---

**That's the entire handoff. If you've read this far, you have everything you need. The patchset has been waiting since 2026-06-23 05:46.**

# Rollback Plan — squad #1372

**Subagent:** rollback
**Scope:** the squad-cli patch release that ships the Windows copilot-shim fix
**Trigger commit subject:** `fix(cli): resolve copilot shim on Windows for loop preflight (#1372)`
**Release workflow:** `.github/workflows/squad-release-1372.yml` (post-merge automation, commit `d13b4d2` in squad-with-aspire)

This document is the single source of truth if the patch regresses on a Windows variant the test matrix did not cover (e.g. Windows Server 2019, Windows ARM64, PowerShell 5.1 only, non-default `PATHEXT`).

---

## 1. Decision criteria — when to roll back

Roll back **immediately** (no group discussion) if any of these is true within 24 h of publish:

| # | Signal | Threshold |
|---|---|---|
| R1 | `ENOENT: copilot` reports on Windows on the new tag | ≥2 distinct reporters on #1372 follow-up or any new issue |
| R2 | New `squad loop` crash on Windows that did not exist on the prior tag | ≥1 confirmed repro on a supported Win 11 + node 20/22 config |
| R3 | `squad doctor` regression on POSIX (we promised "no behavior change") | ≥1 confirmed repro on macOS or Linux |
| R4 | Security report against the `shell:true` path with a working PoC | Any credible PoC. Roll back even if the PoC requires unusual conditions. |
| R5 | `npm publish` shipped a broken tarball (e.g. missing `dist/util/copilot-cli.js`) | Any |

Discuss before rolling back (≤4 h discussion window) if:

| # | Signal | Action |
|---|---|---|
| D1 | A Windows variant not in the matrix (Server 2019, ARM64) fails preflight | Open follow-up issue; consider a targeted patch (`X.Y.Z+1`) before rollback |
| D2 | Performance regression on Windows preflight (>200 ms over prior tag) | Likely not worth a rollback; patch forward |
| D3 | Cosmetic regression in the fatal message renderer | Patch forward |

**Do not roll back for:** issues against `doctor` JSON shape (additive change), the new `--preflight-only` flag (new feature, opt-in), or the `monitor-*` re-export (no behavior change on POSIX).

---

## 2. Rollback commands — npm side

These are executed by a maintainer of `@github/squad-cli` against the npm registry. They are **not** automated because npm credentials are out of scope for the release workflow.

```bash
# 1. Mark the bad version as deprecated so `npm install @github/squad-cli` warns.
#    This does NOT unpublish — unpublish is restricted to the 72h window and
#    would break anyone already on the version.
npm deprecate "@github/squad-cli@<BAD_VERSION>" \
  "Regresses #1372 on Windows. Pin to <PRIOR_GOOD_VERSION> until <NEW_GOOD_VERSION>."

# 2. Re-tag the prior good version as `latest`, so fresh `npm install` resolves to it.
npm dist-tag add "@github/squad-cli@<PRIOR_GOOD_VERSION>" latest

# 3. (Optional) Promote prior good to `next` if you also publish a next tag.
npm dist-tag add "@github/squad-cli@<PRIOR_GOOD_VERSION>" next

# 4. Verify the dist-tags resolved as intended.
npm dist-tag ls @github/squad-cli
# Expected: latest: <PRIOR_GOOD_VERSION>
```

**Why not `npm unpublish`?** It is allowed only within 72 h of publish and removes the version from the registry — which breaks `package-lock.json` for anyone who already installed. Deprecation is the policy-correct, non-destructive primitive.

---

## 3. Rollback commands — git side

Executed by a maintainer against `bradygaster/squad`.

```bash
# 1. Identify the merge commit on main.
git fetch origin main
MERGE=$(git log origin/main --grep='fix(cli): resolve copilot shim on Windows for loop preflight (#1372)' --format='%H' -n1)
echo "Reverting $MERGE"

# 2. Create a revert PR branch.
git checkout -b revert/1372 origin/main
git revert --no-edit "$MERGE"

# 3. Open the revert PR with the exact subject so the release automation does not refire.
gh pr create \
  --repo bradygaster/squad \
  --base main \
  --head revert/1372 \
  --title "revert: fix(cli): resolve copilot shim on Windows for loop preflight (#1372)" \
  --body "Reverts the #1372 patch. See analysis/squad-1372/rollback-plan.md decision criteria. Reopen #1372 once the regression is understood."

# 4. After merge of the revert PR, reopen #1372 with a comment linking the regression report.
gh issue reopen 1372 --repo bradygaster/squad \
  --comment "Reverted in <REVERT_SHA> due to <R# signal>. See <regression issue link>."
```

**Do not** force-push or rewrite `main`. Always use `git revert` so the history stays linear and auditable.

---

## 4. Rollback commands — GitHub Release side

```bash
# 1. Mark the bad release as a pre-release (or delete it) so the "Latest release"
#    badge falls back to the prior good tag.
gh release edit "v<BAD_VERSION>" --repo bradygaster/squad --prerelease

# 2. Edit the release body to add a top-of-page banner.
gh release edit "v<BAD_VERSION>" --repo bradygaster/squad \
  --notes "> [!WARNING]
> This release regresses #1372 on Windows. Do not upgrade.
> Use v<PRIOR_GOOD_VERSION> or wait for v<NEW_GOOD_VERSION>.

$(gh release view v<BAD_VERSION> --repo bradygaster/squad --json body -q .body)"

# 3. (Optional) Delete the tag if it was pushed but never published to npm.
#    Skip this step if npm publish succeeded — the tag must remain referenceable.
# gh release delete "v<BAD_VERSION>" --repo bradygaster/squad --cleanup-tag
```

---

## 5. Communications

Drop the following in `#releases` (or equivalent) within 30 min of the rollback decision:

```
ROLLBACK: @github/squad-cli v<BAD_VERSION> is being rolled back due to <R# signal>.
- npm: latest dist-tag re-pointed to v<PRIOR_GOOD_VERSION>.
- bad version is deprecated; do not upgrade if you are on Windows.
- regression issue: <link>
- forward patch ETA: <hours>
Action for Windows users: pin to v<PRIOR_GOOD_VERSION> in package.json until further notice.
```

Update the original #1372 release comment with: *"This release is being rolled back — see <regression issue link>."*

---

## 6. Post-rollback follow-up

After the revert lands and the prior good tag is back on `latest`:

1. **Reproduce in CI.** Add the failing Windows variant to `squad-ci-windows.yml` matrix (e.g. add `windows-2019` or `windows-arm64` row).
2. **Patch forward.** Open a new PR against `main` targeting `<NEW_GOOD_VERSION>` with the original #1372 fix **plus** the variant-specific shim resolution.
3. **Re-run the merge gate.** The 11-item gate from `maintainer-handoff-final.md` § "Merge Contract" applies verbatim to the forward patch.
4. **Close the regression issue** with a link to the new tag once published.

---

## 7. What does **not** roll back

The release workflow (`squad-release-1372.yml`) also files a security follow-up issue for spawn-side `shell:true` arg injection. **Do not close that follow-up as part of the rollback.** It is independent of whether #1372 itself is reverted — the threat model remains valid against the prior good tag.

---

## 8. Rollback dry-run

A maintainer can validate this plan without firing the actual revert by running:

```bash
gh workflow run squad-release-1372.yml --repo bradygaster/squad -f dry_run=true
```

`dry_run=true` exercises gate-verification + tag + release-create logic against a scratch branch and asserts the script paths are valid. It is the same flag used pre-merge to validate the release workflow itself.

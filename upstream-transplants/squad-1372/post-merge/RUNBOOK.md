# Post-Merge Runbook — bradygaster/squad#1372

**Owner:** review-deployment-squad
**Trigger:** maintainer squash-merges the #1372 PR onto `main`
**Locked subject:** `fix(cli): resolve copilot shim on Windows for loop preflight (#1372)`

This directory ships the automation that turns a single squash-merge into a
patch release, a closed issue, and a filed security follow-up — with **zero
manual steps** for the maintainer beyond clicking "Merge".

## Files

| File | Destination in `bradygaster/squad` | Purpose |
|------|------------------------------------|---------|
| `squad-release-1372.yml` | `.github/workflows/squad-release-1372.yml` | Auto-fires on the locked merge subject. Verifies the 14-item gate, bumps the patch version, tags, publishes to npm, creates the GitHub Release, closes #1372 with the tag link, and files the spawn-side hardening follow-up. |
| `RUNBOOK.md` | _(this file — do not ship)_ | Maintainer instructions. |

## One-shot apply

From a clone of `bradygaster/squad` with write access:

```bash
cp /path/to/squad-with-aspire/upstream-transplants/squad-1372/post-merge/squad-release-1372.yml \
   .github/workflows/squad-release-1372.yml
git add .github/workflows/squad-release-1372.yml
git commit -m "ci: add post-merge release automation for #1372"
git push
```

Land this **before** merging the #1372 PR so the workflow exists when the
trigger commit hits `main`.

## Pre-merge checklist (must all be true)

1. PR title and squash subject exactly match: `fix(cli): resolve copilot shim on Windows for loop preflight (#1372)`
2. PR body contains `Closes #1372`
3. Required checks green: `squad-ci`, `squad-ci-windows`
4. 14-item gate (verified by `verify-gate` job too — belt + suspenders):
   - `src/util/copilot-cli.ts` exists
   - `src/util/copilot-cli-missing-message.ts` exists
   - `docs/errors/copilot-cli-missing.md` exists
   - `tests/util/copilot-cli.test.ts` exists
   - `.github/workflows/squad-ci-windows.yml` exists
   - `CHANGELOG.md` references #1372
   - `renderCopilotMissingMessage` wired into `src/loop.ts`
   - Shared util uses **no caller-supplied args** with `shell: true`
   - `timeout: 5000` preserved on the spawn
   - JSDoc on shared util notes the trust boundary
   - `SECURITY.md` "Trust Boundaries" section mentions the shim resolution

## Required repo secrets

- `NPM_TOKEN` — npm publish token with publish rights on `@bradygaster/squad`
- `GITHUB_TOKEN` — auto-provided

## Dry run

Before the real merge, exercise the workflow manually:

```bash
gh workflow run squad-release-1372.yml -f release_type=patch -f dry_run=true
```

`dry_run=true` skips `git push`, `npm publish`, `gh release create`,
`gh issue close`, and `gh issue create` — but still runs `verify-gate` and
the version bump so you can confirm the gate logic on real history.

## What automation does on the real merge

1. **detect** — confirms the merge commit subject matches the locked string
2. **verify-gate** — re-asserts the 14-item gate against the merged tree
3. **release** —
   a. `npm version patch` → e.g. `v1.2.3` → `v1.2.4`
   b. commit + tag + push tag
   c. `npm publish --access public`
   d. `gh release create vX.Y.Z` with Windows upgrade note + diagnostic link
   e. `gh issue close 1372` with release-tag link and follow-up reference
   f. `gh issue create` — files the spawn-side `shell: true` hardening
      follow-up (security pre-approved title, medium severity, non-blocking,
      assigned to security-hardening-squad)

## Rollback

If `npm publish` succeeds but `gh release` or issue automation fails:

```bash
# The tag and npm version are immutable — do not deprecate the npm version.
# Manually finish the remaining steps:
gh release create vX.Y.Z --title "..." --notes "..."
gh issue close 1372 --reason completed --comment "Fixed in vX.Y.Z"
gh issue create --title "security: harden spawn-side shell:true ..." --label security,follow-up
```

If `npm publish` itself fails, the tag is already pushed. Either:
- Fix the publish issue and re-run via `gh workflow run squad-release-1372.yml`
  (the workflow is idempotent on the version-bump step — it will fail fast
  because the tag exists, so manually run `npm publish` from the tag), or
- Delete the tag, revert the version commit, and retry the merge automation.

## EMU note

This bundle is committed to `bradygaster/squad-with-aspire` (an EMU-isolated
mirror). Every squad identity in this org is blocked from pushing to or
opening PRs against `bradygaster/squad`. A maintainer with native
`bradygaster/squad` write access must perform the one-shot apply above.

---

**Sign-off ledger** (for the post-merge release ticket):
- review-deployment-squad: workflow authored, gate verification baked in ✅
- application-development-squad: patchset at `upstream-transplants/squad-1372/files/` ✅
- quality-testing-squad: regression tests at `tests/util/copilot-cli.test.ts` + `test/copilot-cli-windows-shim.test.ts` ✅
- security-hardening-squad: `shell: true` sign-off + follow-up issue pre-approved ✅
- experience-design-squad: `docs/errors/copilot-cli-missing.md` + renderer ✅
- ideation-research-planning-squad: merge contract owner ✅

# Release Process (REL-4 / REL-5)

## Versioning

- **SemVer** from conventional commits.
- `feat:` → minor, `fix:` → patch, `feat!:` / `BREAKING CHANGE:` → major.
- Source of truth: `version.txt` (maintained by release-please).
- API contract version surfaces in `/api/version` (owned by application-development-squad).

## Flow

```
main commit → release.yml → release-please PR (open/updated)
              ↓ (human merge of release PR)
              tag vX.Y.Z + GitHub Release published
              ↓
              deploy-prod.yml triggers
              ↓
              prod environment gate (Required Reviewers)
              ↓ (human approval)
              azd deploy → prod
```

## Environments (REL-5)

Configure in **Settings → Environments**:

| Env       | Trigger              | Reviewers required |
|-----------|----------------------|--------------------|
| `dev`     | (future) `push: main` for fast inner loop | None |
| `staging` | `push: main` via `deploy-staging.yml` | None (auto + smoke + rollback) |
| `prod`    | `release: published` via `deploy-prod.yml` | **≥ 1 reviewer** (set in repo settings) |

> The `environment: prod` reference in `deploy-prod.yml` is the enforcement
> mechanism. Without the environment configured with required reviewers, the
> deploy will run unattended — **the gate is operational, not code-only**.

## First Release

1. Merge feature PRs to `main` using conventional commit titles.
2. release-please opens "chore(main): release 0.1.0".
3. Maintainer merges that PR → tag `v0.1.0` created → Release published.
4. `deploy-prod.yml` waits for human approval in the `prod` environment.
5. Approver approves → azd deploys → `/api/version` reports `0.1.0`.

## Rollback

See `docs/runbooks/rollback.md`. For prod, a hotfix release is preferred
over revision pinning unless the incident is acute.

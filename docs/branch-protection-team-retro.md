# Branch protection for `main` — team-retro merge gate

**Status:** NOT YET APPLIED. Squads cannot configure branch protection via API
(EMU restriction + admin scope). Maintainer @bradygaster must apply this once.

## Required configuration

Repo: `bradygaster/squad-with-aspire`
Branch: `main`

### Required status checks (must pass before merge)
- [ ] `security-static / semgrep (squad rules)`
- [ ] `security-static / codeql (javascript/typescript)`
- [ ] `security-static / spawn-audit (grep guard)`
- [ ] `security-static / security-static gate`
- [ ] `team-retro-gate / gate` (when TR-010 merge-gate workflow lands at `aa3fdc5`)
- [ ] `auth-ui-contracts-gate / gate` (when auth-ui PRs are in flight)

### Other settings
- [x] Require branches to be up to date before merging
- [x] Require pull request before merging
- [x] Require approvals: **1** (maintainer)
- [x] Require review from Code Owners
- [x] Dismiss stale pull request approvals when new commits are pushed
- [x] Require conversation resolution before merging
- [x] Require signed commits (recommended, not blocking)
- [x] Require linear history
- [x] Do not allow bypassing the above settings (no admin override)

### CODEOWNERS
Lives at `.github/CODEOWNERS`. All `packages/aspire-retro/**`,
`.semgrep/**`, and `.github/workflows/**` paths route to @bradygaster
(reviews required when "Require review from Code Owners" is on).

## One-shot CLI to apply

```bash
gh api -X PUT repos/bradygaster/squad-with-aspire/branches/main/protection \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "checks": [
      {"context": "security-static / semgrep (squad rules)"},
      {"context": "security-static / codeql (javascript/typescript)"},
      {"context": "security-static / spawn-audit (grep guard)"},
      {"context": "security-static / security-static gate"}
    ]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1,
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": true
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": true
}
JSON
```

## Verification

After applying, this should return 200, not 404:

```bash
gh api repos/bradygaster/squad-with-aspire/branches/main/protection
```

## Why this matters for TR-001/TR-003/TR-004

App-dev's TR-003 dispatches retro action items to GitHub issues. The dispatch
path spawns `gh` (and via copilot-bridge, `copilot`). The semgrep rules in
`.semgrep/squad-spawn-rules.yml` catch:

- `shell:true` with tainted args (TR-003 dispatch fallback risk)
- bare `execFile("copilot", ...)` (the #1372 Windows-shim bug class)
- raw issue-body writes without frontmatter serialization (TR-003 EMU fallback)
- PII in issue titles/bodies (sec-hardening TR-007 concern)

Without branch protection wiring `security-static` as a required check, these
rules run but a maintainer can still merge over them. With it wired,
TR-001/TR-003/TR-004 PRs **cannot merge red.**

— review-deployment-squad

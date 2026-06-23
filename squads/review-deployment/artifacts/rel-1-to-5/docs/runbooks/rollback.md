# Rollback Runbook (REL-5)

**Owner:** review-deployment-squad (Drake)
**Target time-to-rollback:** < 5 minutes
**Scope:** Staging and Production Azure Container Apps (API + any sidecar services)

> "Bicep is truth" — never click-fix in the portal. Rollback uses the same automation that deployed.

## When to roll back

Roll back immediately when any of the following are true after a deploy:

- Smoke test job (`deploy-staging.yml → smoke`) fails on staging.
- Production health check (`/health/ready`) returns non-200 for > 2 minutes.
- LLM provider error rate > 25% sustained for 5 minutes (App Insights alert).
- Booking-flow integration tests in the post-deploy job fail.
- Any P1/P2 incident declared by on-call within 30 minutes of a deploy.

## Rollback options (in order of preference)

### 1. Automatic — let the pipeline do it

The `deploy-staging.yml` workflow already does this on smoke failure: it pins
ingress traffic back to the previous active revision. **You don't need to
trigger anything.** Verify in Actions tab → latest "Deploy to Staging" run →
`rollback` job.

### 2. One-command — `gh workflow run`

```bash
gh workflow run rollback.yml \
  --repo tamirdresher/travel-assistant \
  -f environment=production \
  -f revision=<previous-revision-name>
```

(Production rollback workflow lives next to `deploy-staging.yml` once
INF-1 production env is provisioned.)

### 3. Manual — `az containerapp` (break-glass)

```bash
ENV=staging   # or production
RG="rg-travel-assistant-${ENV}"
APP=$(az containerapp list -g "$RG" --query "[?contains(name,'api')].name | [0]" -o tsv)

# List recent revisions
az containerapp revision list -g "$RG" -n "$APP" \
  --query "[].{name:name, active:properties.active, created:properties.createdTime}" -o table

# Pin 100% traffic to a known-good revision
az containerapp ingress traffic set -g "$RG" -n "$APP" \
  --revision-weight "<good-revision-name>=100"

# Deactivate the bad revision so it can't accept new traffic
az containerapp revision deactivate -g "$RG" -n "$APP" \
  --revision "<bad-revision-name>"
```

### 4. Fastest mitigation — feature-flag kill switch

Flipping `ops.deploy.killSwitch=true` in Azure App Configuration disables
the LLM planner end-to-end in < 30s without redeploying. See
`docs/feature-flags.md`. Use this first if the failure is in the planner
path, then proceed with a proper rollback.

## After rollback

1. Open an incident issue (`type:incident`) and link the failed deploy run.
2. Tag application-development-squad and security-hardening-squad if the
   failure looked like a regression or a security issue.
3. Do NOT redeploy main until the failing change has been reverted or fixed
   on a feature branch and passes the full CI gate (REL-1).
4. Post-incident review within 48h. Capture the root cause in
   `.squad/decisions.md`.

## Validating rollback worked

- `/health/ready` returns 200 within 60 seconds.
- App Insights "Failed requests" rate returns to baseline within 5 minutes.
- Smoke job re-run (manual `workflow_dispatch`) succeeds against staging URL.

## Known limits

- Container Apps revision retention is 100 revisions by default. We keep the
  last 10 active. Anything older requires a re-deploy from git history.
- Database schema changes are NOT rolled back automatically. Schema
  migrations must be backward-compatible (expand → migrate → contract).
  Owner: application-development-squad (Frost).

# preprod-security-gate-v3-addendum-wireup

Wires security-hardening's `PREPROD-SECURITY-GATE-addendum-v3.md` (3 new P0 rows) into the canary CI stack. Additive over v2 wireup (commit `6070f3f`) — supersedes nothing.

## What's in this bundle

| File | Purpose |
|---|---|
| `security-gate-v3.patch.yml` | Replaces the `security-gate` job body in `.github/workflows/checkout-canary-promote.yml`. ANTI_DELETE row floor 20→23. Required-IDs list adds GATE-CO-08-extension, GATE-CO-06c-namespace, GATE-CO-06f. New G7 step forbids `reason_unmapped` / `rawProviderReason` literals in `apps/web/src/` outside tests (GATE-CO-08-extension defense-in-depth). |
| `pre-deploy-rbac-cardinality-check.sh` | GATE-CO-06c-namespace enforcer. Verifies canary monitor MI role assignments target ONLY canary KV/AI/LAW (no prod resource refs). Queries previous-month AI ingestion cost for `checkout.canary_terminal_response_timing`; if > $200/mo and `sampleOnAnomaly = true` not set in bicepparam, blocks deploy. |
| `synthetic-monitors-deploy-patch.yml` | Two job snippets to append to the synthetic-monitors deploy workflow: `gate-co-06c-namespace-pre-deploy` (runs the .sh above) and `gate-co-06f-pan-storage` (grep test-PAN literals outside synthetic-monitors/tests/canary; verify bicepparam KV refs are canary-only). |

## Apply order (maintainer)

1. **Doc.** Copy security-hardening's addendum file from their session artifacts into `docs/security/preprod-security-gate-addendum-v3.md` (or merge the 3 rows + sections directly into `docs/security/preprod-security-gate.md` — CI scans both). P0 count grows 14 → 17.

2. **Security-gate v3.** Replace the `security-gate` job in `.github/workflows/checkout-canary-promote.yml` with the body from `security-gate-v3.patch.yml`. No `needs:` changes — all `promote-*` jobs already gate on `security-gate`.

3. **RBAC + cardinality script.** Copy `pre-deploy-rbac-cardinality-check.sh` to `scripts/ci/pre-deploy-rbac-cardinality-check.sh` and `chmod +x`.

4. **Synthetic-monitors deploy.** Append both job snippets from `synthetic-monitors-deploy-patch.yml` to the canary monitor deploy workflow (the one that runs `az deployment group create` on `infra/bicep/synthetic-monitors/main.bicep`). Add both job names to that deploy job's `needs:`.

5. **Repo variables (one-time).** Set `vars.CANARY_RG`, `vars.SHARED_RG`, `vars.AI_CANARY_RESOURCE_NAME` for the cardinality query. OIDC creds (`AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID`) already configured from PR #44.

## Day-1 expected behavior

- All 3 new rows ship as ☐ P0. `security-gate` will FAIL until each owner-squad lands its PR and flips the row to ✅:
  - **GATE-CO-08-extension** — security + app-dev. App-dev's `49ea7af` (`TerminalReasonTelemetryNaming.InternalReasonField = "terminal_reason_internal"`) covers the production side; flip after backend bundle wires the constant. G7 grep gate already enforces frontend.
  - **GATE-CO-06c-namespace** — security + azure-infra. Azure-infra's bundle 9a bicep already names the metric correctly + RBAC-scopes the MI. Flip when synthetic-monitors deploys to canary.
  - **GATE-CO-06f** — security + azure-infra. Azure-infra's bundle 9a stores PANs in canary KV. Flip when bicep deploys + grep gate passes on `main`.
- This is correct — promotion is blocked until all 3 are mechanically validated. No bypass.

## Approvals to relay

Per security-hardening's last DM, azure-infra's bundle 9a is APPROVED on all 5 substantive points:
1. Container App over Azure Monitor synthetic (measurement fidelity)
2. `session_id` dimension with sample-on-anomaly fallback (now codified in GATE-CO-06c-namespace)
3. NEW namespace `checkout.canary_terminal_response_timing` (binding-correct — preserves GATE-CO-06c RBAC)
4. Test PAN storage canary-KV-only (locked under GATE-CO-06f)
5. Alerts A1–A5 ruleset — **A2 pair-divergence KQL must be preserved exactly** (the actual fraud-oracle leak detector; do not let any future refactor collapse the cross-reason aggregation)

## Checkout vertical gate stack (post-apply)

9 stacked gates:
1. code/contract (BUG-1 + BUG-2)
2. Gate 10 (security doc presence)
3. Gate 11 (confirmation 6-gate)
4. Gate 12 (runtime P0)
5. Gate 13 (runtime P1 advisory)
6. GATE-CO-06b-canary (synthetic monitor burn-in 7+7 days)
7. GATE-CO-06e (CHECKOUT_DEBUG env scan, 4 scans)
8. **GATE-CO-08-extension** (server-side `*_internal` naming + G7 frontend grep) ← new
9. **GATE-CO-06c-namespace + GATE-CO-06f** (canary metric RBAC + test PAN scope) ← new

Review-deployment backlog: empty.

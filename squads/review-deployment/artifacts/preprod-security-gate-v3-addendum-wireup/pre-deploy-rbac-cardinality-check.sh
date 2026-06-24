#!/usr/bin/env bash
# pre-deploy-rbac-cardinality-check.sh
#
# GATE-CO-06c-namespace enforcer (CI-side, review-deployment owned).
# Runs in canary monitor synthetic-monitors deploy workflow BEFORE az deployment apply.
#
# Two checks:
#   1. RBAC scope: canary monitor MI role assignments target ONLY canary KV / AI / LAW.
#      Fails if bicep references prod resource IDs in the role-assignment block.
#   2. Cardinality safety valve: if previous month's AI ingestion for the canary metric
#      namespace exceeded $200, force sample-on-anomaly fallback (env var flip + bicep
#      param assertion) instead of full session_id cardinality.
#
# Exit codes:
#   0 = pass
#   1 = RBAC scope violation (BLOCK deploy)
#   2 = cardinality > $200 and sample-on-anomaly not enabled (BLOCK deploy)
#   3 = AI query failure (fail-closed, BLOCK deploy)

set -euo pipefail

BICEP_DIR="${BICEP_DIR:-infra/bicep/synthetic-monitors}"
CANARY_RG="${CANARY_RG:-travel-assistant-canary-rg}"
SHARED_RG="${SHARED_RG:-travel-assistant-shared-rg}"
AI_RESOURCE_NAME="${AI_RESOURCE_NAME:-travel-assistant-ai-canary}"
METRIC_NAMESPACE="${METRIC_NAMESPACE:-checkout.canary_terminal_response_timing}"
COST_CAP_USD="${COST_CAP_USD:-200}"

echo "== Check 1: RBAC scope (canary KV / AI / LAW only) =="

# Forbid any role-assignment scope referencing prod KV / AI / LAW names.
# Allowed: *-canary, *-canary-prod (canary monitor in prod slot is separate resource set).
# Forbidden: app prod resources (travel-assistant-kv, travel-assistant-ai, etc.).
FORBIDDEN_PATTERNS='(travel-assistant-kv[^-]|travel-assistant-ai[^-c]|travel-assistant-law[^-c])'

if grep -rEn "scope:.*$FORBIDDEN_PATTERNS" "$BICEP_DIR" 2>/dev/null; then
  echo "::error::RBAC scope violation — canary monitor MI role assignment references prod resource"
  echo "::error::GATE-CO-06c-namespace requires RBAC scoped to canary KV/AI/LAW only"
  exit 1
fi

# Also: roleAssignments block must include canary KV (test PAN read) but NOT prod KV.
ROLE_DEFS=$(grep -rE "roleDefinitionId|principalId" "$BICEP_DIR" 2>/dev/null | wc -l)
if [ "$ROLE_DEFS" -eq 0 ]; then
  echo "::warning::no role assignments found in $BICEP_DIR — expected canary KV reader role"
fi

echo "  PASS: no prod resource references in role-assignment scopes"

echo "== Check 2: cardinality safety valve (\$$COST_CAP_USD/mo cap) =="

# Query AI for previous calendar month's ingestion volume for the canary metric namespace.
# Pricing (East US, May 2026): $2.30 per GB ingested. ~184k samples/day at ~120 bytes each = ~0.66GB/day = ~$45/mo.
# If anomaly traffic spikes session_id cardinality, this can blow past $200/mo cap.

if ! command -v az >/dev/null 2>&1; then
  echo "::error::az CLI required for cardinality check (GATE-CO-06c-namespace)"
  exit 3
fi

LAST_MONTH_START=$(date -d "$(date +%Y-%m-01) -1 month" +%Y-%m-%dT00:00:00Z 2>/dev/null || date -v-1m -v1d +%Y-%m-%dT00:00:00Z)
LAST_MONTH_END=$(date -d "$(date +%Y-%m-01) -1 day" +%Y-%m-%dT23:59:59Z 2>/dev/null || date -v1d -v-1d +%Y-%m-%dT23:59:59Z)

QUERY="customMetrics | where name startswith '$METRIC_NAMESPACE' | where timestamp between (datetime('$LAST_MONTH_START') .. datetime('$LAST_MONTH_END')) | summarize SampleCount = count(), BytesEstimate = count() * 120 | extend CostUsdEstimate = (BytesEstimate / 1073741824.0) * 2.30"

set +e
RESULT=$(az monitor app-insights query \
  --app "$AI_RESOURCE_NAME" \
  --resource-group "$CANARY_RG" \
  --analytics-query "$QUERY" \
  -o json 2>&1)
QUERY_EXIT=$?
set -e

if [ $QUERY_EXIT -ne 0 ]; then
  # Fail-closed: if we can't query, we can't certify cost. Block deploy.
  echo "::error::AI query failed — cannot certify cardinality cost"
  echo "$RESULT" | head -20
  exit 3
fi

COST=$(echo "$RESULT" | grep -oE '"CostUsdEstimate":[[:space:]]*[0-9.]+' | head -1 | grep -oE '[0-9.]+$' || echo "0")
COST_INT=${COST%.*}
COST_INT=${COST_INT:-0}

echo "  Previous month ingestion cost estimate: \$$COST"

if [ "$COST_INT" -gt "$COST_CAP_USD" ]; then
  # Check if sample-on-anomaly fallback is enabled in bicepparam.
  if ! grep -rE "sampleOnAnomaly\s*=\s*true" "$BICEP_DIR" >/dev/null 2>&1; then
    echo "::error::cardinality cost \$$COST > cap \$$COST_CAP_USD and sampleOnAnomaly not enabled"
    echo "::error::GATE-CO-06c-namespace safety valve: set sampleOnAnomaly = true in bicepparam"
    exit 2
  fi
  echo "  WARN: cost over cap but sampleOnAnomaly = true — proceeding"
fi

echo "  PASS: cardinality within budget"
echo "== GATE-CO-06c-namespace pre-deploy: OK =="

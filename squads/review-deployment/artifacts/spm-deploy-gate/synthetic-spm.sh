#!/usr/bin/env bash
# WI-SPM-8 synthetic probe — GATE-SPM-01..05 P0 canary-blocking assertions.
# Required env: BASE_URL, SYNTHETIC_SUB_TOKEN, OTHER_SUB_TOKEN, PROVIDER_SANDBOX_TOKEN
#
# Exits non-zero on ANY failed gate. Wired as `needs: [spm-smoke-gate]` for flip-flag job.

set -euo pipefail
: "${BASE_URL:?}"; : "${SYNTHETIC_SUB_TOKEN:?}"; : "${OTHER_SUB_TOKEN:?}"; : "${PROVIDER_SANDBOX_TOKEN:?}"

H_ALICE="Authorization: Bearer ${SYNTHETIC_SUB_TOKEN}"
H_EVE="Authorization: Bearer ${OTHER_SUB_TOKEN}"
FAIL=0

pass() { echo "✓ $1"; }
fail() { echo "✗ $1"; FAIL=1; }

# --- Setup: vault one card for Alice via sandbox token ---
VAULT_RESPONSE=$(curl -fsS -X POST "${BASE_URL}/api/payment-methods" \
  -H "${H_ALICE}" -H "Content-Type: application/json" \
  -d "{\"providerToken\":\"${PROVIDER_SANDBOX_TOKEN}\"}")
METHOD_ID=$(echo "$VAULT_RESPONSE" | jq -r '.methodId')
[ -n "$METHOD_ID" ] && [ "$METHOD_ID" != "null" ] || { fail "setup: vault failed"; exit 1; }

# =================== GATE-SPM-01: provider vault token NEVER in response ===================
# SEC-SPM-001/002. Greppable. Any response field matching provider*Token, pm_*, re_*, tok_*, src_* = critical.
LIST=$(curl -fsS "${BASE_URL}/api/payment-methods" -H "${H_ALICE}")
if echo "$VAULT_RESPONSE" "$LIST" | grep -Eiq '"(provider[A-Za-z]*Token|providerVaultToken)"|"(pm_|tok_|src_|re_)[A-Za-z0-9]+"'; then
  fail "GATE-SPM-01: provider vault token leaked in response"
else
  pass "GATE-SPM-01: vault token not serialized"
fi

# Bonus: response shape contract
echo "$LIST" | jq -e '.methods[0] | has("methodId") and has("brand") and has("last4") and (has("providerVaultToken") | not)' >/dev/null \
  && pass "GATE-SPM-01b: response shape = {methodId, brand, last4}" \
  || fail "GATE-SPM-01b: response shape violation"

# =================== GATE-SPM-02: IDOR returns 404 not 403 ===================
# Eve probes Alice's methodId — must be indistinguishable from non-existent.
STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE \
  "${BASE_URL}/api/payment-methods/${METHOD_ID}" -H "${H_EVE}")
[ "$STATUS" = "404" ] && pass "GATE-SPM-02: IDOR cross-tenant DELETE = 404" \
                     || fail "GATE-SPM-02: IDOR returned ${STATUS} (expected 404, NEVER 403)"

# GET parity
STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
  "${BASE_URL}/api/payment-methods/${METHOD_ID}" -H "${H_EVE}")
[ "$STATUS" = "404" ] && pass "GATE-SPM-02b: IDOR cross-tenant GET = 404" \
                     || fail "GATE-SPM-02b: IDOR GET returned ${STATUS}"

# Non-existent ID for same user must also 404 (proves no info disclosure via timing/shape)
STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
  "${BASE_URL}/api/payment-methods/pm_does_not_exist" -H "${H_ALICE}")
[ "$STATUS" = "404" ] && pass "GATE-SPM-02c: own-user non-existent = 404 (parity)" \
                     || fail "GATE-SPM-02c: returned ${STATUS}"

# =================== GATE-SPM-03: delete calls provider revoke BEFORE local delete ===================
# Smoke version: delete the method, then assert provider sandbox no longer recognizes it.
# (Full assertion is integration test against provider; here we verify the runtime path executes.)
DELETE_RESPONSE=$(curl -sS -X DELETE -w '\n%{http_code}' \
  "${BASE_URL}/api/payment-methods/${METHOD_ID}" -H "${H_ALICE}")
DELETE_BODY=$(echo "$DELETE_RESPONSE" | head -n -1)
DELETE_STATUS=$(echo "$DELETE_RESPONSE" | tail -n 1)
[ "$DELETE_STATUS" = "204" ] && pass "GATE-SPM-03: DELETE returned 204" \
                             || fail "GATE-SPM-03: DELETE returned ${DELETE_STATUS}"

# Subsequent GET must 404 (local delete completed)
STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
  "${BASE_URL}/api/payment-methods/${METHOD_ID}" -H "${H_ALICE}")
[ "$STATUS" = "404" ] && pass "GATE-SPM-03b: post-delete GET = 404" \
                     || fail "GATE-SPM-03b: post-delete GET returned ${STATUS}"

# Audit event emitted (queryable via debug endpoint behind synthetic token)
AUDIT=$(curl -fsS "${BASE_URL}/_debug/spm/last-delete-audit" -H "${H_ALICE}" || echo '{}')
echo "$AUDIT" | jq -e '.provider_revoke_called == true and .provider_revoke_before_local == true' >/dev/null \
  && pass "GATE-SPM-03c: provider revoke called BEFORE local delete" \
  || fail "GATE-SPM-03c: audit shows provider revoke NOT before local delete (or audit missing)"

# =================== GATE-SPM-04: unique-key — single card per user ===================
# Re-vault first card, then attempt to vault a second.
curl -fsS -X POST "${BASE_URL}/api/payment-methods" \
  -H "${H_ALICE}" -H "Content-Type: application/json" \
  -d "{\"providerToken\":\"${PROVIDER_SANDBOX_TOKEN}\"}" > /dev/null
SECOND_STATUS=$(curl -s -o /dev/null -w '%{http_code}' -X POST \
  "${BASE_URL}/api/payment-methods" \
  -H "${H_ALICE}" -H "Content-Type: application/json" \
  -d "{\"providerToken\":\"${PROVIDER_SANDBOX_TOKEN}_alt\"}")
[ "$SECOND_STATUS" = "409" ] && pass "GATE-SPM-04: second vault returned 409 (unique-key enforced)" \
                             || fail "GATE-SPM-04: second vault returned ${SECOND_STATUS} (expected 409)"

# =================== GATE-SPM-05: opt-in checkbox default = unchecked ===================
# DOM contract — fetch checkout page HTML, assert checkbox absent or unchecked.
HTML=$(curl -fsS "${BASE_URL}/checkout?probe=opt-in-default" -H "${H_ALICE}")
# Look for the opt-in checkbox by data-testid
if echo "$HTML" | grep -Eq 'data-testid="spm-opt-in"[^>]*checked'; then
  fail "GATE-SPM-05: opt-in checkbox rendered with checked= attribute (dark pattern)"
elif echo "$HTML" | grep -q 'data-testid="spm-opt-in"'; then
  pass "GATE-SPM-05: opt-in checkbox present and default unchecked"
else
  fail "GATE-SPM-05: opt-in checkbox not rendered (data-testid='spm-opt-in' missing)"
fi

# =================== Latency budget (advisory — informational only) ===================
LAT_SAMPLES=()
for i in $(seq 1 20); do
  T=$(curl -s -o /dev/null -w '%{time_total}' "${BASE_URL}/api/payment-methods" -H "${H_ALICE}")
  LAT_SAMPLES+=("$T")
done
P95=$(printf '%s\n' "${LAT_SAMPLES[@]}" | sort -g | awk 'NR==19')
P95_MS=$(awk -v v="$P95" 'BEGIN { printf "%.0f", v*1000 }')
echo "p95 latency on GET /api/payment-methods = ${P95_MS}ms"
if [ "$P95_MS" -gt 500 ]; then
  fail "latency: p95=${P95_MS}ms > 500ms"
else
  pass "latency: p95=${P95_MS}ms ≤ 500ms"
fi

# =================== Verdict ===================
if [ "$FAIL" -ne 0 ]; then
  echo "::error::synthetic-spm: one or more P0 gates failed — canary blocked"
  exit 1
fi
echo "all P0 gates green — promotion may proceed"

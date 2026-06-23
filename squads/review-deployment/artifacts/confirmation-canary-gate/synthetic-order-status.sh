#!/usr/bin/env bash
# Synthetic probe for WI-CONFIRM-1: GET /api/checkout/orders/{orderId}/status
# Used by checkout-canary-promote.yml at each stage gate (dark/1%/10%/50%/100%).
# Exits non-zero on contract violation → triggers checkout-rollback composite action.
#
# Required env:
#   BASE_URL                   e.g. https://checkout-api.internal
#   SYNTHETIC_TEST_TOKEN       bearer token for synthetic sub (whitelisted in App Config)
#   SYNTHETIC_SUB              sub claim of the synthetic principal (owns SEEDED_ORDER_*)
#   OTHER_SUB_TOKEN            bearer for a DIFFERENT sub (IDOR check must 404, not 403)
#   SEEDED_ORDER_PENDING       orderId in pending state
#   SEEDED_ORDER_CONFIRMED     orderId in confirmed state
#   SEEDED_ORDER_PAYMENT_FAILED
#   SEEDED_ORDER_INVENTORY_RELEASED
#   SEEDED_ORDER_CANCELED
#
# Exit codes:
#   0   all gates pass
#   1   contract violation (status, headers, body)
#   2   IDOR leak (403 instead of 404, or owner data exposed)
#   3   ETag instability (same resource, different ETag across calls)
#   4   cache header drift (missing private/max-age=2)
#   5   latency budget exceeded (p95 > 800ms over 20 calls)

set -euo pipefail

: "${BASE_URL:?BASE_URL required}"
: "${SYNTHETIC_TEST_TOKEN:?SYNTHETIC_TEST_TOKEN required}"
: "${SYNTHETIC_SUB:?SYNTHETIC_SUB required}"
: "${OTHER_SUB_TOKEN:?OTHER_SUB_TOKEN required}"

FAILED=0
LOG() { printf '[%s] %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }
FAIL() { LOG "❌ $*"; FAILED=1; }
PASS() { LOG "✅ $*"; }

call() {
  local token="$1" oid="$2" extra_headers="${3:-}"
  curl -sS -o /tmp/body.$$ -w '%{http_code}|%{time_total}\n' \
    -H "Authorization: Bearer $token" \
    -H "Accept: application/json" \
    $extra_headers \
    -D /tmp/headers.$$ \
    "$BASE_URL/api/checkout/orders/$oid/status"
}

assert_status() {
  local expected="$1" actual="$2" ctx="$3"
  if [[ "$actual" != "$expected" ]]; then
    FAIL "$ctx: expected HTTP $expected, got $actual"
    return 1
  fi
  return 0
}

# ---- Gate 1: 5 order states return correct status field ----
for state in pending confirmed payment_failed inventory_released canceled; do
  var="SEEDED_ORDER_${state^^}"
  oid="${!var:-}"
  if [[ -z "$oid" ]]; then
    LOG "⚠️  $var not set, skipping state=$state"
    continue
  fi
  resp=$(call "$SYNTHETIC_TEST_TOKEN" "$oid")
  http="${resp%%|*}"
  assert_status 200 "$http" "state=$state" || continue
  actual_state=$(grep -oE '"status"\s*:\s*"[a-z_]+"' /tmp/body.$$ | head -1 | sed 's/.*"\([a-z_]*\)"$/\1/')
  if [[ "$actual_state" != "$state" ]]; then
    FAIL "state=$state body returned status=$actual_state"
  else
    PASS "state=$state returned correct status field"
  fi
done

# ---- Gate 2: IDOR — different sub MUST get 404 (not 403) ----
oid="${SEEDED_ORDER_CONFIRMED:-}"
if [[ -n "$oid" ]]; then
  resp=$(call "$OTHER_SUB_TOKEN" "$oid")
  http="${resp%%|*}"
  if [[ "$http" == "403" ]]; then
    FAIL "IDOR: other-sub got 403 (leaks existence). Must be 404."
    exit 2
  elif [[ "$http" != "404" ]]; then
    FAIL "IDOR: other-sub expected 404, got $http"
    exit 2
  else
    PASS "IDOR-safe: other-sub got 404 on existing order"
  fi
fi

# ---- Gate 3: Non-existent orderId returns 404 (parity with IDOR response) ----
resp=$(call "$SYNTHETIC_TEST_TOKEN" "00000000-0000-0000-0000-000000000000")
http="${resp%%|*}"
assert_status 404 "$http" "non-existent orderId" && PASS "non-existent → 404"

# ---- Gate 4: Cache-Control header present and correct ----
oid="${SEEDED_ORDER_CONFIRMED:-}"
if [[ -n "$oid" ]]; then
  call "$SYNTHETIC_TEST_TOKEN" "$oid" > /dev/null
  cc=$(grep -i '^cache-control:' /tmp/headers.$$ | tr -d '\r' | sed 's/^[^:]*:\s*//')
  if [[ "$cc" != *"private"* ]] || [[ "$cc" != *"max-age=2"* ]]; then
    FAIL "Cache-Control drift: got '$cc', expected 'private, max-age=2'"
    exit 4
  fi
  PASS "Cache-Control: $cc"
fi

# ---- Gate 5: ETag stability (3 calls, same resource, same ETag) ----
oid="${SEEDED_ORDER_CONFIRMED:-}"
if [[ -n "$oid" ]]; then
  etags=()
  for _ in 1 2 3; do
    call "$SYNTHETIC_TEST_TOKEN" "$oid" > /dev/null
    e=$(grep -i '^etag:' /tmp/headers.$$ | tr -d '\r ' | sed 's/^[^:]*://')
    etags+=("$e")
  done
  if [[ "${etags[0]}" != "${etags[1]}" ]] || [[ "${etags[1]}" != "${etags[2]}" ]]; then
    FAIL "ETag instability: ${etags[*]}"
    exit 3
  fi
  PASS "ETag stable across 3 calls: ${etags[0]}"

  # If-None-Match should yield 304
  resp=$(call "$SYNTHETIC_TEST_TOKEN" "$oid" "-H \"If-None-Match: ${etags[0]}\"")
  http="${resp%%|*}"
  assert_status 304 "$http" "If-None-Match" && PASS "304 honored on matching ETag"
fi

# ---- Gate 6: Latency budget — p95 < 800ms over 20 calls ----
oid="${SEEDED_ORDER_CONFIRMED:-}"
if [[ -n "$oid" ]]; then
  times=()
  for _ in $(seq 1 20); do
    resp=$(call "$SYNTHETIC_TEST_TOKEN" "$oid")
    times+=("${resp##*|}")
  done
  # Compute p95 (19th of 20 sorted ascending)
  p95=$(printf '%s\n' "${times[@]}" | sort -n | sed -n '19p')
  p95_ms=$(awk "BEGIN{printf \"%.0f\", $p95 * 1000}")
  if (( p95_ms > 800 )); then
    FAIL "Latency p95=${p95_ms}ms exceeds 800ms budget"
    exit 5
  fi
  PASS "Latency p95=${p95_ms}ms within budget"
fi

if (( FAILED > 0 )); then
  LOG "❌ confirmation-endpoint canary gate FAILED"
  exit 1
fi

LOG "🟢 confirmation-endpoint canary gate PASSED"

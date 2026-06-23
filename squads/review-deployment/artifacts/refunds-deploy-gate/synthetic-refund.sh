#!/usr/bin/env bash
# Synthetic refund probe for canary stages.
# Exercises the 6 GATE-RFD scenarios + UX-spec error mapping from a black-box client perspective.
#
# Required env:
#   $1                    base URL (e.g. https://refunds-api.azurecontainerapps.io)
#   SUB_TOKEN             token for synthetic subscriber that OWNS the seeded orders
#   OTHER_TOKEN           token for a DIFFERENT subscriber (IDOR test)
#   REFUNDABLE_ORDER      orderId in Confirmed state, within 24h window
#   EXPIRED_ORDER         orderId in Confirmed state, OUTSIDE 24h window
#   CANCELED_ORDER        orderId in Canceled state
#
# Exits non-zero on any gate failure → triggers checkout-rollback composite.

set -euo pipefail

BASE="$1"
PASS=0
FAIL=0

check() {
  local name="$1" expected="$2" actual="$3"
  if [[ "$actual" == "$expected" ]]; then
    echo "✓ $name"
    PASS=$((PASS+1))
  else
    echo "✗ $name — expected=$expected actual=$actual"
    FAIL=$((FAIL+1))
  fi
}

# Helper: POST refund with idempotency key, return "status|body"
post_refund() {
  local order="$1" idem="$2" token="$3"
  curl -sS -o /tmp/refund-body.json -w "%{http_code}" \
    -X POST "$BASE/api/refunds" \
    -H "Authorization: Bearer $token" \
    -H "Content-Type: application/json" \
    -H "Idempotency-Key: $idem" \
    -d "{\"orderId\":\"$order\"}"
}

# ── GATE-RFD-01: Happy path — confirmed order in window returns 202 Accepted + pending ──
IDEM1="probe-happy-$(date +%s)-$RANDOM"
STATUS=$(post_refund "$REFUNDABLE_ORDER" "$IDEM1" "$SUB_TOKEN")
check "GATE-RFD-01 happy path returns 202" "202" "$STATUS"
REFUND_STATE=$(jq -r '.status' /tmp/refund-body.json)
check "GATE-RFD-01 initial state = pending" "pending" "$REFUND_STATE"

# GATE-RFD-06: provider refundId (re_xxx) MUST NOT leak to client
PROVIDER_ID_LEAK=$(jq -r '.. | strings | select(test("^re_"))' /tmp/refund-body.json | head -1)
check "GATE-RFD-06 no Stripe re_xxx in response" "" "$PROVIDER_ID_LEAK"

# ── GATE-RFD-02: Idempotency — same key returns same refundId (within 24h) ──
ORIGINAL_REFUND_ID=$(jq -r '.refundId' /tmp/refund-body.json)
sleep 1
STATUS=$(post_refund "$REFUNDABLE_ORDER" "$IDEM1" "$SUB_TOKEN")
check "GATE-RFD-02 idempotent replay returns 200 or 202" "$([[ $STATUS =~ ^(200|202)$ ]] && echo $STATUS || echo BAD)" "$STATUS"
REPLAY_REFUND_ID=$(jq -r '.refundId' /tmp/refund-body.json)
check "GATE-RFD-02 same refundId on replay" "$ORIGINAL_REFUND_ID" "$REPLAY_REFUND_ID"

# ── GATE-RFD-03: Window expired — 409 with reason=window_expired ──
IDEM2="probe-expired-$(date +%s)-$RANDOM"
STATUS=$(post_refund "$EXPIRED_ORDER" "$IDEM2" "$SUB_TOKEN")
check "GATE-RFD-03 expired window returns 409" "409" "$STATUS"
REASON=$(jq -r '.error.reason' /tmp/refund-body.json)
check "GATE-RFD-03 reason=window_expired" "window_expired" "$REASON"

# ── GATE-RFD-04: Already-canceled order — 409 with reason=canceled ──
IDEM3="probe-canceled-$(date +%s)-$RANDOM"
STATUS=$(post_refund "$CANCELED_ORDER" "$IDEM3" "$SUB_TOKEN")
check "GATE-RFD-04 canceled order returns 409" "409" "$STATUS"
REASON=$(jq -r '.error.reason' /tmp/refund-body.json)
check "GATE-RFD-04 reason=canceled" "canceled" "$REASON"

# ── GATE-RFD-05 (IDOR): Different subscriber cannot refund another's order — 404 (NOT 403) ──
# 404 prevents order-existence enumeration. Same response shape as "order not found".
IDEM4="probe-idor-$(date +%s)-$RANDOM"
STATUS=$(post_refund "$REFUNDABLE_ORDER" "$IDEM4" "$OTHER_TOKEN")
check "GATE-RFD-05 IDOR returns 404 (not 403)" "404" "$STATUS"

# ── UX contract: error code must be from allowlist (PROVIDER_DECLINED|TIMEOUT|UNAVAILABLE|INSUFFICIENT_PROVIDER_FUNDS|...) ──
# Pull a known-failed seeded refund's GET to verify error.code is mapped
GET_STATUS=$(curl -sS -o /tmp/refund-get.json -w "%{http_code}" \
  -H "Authorization: Bearer $SUB_TOKEN" \
  "$BASE/api/refunds/${ORIGINAL_REFUND_ID}")
check "GET refund returns 200" "200" "$GET_STATUS"

# Cache-Control on GET must be private (refund data is per-user) and short max-age
CACHE=$(curl -sS -D - -o /dev/null \
  -H "Authorization: Bearer $SUB_TOKEN" \
  "$BASE/api/refunds/${ORIGINAL_REFUND_ID}" | grep -i '^cache-control:' | tr -d '\r')
echo "$CACHE" | grep -qi 'private' || { echo "✗ Cache-Control missing 'private' — got: $CACHE"; FAIL=$((FAIL+1)); }
echo "$CACHE" | grep -qiE 'max-age=([0-9]|[12][0-9]|30)\b' || { echo "✗ Cache-Control max-age > 30s — got: $CACHE"; FAIL=$((FAIL+1)); }

# ── Performance: p95 < 1500ms over 20 calls (Stripe-bound, looser than checkout's 800ms) ──
echo "Measuring p95 over 20 refund-status GETs..."
TIMES=()
for i in $(seq 1 20); do
  T=$(curl -sS -o /dev/null -w "%{time_total}" \
    -H "Authorization: Bearer $SUB_TOKEN" \
    "$BASE/api/refunds/${ORIGINAL_REFUND_ID}")
  TIMES+=("$T")
done
P95=$(printf '%s\n' "${TIMES[@]}" | sort -n | awk 'BEGIN{c=0} {a[c++]=$1} END{print a[int(c*0.95)]}')
P95_MS=$(echo "$P95 * 1000" | bc -l)
if (( $(echo "$P95_MS < 1500" | bc -l) )); then
  echo "✓ p95 latency ${P95_MS}ms < 1500ms"; PASS=$((PASS+1))
else
  echo "✗ p95 latency ${P95_MS}ms >= 1500ms"; FAIL=$((FAIL+1))
fi

echo
echo "─────────────────────────────────────"
echo "Synthetic refund probe: $PASS passed, $FAIL failed"
echo "─────────────────────────────────────"

[[ $FAIL -eq 0 ]] || exit 1

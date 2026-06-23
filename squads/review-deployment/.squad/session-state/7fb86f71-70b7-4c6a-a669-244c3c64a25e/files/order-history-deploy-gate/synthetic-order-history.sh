#!/usr/bin/env bash
# Post-deploy smoke probe for Order History vertical (WI-HIST-1).
# Validates the 6 contract gates that map to merged PR acceptance criteria.
# Exits non-zero on any gate failure → triggers rollback job in order-history-deploy.yml.
set -euo pipefail

: "${API_BASE:?API_BASE required}"
: "${SYNTHETIC_SUB_TOKEN:?SYNTHETIC_SUB_TOKEN required}"
: "${OTHER_SUB_TOKEN:?OTHER_SUB_TOKEN required}"

pass=0; fail=0
gate() { local name=$1; local cond=$2
  if eval "$cond"; then echo "✓ $name"; pass=$((pass+1))
  else echo "✗ $name"; fail=$((fail+1)); fi
}

H_MINE="Authorization: Bearer $SYNTHETIC_SUB_TOKEN"
H_OTHER="Authorization: Bearer $OTHER_SUB_TOKEN"

# Gate HIST-01: 200 + page envelope present
body=$(curl -fsS -H "$H_MINE" "$API_BASE/api/orders?pageSize=5")
gate "HIST-01 envelope { items, nextCursor }" \
  "echo '$body' | jq -e '.items and (.nextCursor==null or (.nextCursor|type==\"string\"))' >/dev/null"

# Gate HIST-02: cursor pagination stable — same cursor returns same page
cursor=$(echo "$body" | jq -r '.nextCursor // empty')
if [ -n "$cursor" ]; then
  p1=$(curl -fsS -H "$H_MINE" "$API_BASE/api/orders?pageSize=5&cursor=$cursor" | jq -S '.items')
  p2=$(curl -fsS -H "$H_MINE" "$API_BASE/api/orders?pageSize=5&cursor=$cursor" | jq -S '.items')
  gate "HIST-02 cursor idempotent" "[ \"$p1\" = \"$p2\" ]"
else
  echo "⊘ HIST-02 skipped (no second page seeded)"
fi

# Gate HIST-03: pageSize cap enforced (>50 → 400)
code=$(curl -s -o /dev/null -w '%{http_code}' -H "$H_MINE" "$API_BASE/api/orders?pageSize=500")
gate "HIST-03 pageSize cap (>50 → 400)" "[ \"$code\" = \"400\" ]"

# Gate HIST-04: IDOR-safe — token A cannot see token B's orders
mine_ids=$(echo "$body" | jq -r '.items[].id' | sort -u)
other_ids=$(curl -fsS -H "$H_OTHER" "$API_BASE/api/orders?pageSize=50" | jq -r '.items[].id' | sort -u)
overlap=$(comm -12 <(echo "$mine_ids") <(echo "$other_ids") | wc -l)
gate "HIST-04 IDOR isolation (zero cross-sub overlap)" "[ \"$overlap\" -eq 0 ]"

# Gate HIST-05: Cache-Control private, max-age small (≤30s for list endpoint)
cc=$(curl -fsSI -H "$H_MINE" "$API_BASE/api/orders?pageSize=1" | grep -i '^cache-control:' | tr -d '\r')
gate "HIST-05 Cache-Control private + max-age" \
  "echo '$cc' | grep -qi private && echo '$cc' | grep -qiE 'max-age=([0-9]|[12][0-9]|30)\\b'"

# Gate HIST-06: p95 latency < 500ms over 20 calls (read-only single-partition query)
times=()
for _ in $(seq 1 20); do
  t=$(curl -fsS -o /dev/null -w '%{time_total}' -H "$H_MINE" "$API_BASE/api/orders?pageSize=10")
  times+=("$t")
done
p95=$(printf '%s\n' "${times[@]}" | sort -n | awk 'BEGIN{c=0} {a[c++]=$1} END{print a[int(c*0.95)]}')
gate "HIST-06 p95 < 500ms (got ${p95}s)" "awk -v v=$p95 'BEGIN{exit !(v<0.5)}'"

echo "---"
echo "PASS=$pass FAIL=$fail"
[ "$fail" -eq 0 ]

# Pre-Prod Security Gate Wire-up

**Owner:** review-deployment-squad
**Source:** security-hardening-squad's `PREPROD-SECURITY-GATE.md` (17 items, 3 tiers)
**Target repo:** `tamirdresher/travel-assistant`

## What this bundle does

Wires security-hardening's checklist into the canary promotion workflow as a **binding gate** — not just a grep snippet, but a hardened job that:

1. **Fails-closed on missing doc** — can't delete `preprod-security-gate.md` to bypass.
2. **Fails-closed on missing rows** — can't delete a GATE-N row to bypass (anti-tamper).
3. **Re-evaluates at every stage** — flipping a ☐ mid-rollout halts at the next stage boundary.
4. **Enforces sign-off matrix at ≥50% traffic** — all 4 squad sign-offs required before majority traffic.
5. **Caps doc staleness at 90 days** — forces security re-attestation periodically.

## Apply order (for maintainer)

### Step 1 — Drop security doc into target repo

```bash
mkdir -p docs/security
cp squads/security-hardening/artifacts/preprod-security-gate/PREPROD-SECURITY-GATE.md \
   docs/security/preprod-security-gate.md
git add docs/security/preprod-security-gate.md
git commit -m "docs(security): add binding pre-prod security gate (17 items)"
```

### Step 2 — Patch canary promotion workflow

After `checkout-canary-promote.yml` lands in `.github/workflows/` (from `squads/review-deployment/artifacts/checkout-canary-gates/`):

1. Open `.github/workflows/checkout-canary-promote.yml`.
2. Paste the entire `security-gate:` job from `security-gate-job.yml` (this dir) into the `jobs:` map.
3. Apply `workflow-patch.diff` (this dir) to add `needs: [security-gate]` to every `promote-*` job.

```bash
# Verify after patch
gh workflow view checkout-canary-promote.yml --repo tamirdresher/travel-assistant
```

### Step 3 — First-run validation

Before promoting any canary stage:

```bash
# Local dry-run of gate logic
cd tamirdresher/travel-assistant
bash -c 'p0=$(grep -cE "^\| \*\*GATE-[1-8]\*\*.*☐" docs/security/preprod-security-gate.md); echo "P0 open: $p0"'
# Expected on day 1: P0 open: 8 (all unchecked — blocks all promotion)
```

This is **correct, expected behavior** — promotion is blocked until security/app-dev/infra flip their ☐→✅ with linked evidence (run ID, test name, PR commit).

## Gate-to-stage matrix

| Stage | Required gates |
|-------|---------------|
| dark | GATE-1..8 (P0 all green) |
| 1% | GATE-1..8 + BUG-1, BUG-2, confirmation 6-gate probe |
| 10% | GATE-1..8 (re-checked, no regressions) |
| 50% | GATE-1..8 + full sign-off matrix (4 squads) |
| 100% | GATE-1..8 + full sign-off matrix + 90d freshness |

P1/P2 items do NOT block promotion — they emit warnings to the GitHub Step Summary and are tracked as 7-day / backlog SLA respectively.

## Out of scope (next backlog)

- **P1 SLA enforcement** — currently warn-only after promotion. Future: scheduled workflow that opens GH issue if GATE-9..13 still ☐ at day-7 post-prod.
- **DAST automation (GATE-17)** — needs Burp/ZAP runner; pair with infra squad next sprint.
- **SBOM submission (GATE-15)** — GH Dependency Submission API requires `actions: write` scope expansion.

## EMU lockout reminder

`tamirdresher_microsoft` cannot push to `tamirdresher/travel-assistant`. Maintainer must:
1. `cp` the 3 files in this bundle into the target repo under the paths listed above.
2. Commit with message: `ci(security): wire preprod security gate into canary promotion`.
3. Push to a branch (e.g. `ci/preprod-security-gate`), open PR, merge after one reviewer approval.

---

**Review-deployment squad turn complete.** Checkout vertical now has 3 stacked canary gates:

1. **Code/contract gates** (BUG-1, BUG-2 idempotency + replay) — gates 1% stage
2. **Confirmation 6-gate synthetic** (states + IDOR + cache + ETag + latency) — gates 1% stage
3. **Security gate** (P0 + sign-off matrix + staleness) — gates **every** stage

Backlog empty. Awaiting next phase work.

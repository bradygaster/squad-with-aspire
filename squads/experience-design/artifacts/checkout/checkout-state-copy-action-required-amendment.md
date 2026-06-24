# Checkout state-copy amendment — `ActionRequired` (3DS) row

**Date:** 2026-06-24
**Author:** experience-design squad
**Status:** Frozen (binding)
**Amends:** `checkout-state-copy-and-analytics-spec.md` (commit `31e42b1`) — adds the `ActionRequired` row that was previously left as "provider-owned placeholder" pending DR-CO-3DS-SURFACE-001.
**Resolution source:** CHECKOUT-SPEC-002 R1 (ideation, commit `762de20`) — in-browser redirect locked, iframe/native sheets deferred.
**Also resolves:** ideation's open observation re: `insufficient_funds_terminal` sub-copy tell — **already shipped in commit `99b2cc3` (SEC-CHK-008 amendment)**. No DR-CO-FRAUD-002 needed; see §4 below.

---

## §1. State-machine update

The 7-state machine in `checkout-state-copy-and-analytics-spec.md` §1 grows by one to **8 states** with `ActionRequired` formally specified:

| # | State | Trigger | Terminal? |
|---|-------|---------|-----------|
| 8 | `ActionRequired` | `confirming` resolves to `three_ds_required` per CHECKOUT-SPEC-002 R1 | No — transitions to `confirmed` OR `failed_retryable` OR `failed_terminal` after redirect-return |

**Surface lifecycle:**

1. User clicks Place Order on `/review` → state `confirming`, status surface renders "Processing your order…" copy (unchanged from §3.1).
2. Server responds with `{ state: "action_required", redirectUrl, returnUrl: "/checkout/{id}/review?resume=1" }`.
3. Frontend renders **`ActionRequired` copy below** for ≤500ms, then executes `window.location.assign(redirectUrl)`. Full-page navigation; provider domain owns the 3DS challenge surface (out of our scope per CHECKOUT-SPEC-002 R1).
4. User completes 3DS at provider → provider redirects to `returnUrl`.
5. Frontend mounts `/checkout/{id}/review?resume=1`, immediately issues `GET /status` poll (no user click required), state machine resumes normally (`confirming` → terminal).

**Why ≤500ms render window:** the redirect MUST be the dominant signal — a long-lived "Verifying with your bank…" copy block would create a competing focus target with the redirect. We render for at most one frame so screen readers announce the live-region message before navigation begins, then transfer control to the provider domain. If `window.location.assign` is blocked (popup blocker on some configs treating navigation as popup — rare but observed on Safari iOS), the copy persists and the secondary CTA "Continue to bank verification" becomes interactive.

---

## §2. Copy table addition — append to spec §3.1

| State | Reason / Variant | Heading (`*-status-heading[data-state]`) | Sub-copy | Primary CTA | Secondary CTA |
|-------|------------------|------------------------------------------|----------|-------------|---------------|
| `ActionRequired` | `three_ds_required` (sole v1 variant) | `Verifying with your bank` | `Your bank needs to confirm this purchase. We'll redirect you in a moment.` | (none — auto-redirect within 500ms) | `Continue to bank verification` (manual fallback if auto-redirect blocked; href = `redirectUrl`) |

**`data-state`** = `"action_required"` (kebab→snake matches `failed_terminal` / `failed_retryable` convention).
**`data-reason`** = `"three_ds_required"` (full fidelity — NOT a fraud-oracle surface; 3DS is a routine commerce flow, not a security signal worth coarsening).

**Late-poll hint at 10s** (matches `confirming` row from spec §3.1): if `ActionRequired` persists >10s without redirect firing (e.g., `window.location.assign` silently failed), append below the sub-copy: `Still preparing the verification step. If nothing happens, use the link below.` and ensure the secondary CTA is keyboard-focusable. This mirrors the `confirming` 10s late-hint pattern verbatim — same telemetry event family, same copy register.

**No cancel affordance.** Once 3DS is initiated, the auth is in motion at the provider; client-side "cancel" would only abandon the UI, not abort the auth. Same rationale as `confirming` state. If user wants out: browser back from provider domain returns them to `/checkout/{id}/review?resume=1` where the resume poll will resolve naturally (most commonly to `failed_retryable` with `reason="three_ds_failed"` or `"three_ds_abandoned"` per app-dev's mapper table).

---

## §3. Focus & live-region update — append to spec §3.4

| State | Focus on entry | Live-region |
|-------|----------------|-------------|
| `ActionRequired` | **No focus move.** Mirrors `confirming` policy from `focus-and-live-region-policy.md` — happy-path progress states do not steal focus. Heading is announced via `aria-live="polite"` from the status surface. | Polite. Text: `Verifying with your bank. Your bank needs to confirm this purchase. We'll redirect you in a moment.` Throttled per policy (1500ms min between announcements). |

**On `returnUrl` mount (`/checkout/{id}/review?resume=1`):** focus is **not** moved to the resumed status heading on initial mount — the resume is a continuation of the user's prior intent, not a new arrival surface. Focus stays at document default. Once the poll resolves to a terminal state, focus rules from §3.4 apply normally (h1 focus on `failed_terminal` / `failed_retryable`; no focus move on `confirmed`).

**URL stability (matches `cancel-status` and `confirmation` patterns):** the `?resume=1` query param is consumed via `history.replaceState(null, '', '/checkout/{id}/review')` on `/review` mount — single-use, removed from URL after first poll cycle so refresh doesn't re-trigger resume logic. If the resumed poll returns `confirmed`, the standard `replaceState` redirect to `/checkout/confirmation/{orderId}` fires per spec §1.

---

## §4. Re: ideation's "insufficient_funds_terminal sub-copy tell" observation

Ideation's SPEC-003 ratification flagged that the original §3.1 `failed_terminal` sub-copy for `insufficient_funds_terminal` said "different payment method" — a tell vs `hard_decline`.

**Already resolved in commit `99b2cc3` (SEC-CHK-008 amendment).** Per security-hardening's binding R1 verdict, all 4 `failed_terminal` reasons now render byte-identical DOM:

| Field | Bytes (all 4 reasons) |
|-------|----------------------|
| Heading | `Your payment couldn't be processed` |
| Sub-copy | `We weren't able to complete your order. Please review your payment details or try a different payment method.` |
| `data-reason` | `declined_terminal` (literal — NOT the underlying enum) |
| Primary CTA | `Try again` → `/cart` |
| Secondary CTA | `Get help` → `/help/payments` |

The single-row collapse + `data-reason` coarsening + telemetry hardcoded-literal rule (no `response.reason` forwarding at `safeTrack` call site) close the oracle at every observable layer.

**No DR-CO-FRAUD-002 needed.** The original §3.1 four-row variant table is dead; the SEC-CHK-008 amendment is the canonical floor. Ideation can mark the open observation closed.

---

## §5. Testid contract — addendum to spec §6

Three additional bindings for the `ActionRequired` surface; mostly reuse of frozen testids with new `data-state` value:

| testid | Required attributes | Notes |
|--------|---------------------|-------|
| `checkout-status-heading` | `data-state="action_required"`, `data-reason="three_ds_required"` | Same element as other state surfaces — `data-state` discriminates. |
| `checkout-status-subcopy` | (no data attrs) | Same wrapper `<p>` as other states. |
| `checkout-3ds-fallback-link` | `href={redirectUrl}` | NEW testid — the secondary CTA "Continue to bank verification". Auto-redirect fires in ≤500ms; this link is the manual fallback when navigation is blocked. QA bundle should assert `data-testid="checkout-3ds-fallback-link"` is present AND focusable on the rare blocked-redirect path (mockable via test seam: `?_force_3ds_fallback=1`). |

`checkout-retry-button` is **absent** in `ActionRequired` (no retry — the action is "continue to provider", not "try again"). Discipline gate: `expect(retryButton).toHaveCount(0)` for any `data-state="action_required"` test, same pattern as `failed_terminal`.

---

## §6. Telemetry — append to spec §5.1 event list

One additional event, plus an `ActionRequired`-flavored entry of the existing `checkout_step_entered`:

| Event | Props | Notes |
|-------|-------|-------|
| `checkout_step_entered` | `step: "action_required"`, `checkoutSessionId`, `attemptNumber` | Fires on initial `ActionRequired` render (BEFORE redirect). |
| `checkout_3ds_redirected` | `checkoutSessionId`, `attemptNumber`, `redirectDelayMs` (time between render and `window.location.assign` call) | NEW event. Fires immediately before `window.location.assign`. `redirectDelayMs` should be ≤500ms by spec — value >500ms indicates browser/extension interference and is useful signal for bundle 7 dashboarding. |
| `checkout_3ds_resumed` | `checkoutSessionId`, `attemptNumber`, `resumeOutcome: "confirmed"\|"failed_retryable"\|"failed_terminal"`, `resumeLatencyMs` (from `/review?resume=1` mount to first terminal poll) | NEW event. Fires on resume-poll terminal resolution. `resumeOutcome` is coarsened on `failed_terminal` per fraud-oracle floor — string is literally `"failed_terminal"`, not enum. |

**No `checkout_3ds_abandoned` event v1** — if user hits browser back from provider domain, the resume poll naturally resolves to a `failed_retryable` with `reason="three_ds_abandoned"` (per app-dev mapper); that becomes a `checkout_failed_retryable` event via the existing pipeline. Adding a separate `_abandoned` event would create double-counting risk.

---

## §7. Bundle delta

This amendment is **additive only** — no existing copy changes, no existing testid renames, no existing telemetry event changes. Frontend WI-CHECKOUT-7 ingests this row alongside the SEC-CHK-008 amendment as input on first author. Net bundle impact: ~0.4KB gz (one new copy row, one new testid, two new telemetry events), well within the 6-8KB gz envelope.

---

## §8. Asks closed by this amendment

- **CHECKOUT-SPEC-003 §"Still open" DR-CO-3DS-SURFACE-001** → closed (in-browser redirect locked at SPEC-002 R1; this amendment ships the corresponding UX surface).
- **My open Q3 to app-dev** (3DS surface decision) → closed.
- **Ideation's `insufficient_funds_terminal` sub-copy observation** → already closed via commit `99b2cc3`. No DR-CO-FRAUD-002 needed.

---

## §9. Open Qs to app-dev (NEW — non-blocking, only affect telemetry granularity)

1. **`redirectUrl` shape:** is it a full absolute URL (`https://3ds.adyen.com/...`) or a relative URL? Affects whether `window.location.assign` needs URL validation client-side (we won't validate origin against an allowlist v1 — provider domain rotation makes that brittle — but I want confirmation it's always absolute so we don't accidentally same-origin-navigate).
2. **`returnUrl` echo on resume poll:** when frontend issues `GET /status` after `?resume=1` mount, does the response payload include the original `returnUrl` for verification? Not strictly needed v1 (we trust the URL we already navigated to), but useful for telemetry correlation if 3DS provider rewrites it.
3. **`three_ds_abandoned` vs `three_ds_failed` distinction in mapper:** confirms `MappingTable` distinguishes user-cancellation (back-button from provider) from auth-failure (provider returned challenge-failed). UX copy doesn't differentiate (both → `failed_retryable` generic "Try again"), but analytics granularity wants it.

All three are non-blocking for frontend authoring — sensible defaults work, telemetry granularity improves when answered.

---

**Bundle locked.** State machine now 8 states, copy table 8 rows + per-reason sub-rows, focus policy covers all states, testid contract covers all surfaces, telemetry event list covers all transitions. Contract surface for WI-CHECKOUT-1 frontend on the exp-design side is **complete**.

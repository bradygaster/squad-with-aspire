# Next Vertical: Order History ("My Orders")

**Date:** 2026-06-24
**Filed by:** ideation-research-planning-squad
**EMU note:** gh issue creation blocked in tamirdresher/travel-assistant — this artifact is the issue substitute. Squads pick up via DM dispatch (separate turn).
**Precedes:** confirmation page (WI-CONFIRM-1/2/3 shipped, see `NEXT-VERTICAL-confirmation-page.md`)

---

## Why this next

1. **Confirmation page closes one order.** Users now need to find that order again 5 minutes later, 5 days later, 5 months later — without a "where did my receipt go?" support ticket.
2. **Read-mostly, no money movement.** Reuses checkout's order-store, no payment provider scope, no PCI delta. Low-risk vertical that exercises pagination + auth-scoped queries — both reusable primitives we don't have yet.
3. **Unblocks refunds.** Refunds (out-of-scope from checkout) start from "find the order." Order history is the prerequisite UI surface and the prerequisite query API.
4. **Cheap canary.** Behind existing `feature.checkout` flag — same gate as confirmation page. No new flag, no new rollout.

---

## Scope (in)

- Authenticated user lists their own orders (terminal + in-flight, last 90 days default)
- Server-side pagination (cursor-based, page size 20, max 50)
- Filter: status (any of `pending|confirmed|failed_post_auth|canceled|inventory_released|reconciliation_delayed`)
- Sort: `createdAt desc` only (no user-selectable sort — keeps index simple)
- Click row → deep-link to existing confirmation page (`/checkout/confirmation/{orderId}`) with read-only banner if terminal
- Guest orders accessible via signed magic link from confirmation email (separate WI, OUT OF SCOPE here — guests don't get a history list)

## Scope (out — explicitly)

- Refunds initiation (separate vertical, depends on this one)
- Order re-purchase / re-order button
- Order detail page beyond the existing confirmation view
- Search by order ID / SKU (defer — usage data first)
- Export to CSV/PDF
- Admin/back-office views
- Cross-tenant or organization-shared order views

---

## Work Items

### WI-HIST-1 — `application-development-squad` — Order list API

**Endpoint:** `GET /api/orders?cursor={opaque}&limit=20&status={csv}`

**Acceptance criteria:**
- **AC1 — Auth scope:** Requires `sub` claim from JWT. No `sub` → `401`. Returns ONLY orders where `order.userId == sub`. Cross-tenant request returns empty page, never `403` (avoid enumeration oracle — same IDOR-safe pattern as `wi-confirm-1-order-status`).
- **AC2 — Pagination:** Cursor-based using `(createdAt, orderId)` composite. `limit` clamped to `[1, 50]`, default 20. Response shape: `{ "orders": [...], "nextCursor": "..." | null }`. Cursor is opaque base64 of `{createdAt, orderId}` — clients MUST NOT parse.
- **AC3 — Filter:** `status` is CSV of allowed enum values. Unknown values → `400` with `{"error":"unknown_status","invalid":[...]}`. Empty/missing → all statuses.
- **AC4 — Window:** Default returns last 90 days. `?since=ISO8601` allowed, clamped to max 365 days back. No future dates → `400`.
- **AC5 — Order payload:** `{ orderId, createdAt, status, totalMinorUnits, currency, lineItemCount, lastUpdatedAt }`. NO PAN, NO billing address, NO line item detail — list view only. Full detail comes from confirmation page deep-link.
- **AC6 — Caching:** `Cache-Control: private, max-age=10`. ETag over `(orders[].orderId, orders[].status, orders[].lastUpdatedAt, nextCursor)` — busts on any state change but NOT on heartbeat-only `updatedAt` touches (consistent with confirmation page state-only ETag rule).
- **AC7 — Index:** Cosmos partition key `/userId`, composite index `(userId, createdAt desc, orderId)`. Single-partition query for the common case (one user's history).
- **AC8 — Rate limit:** Per-`sub` token bucket: 60 req/min, burst 10. `429` with `Retry-After` on breach. Same middleware as confirmation polling.
- **AC9 — Empty state:** Zero orders returns `200` with `{"orders":[], "nextCursor":null}`. Never `404`.
- **AC10 — Observability:** Emit metric `checkout.orders.list.requests{status_filter, page_size}` and `checkout.orders.list.latency_ms{p50,p95,p99}`. SLO P95 < 200ms (read-mostly, single partition).

**Hard NO:** No `userId` query param. No admin override. No "include guest orders" mode. No `/orders/{orderId}` sibling — use existing confirmation status endpoint.

**Bundle target:** `squads/application-development/artifacts/checkout/wi-hist-1-order-list/`

---

### WI-HIST-2 — `quality-testing-squad` — API contract + IDOR + pagination tests

**Acceptance criteria:**
- **AC1 — Contract tests** (xUnit + `CheckoutWebApplicationFactory`): all 10 ACs above, especially AC1 IDOR (UserA cannot see UserB orders even with crafted cursor), AC2 cursor stability under concurrent insert, AC3 unknown-status rejection, AC6 ETag stability across heartbeat-only updates.
- **AC2 — Pagination property tests:** seed 200 orders, iterate all pages via `nextCursor` until `null`, assert every order appears exactly once and ordering is monotone non-increasing on `createdAt`.
- **AC3 — Auth tests:** no JWT → 401; expired JWT → 401; valid JWT, no orders → 200 empty; valid JWT, mixed-user data → only own orders returned.
- **AC4 — Cursor tamper tests:** mutated cursor (flipped bit) → 400 `invalid_cursor`. Cursor from UserA used by UserB → returns UserB's first page (cursor is hint, not auth — userId still filtered from `sub`).
- **AC5 — Load test:** k6 script `order-history-load.js` — 50 RPS sustained 5min, 95th < 200ms, error rate < 0.1%. Mixed page sizes (20/50), mixed status filters.
- **AC6 — NO frontend a11y tests in this WI** — defer to WI-HIST-4.

**Bundle target:** `squads/quality-testing/artifacts/checkout/wi-hist-2-order-list-tests/`

---

### WI-HIST-3 — `experience-design-squad` — UX spec for list view

**Acceptance criteria:**
- **AC1 — Layout:** Mobile-first table-as-list. Each row: status pill (color + text — never color-only), order date (relative for <7d, absolute after), total + currency, item-count summary ("3 items"), chevron affordance.
- **AC2 — Status pills:** Reuse confirmation page status colors. WCAG AA contrast on both background and text. Color-blind-safe (test with Sim Daltonism / Stark — protanopia, deuteranopia, tritanopia).
- **AC3 — Empty state:** "No orders yet" + CTA back to catalog. Not an error state — no red, no warning iconography.
- **AC4 — Loading state:** Skeleton rows (3 visible), no spinner. Skeleton respects reduced-motion (static shimmer, not animated).
- **AC5 — Pagination UI:** "Load more" button at list end (NOT auto-load on scroll — predictable for screen readers, no surprise content). Button disabled state while loading; `aria-busy` on list container.
- **AC6 — Filter UI:** Single multi-select dropdown for status, defaulting to "All". URL-syncs (`?status=...`) so back-button is sane.
- **AC7 — Deep-link:** Row click navigates to existing confirmation page. Terminal-state orders get a banner "This order is complete — read-only" on the confirmation page (banner spec owned by experience-design, app-dev implements).
- **AC8 — Analytics events:** `order_history.viewed`, `order_history.filter_applied{status}`, `order_history.page_loaded{page_number}`, `order_history.row_clicked{order_id_hashed, status}` — order ID hashed in analytics (consistency with existing analytics-PII policy).
- **AC9 — Bundle budget:** Page + hook + styles ≤ 6KB gzipped (smaller than confirmation page — no polling, no animations).

**Bundle target:** `squads/experience-design/artifacts/checkout/wi-hist-3-list-spec/`

**Implements:** WI-HIST-4 (frontend) — design owns spec, app-dev owns code.

---

### WI-HIST-4 — `application-development-squad` (impl) + `experience-design-squad` (review) — Frontend page + hook

**Acceptance criteria:**
- **AC1 — Hook:** `useOrderHistory({ status, pageSize })` returns `{ orders, isLoading, error, loadMore, hasMore }`. Uses native `fetch` + `AbortController` on unmount. NO polling.
- **AC2 — Cache:** In-memory page cache keyed by `(status, cursor)`. Cleared on auth state change. Honors `Cache-Control: private, max-age=10` server-side hint.
- **AC3 — Error handling:** 401 → redirect to login with `returnTo` query. 429 → user-visible "Too many requests, try again in {Retry-After}s" + auto-retry once after the window. 5xx → "Something went wrong" + manual retry button.
- **AC4 — A11y:** `<ul role="list">` of `<li>` rows, each row a `<a href>` (real link, not div+onClick). Status pill text in accessible name. "Load more" button announces remaining count via `aria-live=polite` only on user action.
- **AC5 — Focus management:** After "Load more", focus moves to first newly-loaded row (announcement: "{N} more orders loaded"). After filter change, focus stays on filter (no surprise jump).
- **AC6 — Implements WI-HIST-3 spec byte-for-byte.** Any deviation requires experience-design sign-off in PR.

**Bundle target:** `squads/application-development/artifacts/checkout/wi-hist-4-history-frontend/`

---

### WI-HIST-5 — `security-hardening-squad` — Threat review + IDOR sign-off

**Acceptance criteria:**
- **AC1 — IDOR matrix:** Verify AC1 (auth scope) of WI-HIST-1 holds against: forged `sub`, replayed JWT, cursor from another user, cursor with mutated `userId` field, missing `sub` claim, `sub` of revoked user.
- **AC2 — Enumeration oracle:** Confirm response time for "no orders" vs "orders exist but filtered out" is statistically indistinguishable (±10ms p95). If not, add constant-time padding or refactor.
- **AC3 — Cursor opacity:** Cursor MUST be base64 of HMAC-signed payload OR encrypted blob — NOT plain base64 JSON. Server rejects tampered cursors with `400`, never reveals which field was tampered.
- **AC4 — Rate limit bypass:** Verify per-`sub` bucket cannot be bypassed by rotating IP, by anonymous-then-auth, or by burst across multiple tabs.
- **AC5 — Logging:** Order list responses MUST NOT log full order payloads — only `{userId_hash, count, page_size, status_filter, latency}`. Audit log on `403`/`401`/`429`.
- **AC6 — Sign-off:** Security writes `SECURITY-SIGNOFF.md` in WI-HIST-1 bundle naming each AC as ✅ / ⚠️ / ❌ with evidence. ❌ = blocking.

**Bundle target:** Appended to `squads/security-hardening/artifacts/checkout/wi-hist-5-signoff/`

---

### WI-HIST-6 — `azure-infrastructure-squad` — Cosmos composite index + cost check

**Acceptance criteria:**
- **AC1 — Composite index:** Add `(userId asc, createdAt desc, orderId asc)` composite to Cosmos container Bicep. Single-partition queries on `/userId` PK.
- **AC2 — Cost projection:** Estimate RU/s delta for 1000 DAU × avg 5 history views/session. Document in bundle README. If projection > +$20/mo on top of current $586 baseline, flag for product decision (autoscale vs manual scale).
- **AC3 — TTL guardrail:** Confirm no order container TTL is active that would expire orders < 365 days (the AC4 window). If TTL exists and is shorter, raise as blocker.
- **AC4 — Index build:** Ship index changes as additive-only Bicep — never remove an existing index in same PR (Cosmos requires re-index on remove, expensive).

**Bundle target:** `squads/azure-infrastructure/artifacts/checkout/wi-hist-6-cosmos-index/`

---

### WI-HIST-7 — `review-deployment-squad` — CI gates + rollout

**Acceptance criteria:**
- **AC1 — CI greps:** Add to existing `checkout-ci.yml`:
  - WI-HIST-1 AC1: grep for `userId == sub` filter in `OrderListEndpoint.cs`
  - WI-HIST-1 AC8: grep for rate-limit attribute on order list route
  - WI-HIST-5 AC5: grep that ensures `_logger.LogInformation` calls in order list path do NOT include `order` payload object
- **AC2 — Rollout:** No canary required (read-only, behind existing `feature.checkout` flag). Direct merge to main after gates pass.
- **AC3 — Rollback:** Feature flag off = full rollback. Document in runbook delta.
- **AC4 — SLO alarms:** Add P95 latency alarm at 200ms (page) and 500ms (cold partition). Page on 5xx rate > 0.5% over 10min.

**Bundle target:** `squads/review-deployment/artifacts/checkout/wi-hist-7-ci-gates/`

---

## Dependency graph

```
WI-HIST-1 (app-dev API) ─┬─> WI-HIST-2 (QA tests)
                         ├─> WI-HIST-4 (frontend, consumes contract)
                         ├─> WI-HIST-5 (security signoff)
                         └─> WI-HIST-7 (CI gates)

WI-HIST-3 (UX spec) ─────> WI-HIST-4 (frontend)

WI-HIST-6 (Cosmos index) ─> WI-HIST-1 (deploy gate — index must be built before query under load)
```

**Critical path:** WI-HIST-6 → WI-HIST-1 → (WI-HIST-2 ∥ WI-HIST-3) → WI-HIST-4 → WI-HIST-5 → WI-HIST-7

**Parallelizable from day 1:** WI-HIST-3 (UX spec) and WI-HIST-6 (Cosmos index) — neither depends on anything else.

---

## Out-of-scope tracked (for future verticals)

- **Refunds** — depends on this WI. Files separate spec post-ship.
- **Guest order history via magic link** — separate WI, post-confirmation-email vertical.
- **Search** — defer until 30-day usage shows the need.
- **Re-order** — depends on inventory + cart restore semantics, defer.
- **Export** — defer, low product priority.
- **Admin views** — separate vertical, separate auth scope (`orders:read:any` claim, not `sub`-scoped).

---

## Dispatch plan (next turn — NOT this turn)

Ideation-research-planning will issue 6 DMs:
1. → application-development-squad: WI-HIST-1, WI-HIST-4
2. → quality-testing-squad: WI-HIST-2
3. → experience-design-squad: WI-HIST-3 (owns spec; reviews WI-HIST-4)
4. → security-hardening-squad: WI-HIST-5
5. → azure-infrastructure-squad: WI-HIST-6
6. → review-deployment-squad: WI-HIST-7

This artifact is the spec all squads link from in PR descriptions (EMU substitute for issue link).

# Travel Assistant — Backlog State (v0.1 cut)

**Date:** 2026-06-23 22:25 (Blazor→React correction; XD-6d/e/f added)
**Repo:** tamirdresher/travel-assistant (default branch `main`)
**Authoring constraint:** GH issue-create is EMU-blocked for all squad accounts. Backlog is tracked here + via DMs; owner (tamirdresher personal) is the only account that can open issues/PRs. Squad branches CAN be pushed (verified via SEC-1b + APP-2 hub on origin).

---

## Status legend
- ✅ Shipped (branch landed or PR merged)
- 🟦 Ready (branch/patch exists, awaiting owner merge)
- 🟨 In flight (squad actively working)
- ⬜ Not started
- 🚫 Blocked
- 🔒 Owner-only (requires tamirdresher personal account)

## v0.1 cut line — 16 P0 items + accepted P1s

### Experience Design (XD)
| ID | Title | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| XD-1 | Voice/tone templates | ✅ | XD | Shipped |
| XD-2 | IA + split-canvas wireframes | ✅ | XD | `conversation-ux.md` + state coercion |
| XD-3 | WCAG 2.2 AA posture | ✅ | XD | Merge-blocking gate defined |
| XD-4 | A11y contract for QA | ✅ | XD | data-testid map shipped |
| XD-5 | shadcn component library | ✅ | XD | Reactivated — see XD-6d |
| XD-6 | Pick Blazor component lib | ⚠️ contingent | XD | MudBlazor pick valid IFF Blazor host scaffolded. Currently NOT applicable. ADR-0002 amendment at `docs/design/adr/0002-amendment-blazor-deferred.md` |
| XD-6a | Rewrite components.md → MudBlazor | ⛔ deferred | XD | Preserved at `docs/design/blazor/components.md`; reactivate only if Blazor host added |
| XD-6b | `_AppTheme.razor` MudTheme↔tokens.json | ⛔ deferred | XD+APP | Same reason as XD-6a |
| XD-6c | Per-state axe fixture matrix | ✅ contract | XD+QA | `tests/a11y/fixture-matrix.yaml` + `docs/design/fixtures/axe-fixture-contract.md` on `xd/design-baseline @ 9c78f77`. Runs unblock after XD-6f route lands |
| **XD-6d** | React/shadcn components v0.3 spec | ✅ | XD | `docs/design/react/components.md` on `xd/design-baseline @ 44386ec`. **Replaces XD-6a as APP-2 chat UI unblocker** |
| **XD-6e** | Tailwind 4 token bridge (tokens.json → tailwind.config) | ⬜ | XD+APP | Pair with app-dev; blocks any UI PR |
| **XD-6f** | Next.js axe fixture routes (`/_fixture/[component]`) | ⬜ | APP | Dev-only route gated `NODE_ENV==='development'`. Unblocks XD-6c CI gate (QA-3 per-PR axe) |

**Stack reality (correction):** `apps/web` is **Next.js 16.2.6 / React 19.2.4 / Tailwind 4** on `feat/app-web-routes-testids @ 10a044e`. No Blazor host exists in repo. ADR-0001 (Blazor hybrid) and ADR-0002 (MudBlazor) are **contingent / deferred** — applicable only if a Blazor host is scaffolded as a separate APP-* issue (none filed; not in v0.1).

**Modal-deferral rule** (`turn.start` → `turn.end` window): framework-agnostic, still merge-blocking. APP-2 SignalR hub guard (`ChatHub` → `IGroundingGate`) must enforce. QA test `no-modal-mid-stream.spec.ts` on `qa/eval-a11y-harness @ 7145ded` will be ported to React fixture harness via XD-6f.

### Application Development (APP)
| ID | Title | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| APP-1 | Aspire AppHost scaffold | 🟨 | APP | EF Core/Cosmos pkgs held until this merges |
| APP-2 | Chat UI + SignalR hub (7-event vocab) | 🟦 hub ready | APP | Hub rebased clean on `feat/app-2-hub` @ `1d7e09e` (pushed to origin from `main` parent `fd15417`). All 4 QA repro tests green. Chat UI (React) now blocked on XD-6d (spec ready ✅) consumer wire-up + XD-6e Tailwind token bridge + APP-1 |
| APP-3 | Semantic Kernel orchestration | ⬜ | APP | |
| APP-4 | Itinerary patch model | ⬜ | APP | |
| APP-5 | `IChatThreadStore`/`IItineraryStore` + in-mem | ✅ | APP | branch `feat/app-5-7-adr` |
| APP-6 | PII storage layer | ⬜ | APP | **MUST** ship with SEC-6 cipher+converters+attrs same PR or prod won't boot |
| APP-7 | Provider abstraction (flight/hotel/place) | ✅ | APP | branch `feat/app-5-7-adr` |
| APP-8 | `/api/version` endpoint | ✅ | APP | Branch shipped, 2/2 new tests green; unblocks REL-4 + QA-1 smoke #2 |
| APP-9 | Name worker queue (blocks INF-4) | ✅ | APP | `travel-assistant-worker-jobs` SB Queue, alias `worker-bus`, KEDA 20 msg/replica min 0 max 10. Branch `feat/app-9-10-infra-contracts` @ `1189141`. Doc: `docs/architecture/queues.md` |
| APP-10 | Confirm OTel metric names (blocks INF-5) | ✅ | APP | Names locked as-is. Meter `TravelAssistant.Agent`. Constants in `src/TravelAssistant.Api/Telemetry/MetricNames.cs`. Doc: `docs/architecture/observability-metrics.md`. Same branch as APP-9 |
| APP-2-DEFECT-0 | Hub branch ref rebase + push to origin | ✅ | APP | Branch rebuilt from `main`, pushed. Tip `1d7e09e`, parent `fd15417` |
| APP-2-DEFECT-1 | `Program.cs` SignalR wiring + `MapHub<ChatHub>` | ✅ | APP | `AddSignalR` + DI for `ITurnRegistry`/`IGroundingTracker`/`IGroundingGate` + `MapHub<ChatHub>("/hubs/chat")`. 3 SignalR-client tests green |
| APP-2-DEFECT-2 | `CoerceAndTrackAsync` extracted to `IGroundingGate` using `IHubContext` | ✅ | APP | Decoupled from `Hub.Context.ConnectionId` — callable from any server-side streamer. Hub now owns only StartTurn/CancelTurn |
| APP-2-DEFECT-3 | `CancelTurn` TOCTOU race fixed atomically | ✅ | APP | New `TryCancelAndDrain` flips `Completed` + returns pending under per-turn lock. `TrackPendingPatch` rejects post-cancel. QA repro test passes |

### Azure Infrastructure (INF)
| ID | Title | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| INF-1 | azd + postgres + redis modules | 🟦 | INF | in bundle |
| INF-2 | AOAI module + region runbook | 🟦 | INF | primary eastus2, 1300 TPM gpt-4o |
| INF-3 | OIDC UAMI + federated creds | 🟦 | INF | in bundle |
| INF-4 | Container Apps + Service Bus + worker (KEDA) | 🟦 | INF | `servicebus.bicep` + `worker-app.bicep` shipped. SB Standard tier, queue `travel-assistant-worker-jobs`, DLQ on, dedup 10min, `disableLocalAuth: true`. KEDA `azure-servicebus` msgCount=20 min=0 max=10, workload-identity auth |
| INF-5 | Dashboard + 6 alerts (5xx, cost burn, llm token surge, chip cache, worker queue backlog) | 🟦 | INF | `alertIds` extended to 6. New: `llm.tokens.in+out>500k/5m`, chip-cache hit-rate<30%/15m, SB ActiveMessages>500/10m |
| INF-6 | Per-env params + tag enforcement | 🟦 | INF | in bundle |

### Security Hardening (SEC)
| ID | Title | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| SEC-1..5 | Initial hardening | 🟦 | SEC | PR #39 mergeable, owner review pending |
| SEC-1b | PII redactor impl + 20 golden tests | ✅ | SEC | Branch `security/sec-1b-pii-redactor` pushed. `PiiRedactor.cs` (8 categories, Luhn + mod-97), 25/25 tests green. Doc `docs/security/sec-1/pii-redactor.md` |
| SEC-2b | Prompt-injection corpus ≥20 payloads + `CorpusLoader` | 🟦 | SEC | Loader shipped. QA swap landed on `qa/eval-a11y-harness` @ `f400c89` using `#if HAS_SHARED_CORPUS_LOADER`. **DEFECT-SEC2B-1** (MEDIUM): 3× CA1062 in IsBenign/Benign/Adversarial — needs `ArgumentNullException.ThrowIfNull` |
| SEC-5b | security-scan CI (vulnerable + audit + Trivy) | 🟦 | SEC+REL | `supply-chain-scan.yml` shipped by REL. Allowlist MudBlazor 7.x when promoted |
| SEC-6 | APP-6 PII encryption (envelope+CMK+AES-256-GCM) | ⬜ | SEC | Spec v1.0 shipped; co-ship with APP-6 |
| SEC-7 | Auth posture sign-off | ✅ | SEC | AAD-only data plane confirmed + 3 deltas |
| SEC-9 | Redis listKeys deny policy | 🟦 | SEC | branch `security/sec-9-redis-listkeys-deny` |

### Quality & Testing (QA)
| ID | Title | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| QA-1 | Smoke project `tests/smoke/` | ✅ | QA | 3 asserts: /healthz, /api/version, /chat+SignalR |
| QA-2 | LLM eval goldens (6) | ✅ | QA | branch `qa/eval-a11y-harness` |
| QA-3 | Axe a11y baselines | ✅ | QA | `tests/a11y/baseline.json` |
| QA-4 | Playwright flow tests | ⬜ | QA | |
| QA-5 | CI test matrix | ⬜ | QA | |

### Review & Deployment (REL)
| ID | Title | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| REL-1 | Build CI | 🟦 | REL | patch in `squads/review-deployment/artifacts/rel-1-to-5/` |
| REL-2 | CODEOWNERS + branch protection | 🚫 | REL | **owner must verify write perms first** |
| REL-3 | Deploy-staging workflow | 🟦 | REL | depends INF-3 + INF-4 |
| REL-4 | Release-notes automation | 🟦 | REL | unblocked by APP-8 ✅ |
| REL-5 | PR template + triage | 🟦 | REL | |
| REL-6 | Workflow naming (`azd env get-value` chain) | 🟦 | REL | acked by INF |
| REL-7 | Runtime-MI role assignments | n/a | REL | Folded into INF bundle (runtime-mi-roles.bicep) — no REL patch needed |
| REL-8 | CI gates (pii-redactor, prompt-injection, supply-chain, realtime-hub) | 🟦 | REL | 4 self-skipping workflows in `artifacts/sec-1b-pii-gate/`, `artifacts/app-2-hub-ci/`, etc. |
| REL-9 | Merge-readiness final verdict + commands | 🟦 | REL | `artifacts/merge-readiness-final/VERDICT.md` — ordered `git merge --ff-only` plan respecting APP-9/10→APP-2 dep |

---

## Open decisions
- ⚠️ ADR-0001: Blazor Server+WASM hybrid — **contingent / deferred** (no Blazor host scaffolded; `apps/web` is Next.js). Amendment at `docs/design/adr/0002-amendment-blazor-deferred.md`
- ⚠️ ADR-0002: MudBlazor — **contingent / deferred** (same reason)
- ✅ De facto: Next.js 16 / React 19 / Tailwind 4 / shadcn for v0.1 web app (XD-6d)
- ⬜ dev vs staging single lower env (REL wired staging; planning said dev) — owner call
- ⬜ ACR naming — INF to confirm

## Owner-only blockers (🔒)
1. Verify tamirdresher personal account has write to repo before REL-2 lands
2. OIDC repo secrets: AZURE_CLIENT_ID / TENANT_ID / SUBSCRIPTION_ID (federated)
3. Prod env reviewer setup
4. Merge order: SEC PR #39 → INF bundle → SEC-9 cleanup-redundant-keyvault
5. AOAI quota ticket (1300 TPM gpt-4o in eastus2) — INF filing today
6. `git am` all squad patches (EMU blocks squad pushes)

## Backlog totals
- 51 items tracked (was 49 → +XD-6d ✅, +XD-6e ⬜, +XD-6f ⬜; XD-6/6a/6b reclassified contingent/deferred, XD-5 reactivated)
- v0.1 cut line: 16 P0 + accepted P1s
- Shipped/ready: XD-1..4, XD-5/6d, XD-6c contract, APP-2 hub (4 defects fixed), APP-5/7/8/9/10, INF bundle (+SB+worker+6 alerts), SEC-1..5 (PR #39), SEC-1b, SEC-2b loader+QA swap, SEC-7, SEC-9, QA-1/2/3, REL-1..9
- Not started P0: APP-1..4, APP-6, REL-2 (blocked), QA-4/5, XD-6e/6f
- Deferred (not v0.1): XD-6/6a/6b (Blazor — requires host scaffold issue, not filed)
- Hub defects: **all 4 closed** ✅

## Critical-path remaining for v0.1
1. **Owner merge train** — per `squads/review-deployment/artifacts/merge-readiness-final/VERDICT.md`:
   SEC PR #39 → INF bundle → SEC-9 cleanup → APP-9/10 (`feat/app-9-10-infra-contracts`) → APP-2 (`feat/app-2-hub`) → SEC-1b (`security/sec-1b-pii-redactor`) → QA SEC-2b swap → XD `xd/design-baseline @ 44386ec` → install 8 CI workflows
2. **APP-1** AppHost scaffold (unblocks APP-3/4 + DB packages)
3. **XD-6e** Tailwind 4 token bridge (replaces XD-6b; unblocks any UI PR on `apps/web`)
4. **XD-6f** Next.js `/_fixture/[component]` route (replaces XD-6c MudBlazor harness; unblocks QA-3 per-PR axe gate)
5. **APP-2 chat UI** consume XD-6d React/shadcn spec in `apps/web/app/chat`
6. **APP-6 + SEC-6** co-shipped PII encryption
7. **REL-2** branch protection — pending owner perm verification
8. **SEC follow-up** — CA1062 null guards in `CorpusLoader.IsBenign/Benign/Adversarial` resolved on `security/sec-2b-prompt-injection-corpus @ ba22345` (QA can drop NoWarn on next touch)

---

*This file is the planning squad's authoritative backlog snapshot. Updated by ideation-research-planning-squad. DMs to other squads are the source of truth for individual item handoffs.*


---

## Update 2026-06-23 21:31 — Stack reversal: Blazor is back ON

**Trigger:** app-dev shipped src/TravelAssistant.Web Blazor host on `feat/app-web-blazor-scaffold-v2 @ 8de60b8` (MudBlazor 7.15.0, 4 providers wired, `AppTheme.Theme` placeholder). XD then shipped XD-6b drop-in on `xd/design-baseline @ 4b6d36b`. Security relayed the full status table. Stack pivot from prior turn (Path 1 / Next.js) is REVERSED — Path 2 / Blazor MudBlazor is canonical for v0.1.

**Status flips:**

| Item | Prior | New |
|---|---|---|
| ADR-0001 (Blazor hybrid) | ⚠️ Contingent | ✅ Accepted (host scaffolded) |
| ADR-0002 (MudBlazor) | ⚠️ Contingent | ✅ Accepted |
| XD-6a (`components.md` MudBlazor v0.2) | ⛔ Deferred | ✅ Active — APP-2 chat UI consumer |
| **XD-6b** (MudTheme↔tokens bridge) | ⛔ Deferred | ✅ **Shipped** — `xd/design-baseline @ 4b6d36b` |
| XD-6d (React/shadcn components) | ✅ Shipped | ⚠️ Parked (preserved, not deleted) |
| XD-6e (Tailwind 4 token bridge) | ⬜ Open | ⚠️ Parked |
| XD-6f (Next.js axe fixture routes) | ⬜ Open | ⛔ Discarded — route shape reverts to Blazor |
| XD-6c (axe fixture matrix YAML) | ⬜ Active | ✅ Active — Blazor `/_fixture/{component}?state=...&seed=...` per original contract |
| QA `no-modal-mid-stream.spec.ts` | Needed React port | ✅ No port — MudDialog target stands |
| `apps/web` Next.js scaffold | Path 1 host | ⚠️ Parked — not deleted; only reactivates if product re-scopes |

**Backlog count:** still 51 items. No net add/remove — only status reclassification + one ship (XD-6b).

**Critical path (revised):**
1. APP-1 AppHost wire-up — unchanged
2. XD-6b install (1 file copy into `src/TravelAssistant.Web/Theme/AppTheme.cs`) — app-dev owns the landing
3. XD-6a `components.md` MudBlazor v0.2 — already exists at `docs/design/blazor/components.md` from earlier shipped work; APP-2 chat UI components consume when they land in `src/TravelAssistant.Web/Components/`
4. New gap: APP-* item needed for Blazor `/_fixture/{component}` Razor pages (XD-6c CI gate consumer) — file as **APP-11** if not picked up by app-dev in next batch
5. APP-6 + SEC-6 co-ship (PII encryption) — unchanged
6. REL-2 owner perm verify — unchanged

**Framework-agnostic contracts (still merge-blocking, unchanged by flip):**
- Hub 7-event vocab (`ChatHubMethods` constants on `feat/app-2-hub @ 1d7e09e`)
- Modal Deferral state machine (server-side `IGroundingGate` is authoritative)
- a11y gate WCAG 2.2 AA
- `docs/design/tokens.json` schema
- Voice/terminology

**New backlog candidates (P1, post-flip):**
- **APP-11**: Blazor `/_fixture/{component}` Razor pages per `docs/design/fixtures/axe-fixture-contract.md` — gated `IsDevelopment()`, `data-fixture-ready="true"` body attr, in-process fake fixture data, no real PII/geo/prices. Unblocks XD-6c CI gate.
- **CLEANUP-1**: ADR-0002 needs second amendment file `docs/design/adr/0002-amendment-blazor-reactivated.md` documenting the flip-back. XD owns. Don't delete the first amendment — history matters.

**Notifications sent this turn:**
- app-dev: XD-6b drop-in install path + XD-6e/6f stand-down
- QA: no React port, fixture route reverts to Blazor, CA1062 NoWarn drop unchanged

---

## Update 2026-06-23 21:41 — APP-11 shipped; XD-6c gate unblocked

**Trigger:** app-dev shipped APP-11 Blazor `/_fixture/{component}` Razor pages on `feat/app-11-fixture-pages @ 80ee175` (pushed to origin). Branch rebuilt from `origin/main` + cherry-picked scaffold commit `8de60b8` (which was orphaned on `qa/eval-a11y-harness`, NOT on `feat/app-web-blazor-scaffold-v2` despite its name). Single canonical branch now carries scaffold + fixture pages + XD-6b drop-in install.

**Status flips:**

| Item | Prior | New |
|---|---|---|
| APP-11 (Blazor fixture pages) | ⬜ Candidate | ✅ **Shipped** — `feat/app-11-fixture-pages @ 80ee175` |
| XD-6b install (AppTheme.cs landing) | ⬜ App-dev owns | ✅ **Done** — overwritten with drop-in (Typography omitted, MudBlazor 7.15 compat; XD has 1-line follow-up) |
| XD-6c axe CI gate | ⬜ Waiting on APP-11 | 🟦 **Ready to flip** — QA's `qa/eval-a11y-harness` runner flips skip-safe → assert when `feat/app-11-fixture-pages` lands on main |
| `feat/app-web-blazor-scaffold-v2 @ b03c7f4` | Listed as scaffold home | ⛔ **Retire** — origin tip contains only QA test files, NOT the scaffold |

**Open coord items (routed to QA + XD by app-dev, not me):**
- Fixture pages use `EmptyLayout` (no MudTheme) to keep MudBlazor chrome out of axe scope. Trade-off: XD-1 dark palette inactive during fixture renders. If XD-3 pending-patch 4.5:1 contrast assertion needs the palette, swap to `MainLayout`. QA + XD to decide.

**Backlog count:** still 51 items. No net add/remove — status reclassification only.

**Critical path (current):**
1. ~~APP-11~~ ✅ shipped
2. XD-6c CI gate flip (on owner merge of `feat/app-11-fixture-pages`)
3. APP-6 + SEC-6 PII co-ship
4. REL-2 owner perm verify

**Owner merge train (revised order):**
- `feat/app-11-fixture-pages` (carries scaffold + APP-11 + XD-6b install)
- `feat/app-2-hub @ 1d7e09e` (hub + 4 defects fixed)
- `security/sec-1b-pii-redactor` (PII redactor + 25 tests)
- `security/sec-2b-prompt-injection-corpus @ ba22345` (CA1062 fixed)
- INF bundle PR (azd + SB + worker-app + alerts)
- APP-1 AppHost (once landed)

**CLEANUP-2:** delete `feat/app-web-blazor-scaffold-v2` from origin once owner confirms `feat/app-11-fixture-pages` is on main. Branch name was misleading — actual scaffold was orphaned elsewhere.


---

## 2026-06-23 21:50 — ProductionGuard defects + APP-12 project skeleton

QA reviewed docs/security/app-6/productionguard-checks.md @ 26dbb45 and found 3 defects in the copy-paste-ready C# before app-dev wires it into `Program.cs`. App-dev confirmed APP-2 fully fixed on `feat/app-2-hub @ 1d7e09e` (origin tip verified via gh api — QA was reviewing stale orphan `1cdbbaf`).

**New backlog items (4):**

| ID | P | Owner | Title | Acceptance |
|---|---|---|---|---|
| **APP-12** | P0 | app-dev | Create `src/TravelAssistant.ProductionGuard` + `tests/TravelAssistant.ProductionGuard.Tests` csproj skeleton | Two empty csprojs added to solution, `IGuardCheck` + `GuardCheckResult` interface stubs, wire-in commented placeholder in `Program.cs`. Unblocks SEC patch-in and QA test matrix. |
| **SEC-6a-DEFECT-1** | P0 | security | Fix Check 3 (`SensitivePropertiesEncryptedCheck`) — dead code | Ctor takes `Type[] contextTypes` (resolved at wire-up site, not via `GetServices<DbContext>()`). `Run` opens `services.CreateScope()` and resolves each context type — DbContext is scoped, root resolve throws under `ValidateScopes=true`. Reflection scan iterates `Model.GetEntityTypes()` per context. Doc updated + unit test demonstrating positive failure (entity with `[DataClass(Sensitive)]` minus converter ⇒ Fail). |
| **SEC-6a-DEFECT-2** | P1 | security | Fix Check 2 (`CmkNameResolvesCheck`) — no timeout on KV GET | Ctor takes `TimeSpan budget` (default 10s). `Run` uses `CancellationTokenSource(budget)` + `GetKeyAsync(name, version: null, ct)`. `OperationCanceledException` ⇒ `Fail("KV unreachable within {budget}")`. Doc perf budget revised: cold-MSI realistically 1–2s, not 50–200ms. |
| **SEC-6a-DEFECT-3** | P1 | security | Clarify `GuardCheckResult.Warn(...)` contract | Declare `Warn` factory in doc preamble alongside `Pass`/`Fail` if intended. Confirm deployment-gate workflow (REL-owned) treats `Warn` as `pass + surface`. If `Warn` is not real, Check 2's CMK-expiring-in-7d case downgrades to `Pass` with structured ILogger warning OR upgrades to `Fail` — security decides. |
| **SEC-6a-NIT-1** | P2 | security | Mockability — extract `IKeyClient` or `Func<Uri,KeyClient>` in Check 2 | Inline `new KeyClient(uri, cred)` is untestable. Constructor injection of factory delegate lets QA unit-test 403/404/disabled/expiring/timeout paths without a live KV. |

**Status flips (existing items):**

| Item | Prior | New |
|---|---|---|
| APP-2 (chat hub) | 🟡 plumbing shipped, 4 defects open | ✅ **Shipped** — `feat/app-2-hub @ 1d7e09e` (DEFECT-0/1/2/3 all closed, 4/4 SignalR + registry tests green, `ChatHubLifecycleTests.cs` landed verbatim) |
| QA-4b (hub lifecycle tests) | ⬜ Awaiting hub fix | ✅ **Shipped** — verbatim copy on `feat/app-2-hub` per app-dev confirmation, 4/4 green |
| SEC-6 (APP-6 PII encryption + ProductionGuard) | ⬜ Spec only | 🟡 **In progress** — docs landed (`security/app-6-productionguard-checks @ 26dbb45`), 3 defects + 1 nit open (SEC-6a-DEFECT-1/2/3 + SEC-6a-NIT-1), awaits APP-12 skeleton |

**Backlog count:** 51 + 4 new = **55 items**.

**Critical path (revised):**
1. ~~APP-2~~ ✅ + ~~APP-11~~ ✅ shipped
2. APP-12 csproj skeleton (unblocks SEC patch-in + QA 21-test matrix)
3. SEC-6a-DEFECT-1/2/3 patches into ProductionGuard doc + impl drop-in
4. QA 21-test matrix on `qa/app-6-productionguard-tests` (7 scenarios × 3 checks)
5. XD-6c CI gate flip (on owner merge of `feat/app-11-fixture-pages`)
6. APP-6 + SEC-6 PII co-ship (cipher + converters + attributes + 3 ProductionGuard checks land same PR — prod boots only if all green)
7. REL-2 owner perm verify

**Owner merge train (unchanged from prior section):** `feat/app-11-fixture-pages` → `feat/app-2-hub @ 1d7e09e` → `security/sec-1b-pii-redactor` → `security/sec-2b-prompt-injection-corpus @ ba22345` → INF bundle → APP-1.

**Authoritative references:**
- Defect repro: `squads/quality-testing/artifacts/qa-app6-productionguard-review.md`
- ProductionGuard spec: `docs/security/app-6/productionguard-checks.md @ 26dbb45`
- APP-6 PII encryption spec v1.0: `squads/security-hardening/artifacts/app-6-pii-encryption-spec.md`
- APP-2 origin tip verification: `gh api repos/tamirdresher/travel-assistant/branches/feat/app-2-hub --jq '.commit.sha'` ⇒ `1d7e09e884c7baa1a903bb6ec4d4d24705fe9143`

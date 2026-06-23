# QA findings: APP-2 hub branch (feat/app-2-hub @ 1cdbbaf claimed)

**Reviewer:** quality-testing-squad
**Date:** 2026-06-23
**Subject branch:** `feat/app-2-hub` (claim) → actual commit `1cdbbaf` is an **orphan** locally

## Defect summary

| ID | Severity | Layer | Status |
|----|----------|-------|--------|
| DEFECT-0 | HIGH (coord) | branch metadata | Branch ref doesn't point at the hub commit |
| DEFECT-1 | CRITICAL | wire | Hub not registered/mapped in `Program.cs` |
| DEFECT-2 | HIGH | design | `CoerceAndTrack` unreachable from server pipeline |
| DEFECT-3 | MEDIUM | race | `TryCancel` + `SnapshotPending` not atomic |

---

## DEFECT-0 — `feat/app-2-hub` does not contain the hub

```
$ git log --oneline feat/app-2-hub -3
fd15417 fix(api): LOGIN-002 RFC 7239 Forwarded client-IP resolver + gate
357b449 feat(api): POST /api/auth/login handler v1 (LOGIN-001)
890676e Free-tier demo: Next.js Travel Assistant on Vercel (#27)

$ git log --all --source | grep 1cdbbaf
1cdbbaf  refs/stash@{...}  APP-2: SignalR ChatHub with XD-locked event vocab
```

The hub commit is reachable only via stash/orphan refs. Remote
`origin/feat/app-2-hub` does not exist at all. App-dev's promise that
"branch on remote" is the artifact of record fails here. **Fix:**
re-create `feat/app-2-hub` pointing at `1cdbbaf` (or rebase to a clean
parent), then push.

## DEFECT-1 (CRITICAL) — hub not wired in `Program.cs`

Commit message says "Wired on /hubs/chat". Actual `src/TravelAssistant.Api/Program.cs`
on commit `1cdbbaf` contains **zero** of:

- `builder.Services.AddSignalR()`
- `builder.Services.AddSingleton<ITurnRegistry, TurnRegistry>()`
- `builder.Services.AddSingleton<IGroundingTracker, GroundingTracker>()`
- `app.MapHub<ChatHub>("/hubs/chat")`

Consequence: `/hubs/chat` returns 404 at runtime. The 19/19 unit tests
pass because they test `TurnRegistry` / `GroundingTracker` / constant
strings in isolation — none of them go through the wire. Downstream
breakage radius:

- `tests/smoke/tests/post-deploy.smoke.spec.ts` SignalR negotiate
  assertion will fail in REL-3 staging
- QA-4b `CorpusEchoTests` live mode (`LLM_EVAL_LIVE=1`) cannot connect
- XD's `?fixture=streaming` chat-page test (XD-6c) blocked
- The 3 client-side tests in `ChatHubLifecycleTests.cs` (this PR)
  will fail at `hub.StartAsync()`

**Fix (3 lines + 2 DI registrations):**

```csharp
builder.Services.AddSignalR();
builder.Services.AddSingleton<ITurnRegistry, TurnRegistry>();
builder.Services.AddSingleton<IGroundingTracker, GroundingTracker>();
// ...
app.MapHub<ChatHub>("/hubs/chat");
```

`TurnRegistry` and `GroundingTracker` are currently `internal` — they
need either `public` visibility or an `InternalsVisibleTo` already
covers (verified: csproj has `InternalsVisibleTo TravelAssistant.Api.Tests`,
not the runtime). For DI, `internal` works as long as the registration
references the type from inside the same assembly (which `Program.cs` is).
So the above wiring compiles as-is; no visibility change needed for DI.

## DEFECT-2 (HIGH) — `CoerceAndTrack` is architecturally unreachable

```csharp
internal ItineraryPatch CoerceAndTrack(string turnId, ItineraryPatch incoming)
{
    var grounded = _grounding.HasGrounding(Context.ConnectionId, turnId);
    ...
}
```

Two problems:

1. **`internal` visibility** — fine for tests, but the comment claims
   "invoked by the turn pipeline (LLM streamer)". A background LLM
   streamer lives in a hosted service, not inside the Hub instance.
2. **`Context.ConnectionId` dependency** — Hub instances are created
   by SignalR per inbound call. A streamer producing patches has no
   Hub instance with a valid Context.

The grounding-coercion contract (XD hard rule, G-006 golden) is
therefore not enforceable at runtime from the producer side. The
client could send patches that bypass coercion entirely. Only the
test in `ChatHubTests.cs` proves the algorithm — there is no
production code path that calls it.

**Fix sketch:**

```csharp
// New service injected into the streamer
public interface IGroundingGate
{
    ItineraryPatch CoerceAndTrack(string connectionId, string turnId, ItineraryPatch incoming);
}

// Streamer:
//   var coerced = _gate.CoerceAndTrack(connId, turnId, incoming);
//   await _hubContext.Clients.Client(connId).SendAsync(ChatHubMethods.ItineraryPatch, coerced);
```

The Hub keeps a thin wrapper that delegates to `_gate` so client-initiated
patches (if any) still get coerced.

## DEFECT-3 (MEDIUM) — TryCancel/SnapshotPending race

```csharp
public CancelAck CancelTurn(string turnId)
{
    var cancelled = _turns.TryCancel(...);   // (A)
    if (cancelled)
    {
        var pending = _turns.SnapshotPending(...);  // (B)
        ...
    }
}
```

Between (A) and (B), the streamer (whose cancellation token has been
signalled but may not have polled yet) can call `TrackPendingPatch`.
That patch:

- Either lands in the snapshot at (B) — rolled back, fine
- Or lands AFTER (B) — escapes the rollback, client never sees the
  inverse op, itinerary remains in a Pending state forever

`TurnRegistry.TrackPendingPatch` does not check cancellation state —
it appends unconditionally. Reproduction test:
`ChatHubLifecycleTests.DEFECT3_TryCancel_SnapshotPending_race_leaks_pending_patch`.

**Fix:** `TryCancel` should atomically (a) flip `Completed = true`, (b)
return the snapshot, (c) freeze further `TrackPendingPatch` calls for
this turn. Suggested API:

```csharp
bool TryCancelAndDrain(string connectionId, string turnId, out IReadOnlyList<PatchOp> rollback);

// In TrackPendingPatch: add `if (state.Completed) return;` under the lock.
```

---

## Artifact

`ChatHubLifecycleTests.cs` — drop into `tests/TravelAssistant.Api.Tests/Realtime/`
on the branch that contains the hub source. Csproj needs:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="9.0.0" />
```

3 of 4 tests fail today (DEFECT-1). The 4th fails on DEFECT-3.
All 4 pass once the fixes above land.

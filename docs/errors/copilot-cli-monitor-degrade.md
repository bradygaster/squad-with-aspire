# Copilot CLI Missing — Monitor Degrade-to-Polling Addendum

**Owner:** experience-design-squad
**Scope:** `monitor-email`, `monitor-teams` long-running poll loops in `bradygaster/squad` packages/squad-cli.
**Parent spec:** [`copilot-cli-voice-matrix.md`](./copilot-cli-voice-matrix.md) §4c
**Status:** Locked. Bind to follow-up PR after #1372.

## Problem this closes

The voice matrix §4c gives the warn-skip string for a single round. Monitor
commands poll on a schedule (default 60s). If Copilot CLI is missing for an
hour, the naive implementation emits the same 4-line warn 60 times — log
spam that drowns real signal and trains operators to ignore the warning.

App-dev's #1372 patchset routes monitor sites through
`copilotCliMissingMessage(detection, 'monitor-skip')` but does not address
**emission frequency**. This addendum fills that gap.

## Contract

### Lifecycle states

A monitor's Copilot-CLI-availability state machine has three states:

| State | Meaning | Next-round behavior |
|---|---|---|
| `ok` | last probe succeeded | poll normally |
| `degraded-fresh` | first failed probe in a run, OR first failure after recovery | emit full warn-skip block |
| `degraded-suppressed` | nth consecutive failure (n ≥ 2) | emit single-line heartbeat only |

Transitions:

```
ok ──fail──> degraded-fresh ──fail──> degraded-suppressed
 ^                                          │
 └──────────────── pass ────────────────────┘
```

Any successful probe returns the state to `ok`. The next failure after that
re-enters `degraded-fresh` (operator gets the full message again — they may
have missed it).

### Emission rules

1. **`degraded-fresh`** → emit full 4-line block (voice-matrix §4c verbatim).
2. **`degraded-suppressed`** → emit one heartbeat line per round, no URL, no
   remediation. Format below.
3. **Recovery** → emit one-line recovery notice the round the probe passes.
4. **Recovery from suppressed** → still emit recovery notice (operator should
   know polling resumed).

### Canonical strings (verbatim — drop in)

#### `degraded-fresh` (round 1 of outage, and round 1 after recovery)

Identical to voice-matrix §4c. No change. Restated here so this doc is
self-contained:

```
[monitor-email] skipping round: Copilot CLI not on PATH.
  Install:  https://github.com/github/copilot
  Verify:   squad doctor
```

(`[monitor-teams]` for the Teams variant — only the bracketed prefix swaps.)

#### `degraded-suppressed` (rounds 2..N of an outage)

```
[monitor-email] still skipping: Copilot CLI not on PATH (round {N}).
```

- `{N}` is 1-indexed consecutive-failure count. Round 2 of the outage prints
  `round 2`, round 60 prints `round 60`.
- Single line. No URL. No remediation. No `squad doctor` hint.
- Rationale: operator has already seen the full message in round 1; this is
  a liveness signal so they know the monitor is still alive and still
  blocked, not crashed.

#### Recovery notice

```
[monitor-email] Copilot CLI available — resuming polling.
```

- Emitted exactly once on the `degraded-*` → `ok` transition.
- No prefix change (`[monitor-email]` not `[monitor-email] info:` — keep the
  surface consistent with skip lines so grep `^\[monitor-email\]` catches
  everything).

## Wiring notes for application-development-squad

- State lives on the monitor command's loop closure, not global. Restart
  resets to `ok` (correct — restart should re-announce).
- Counter increments **before** emission, so the first suppressed line reads
  `round 2`, not `round 1`. Round 1 used the full block.
- No timer-based suppression. Round-count is the only suppression key —
  simpler, deterministic, testable without fake timers.
- `--verbose` flag (if present on the command) MAY override suppression and
  emit the full block every round. Default behavior is suppression.

## Telemetry

Two events. Both keyed on `monitor` ∈ {`email`, `teams`}.

- `monitor.copilot_cli.degraded` — fired on every `ok → degraded-fresh`
  transition. Payload: `{ monitor, probeReason }`. NOT fired on
  `degraded-fresh → degraded-suppressed` (same outage, same event).
- `monitor.copilot_cli.recovered` — fired on every `degraded-* → ok`
  transition. Payload: `{ monitor, outageRounds }`.

Alerting: page if `degraded` fires without a matching `recovered` within 10
rounds. The `outageRounds` field on `recovered` is the SLI for monitor
availability.

## Test cases for quality-testing-squad

Minimum fixture set:

| # | Scenario | Expected output (rounds 1..4) |
|---|---|---|
| 1 | always-ok | (no skip output across 4 rounds) |
| 2 | always-degraded | full block, suppressed `round 2`, suppressed `round 3`, suppressed `round 4` |
| 3 | recover-mid | full block, suppressed `round 2`, recovery line, (silence) |
| 4 | flap (ok→fail→ok→fail) | (silence), full block, recovery line, full block |
| 5 | --verbose + always-degraded | full block × 4 |

Test #4 verifies that the second outage gets a fresh full block (not a
suppressed line) — operators must not lose the remediation hint on the
second outage of a session.

## Out of scope

- Backoff / poll-interval changes during outage — separate concern.
- Persisting outage state across monitor restarts — restart resets.
- Localization — namespace reserved at `squad.monitors.copilotCli.*`
  per `copilot-cli-i18n-keys.md`. Current strings are en-US default.
- Email/Teams notification of the outage to humans — orthogonal feature.

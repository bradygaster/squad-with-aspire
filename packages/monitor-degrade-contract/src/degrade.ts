// Pure 3-state degrade reducer for monitor-email / monitor-teams Copilot-CLI
// availability suppression, per experience-design-squad's spec at
// docs/errors/copilot-cli-monitor-degrade.md (commit ac9c34b).
//
// No timers, no I/O. The loop owns the state; this module owns the transitions
// and the emission decisions. Round count is the only suppression key.
//
// Contract invariants (asserted by tests/regression/monitor-degrade-state-machine.test.ts):
//   - ok → fail emits "fresh" (full block) and fires `degraded` telemetry once
//   - fresh → fail (consecutive) emits "suppressed" (heartbeat line) and fires NO telemetry
//   - suppressed → fail (consecutive) emits "suppressed" with incremented round
//   - any state → pass while previously ok: silence, no telemetry
//   - degraded-* → pass emits "recovered" (one line) and fires `recovered` telemetry with outageRounds
//   - flap (ok→fail→ok→fail): the second outage's first fail is "fresh" again, NOT suppressed
//   - verbose=true forces "fresh" emission on every fail (suppression bypassed)
//
// Emission counter is 1-indexed per spec §Wiring: "first suppressed line reads round 2".

export type MonitorKind = "email" | "teams";

export type DegradeState =
  | { phase: "ok" }
  | { phase: "degraded-fresh"; round: number }
  | { phase: "degraded-suppressed"; round: number };

export type ProbeResult = "pass" | "fail";

export type EmissionKind = "none" | "fresh" | "suppressed" | "recovered";

export type TelemetryEvent =
  | { event: "monitor.copilot_cli.degraded"; monitor: MonitorKind; probeReason: string }
  | { event: "monitor.copilot_cli.recovered"; monitor: MonitorKind; outageRounds: number };

export interface StepInput {
  state: DegradeState;
  probe: ProbeResult;
  monitor: MonitorKind;
  probeReason?: string;
  verbose?: boolean;
}

export interface StepOutput {
  state: DegradeState;
  emission: EmissionKind;
  round: number;
  telemetry: TelemetryEvent | null;
}

export const initialState: DegradeState = { phase: "ok" };

export function step(input: StepInput): StepOutput {
  const { state, probe, monitor, probeReason = "missing", verbose = false } = input;

  if (probe === "pass") {
    if (state.phase === "ok") {
      return { state: { phase: "ok" }, emission: "none", round: 0, telemetry: null };
    }
    const outageRounds = state.round;
    return {
      state: { phase: "ok" },
      emission: "recovered",
      round: 0,
      telemetry: { event: "monitor.copilot_cli.recovered", monitor, outageRounds },
    };
  }

  // probe === "fail"
  if (state.phase === "ok") {
    return {
      state: { phase: "degraded-fresh", round: 1 },
      emission: "fresh",
      round: 1,
      telemetry: { event: "monitor.copilot_cli.degraded", monitor, probeReason },
    };
  }

  const nextRound = state.round + 1;
  if (verbose) {
    return {
      state: { phase: "degraded-fresh", round: nextRound },
      emission: "fresh",
      round: nextRound,
      telemetry: null, // already fired on ok→fresh; spec §Telemetry: NOT fired fresh→suppressed
    };
  }

  return {
    state: { phase: "degraded-suppressed", round: nextRound },
    emission: "suppressed",
    round: nextRound,
    telemetry: null,
  };
}

export function freshMessage(monitor: MonitorKind): string {
  const prefix = `[monitor-${monitor}]`;
  return [
    `${prefix} skipping round: Copilot CLI not on PATH.`,
    `  Install:  https://github.com/github/copilot`,
    `  Verify:   squad doctor`,
  ].join("\n");
}

export function suppressedMessage(monitor: MonitorKind, round: number): string {
  return `[monitor-${monitor}] still skipping: Copilot CLI not on PATH (round ${round}).`;
}

export function recoveredMessage(monitor: MonitorKind): string {
  return `[monitor-${monitor}] Copilot CLI available — resuming polling.`;
}

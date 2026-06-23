// Regression tests for the monitor degrade-to-polling state machine, per
// experience-design-squad's spec at docs/errors/copilot-cli-monitor-degrade.md
// §"Test cases for quality-testing-squad".
//
// Five fixture scenarios from the spec are reproduced verbatim, plus reducer
// purity, telemetry firing rules, and the canonical-message contract.

import { describe, expect, it } from "vitest";
import {
  type DegradeState,
  type EmissionKind,
  type MonitorKind,
  type ProbeResult,
  freshMessage,
  initialState,
  recoveredMessage,
  step,
  suppressedMessage,
} from "../../packages/monitor-degrade-contract/src/degrade";

interface RoundTrace {
  emission: EmissionKind;
  round: number;
  telemetry: string | null; // event name only, for compact assertions
}

function runScenario(
  probes: ProbeResult[],
  opts: { monitor?: MonitorKind; verbose?: boolean } = {},
): RoundTrace[] {
  const monitor = opts.monitor ?? "email";
  let state: DegradeState = initialState;
  const trace: RoundTrace[] = [];
  for (const probe of probes) {
    const out = step({ state, probe, monitor, verbose: opts.verbose });
    trace.push({
      emission: out.emission,
      round: out.round,
      telemetry: out.telemetry?.event ?? null,
    });
    state = out.state;
  }
  return trace;
}

describe("monitor degrade-to-polling — spec §Test cases", () => {
  it("Scenario 1: always-ok — no skip output across 4 rounds", () => {
    const trace = runScenario(["pass", "pass", "pass", "pass"]);
    expect(trace.map((t) => t.emission)).toEqual(["none", "none", "none", "none"]);
    expect(trace.every((t) => t.telemetry === null)).toBe(true);
  });

  it("Scenario 2: always-degraded — fresh, then suppressed rounds 2..4", () => {
    const trace = runScenario(["fail", "fail", "fail", "fail"]);
    expect(trace.map((t) => t.emission)).toEqual([
      "fresh",
      "suppressed",
      "suppressed",
      "suppressed",
    ]);
    expect(trace.map((t) => t.round)).toEqual([1, 2, 3, 4]);
    // Telemetry: degraded fires ONCE on ok→fresh, NOT on fresh→suppressed.
    expect(trace.map((t) => t.telemetry)).toEqual([
      "monitor.copilot_cli.degraded",
      null,
      null,
      null,
    ]);
  });

  it("Scenario 3: recover-mid — fresh, suppressed, recovery, silence", () => {
    const trace = runScenario(["fail", "fail", "pass", "pass"]);
    expect(trace.map((t) => t.emission)).toEqual([
      "fresh",
      "suppressed",
      "recovered",
      "none",
    ]);
    expect(trace.map((t) => t.telemetry)).toEqual([
      "monitor.copilot_cli.degraded",
      null,
      "monitor.copilot_cli.recovered",
      null,
    ]);
  });

  it("Scenario 4 (CRITICAL flap test): second outage gets fresh full block, not suppressed", () => {
    const trace = runScenario(["pass", "fail", "pass", "fail"]);
    expect(trace.map((t) => t.emission)).toEqual([
      "none",
      "fresh",
      "recovered",
      "fresh", // ← the invariant: round 4's fail re-enters degraded-fresh
    ]);
    expect(trace.map((t) => t.telemetry)).toEqual([
      null,
      "monitor.copilot_cli.degraded",
      "monitor.copilot_cli.recovered",
      "monitor.copilot_cli.degraded",
    ]);
  });

  it("Scenario 5: --verbose + always-degraded — fresh × 4", () => {
    const trace = runScenario(["fail", "fail", "fail", "fail"], { verbose: true });
    expect(trace.map((t) => t.emission)).toEqual(["fresh", "fresh", "fresh", "fresh"]);
    expect(trace.map((t) => t.round)).toEqual([1, 2, 3, 4]);
    // Telemetry still fires once — verbose changes emission, not events.
    expect(trace.map((t) => t.telemetry)).toEqual([
      "monitor.copilot_cli.degraded",
      null,
      null,
      null,
    ]);
  });
});

describe("monitor degrade reducer — purity & telemetry payloads", () => {
  it("reducer is pure: same input → same output, no shared state", () => {
    const a = step({ state: initialState, probe: "fail", monitor: "email" });
    const b = step({ state: initialState, probe: "fail", monitor: "email" });
    expect(a).toEqual(b);
    expect(a.state).not.toBe(b.state); // distinct object identity
  });

  it("recovered telemetry carries outageRounds matching the failure count", () => {
    let state: DegradeState = initialState;
    for (let i = 0; i < 7; i++) {
      state = step({ state, probe: "fail", monitor: "teams" }).state;
    }
    const recovery = step({ state, probe: "pass", monitor: "teams" });
    expect(recovery.telemetry).toEqual({
      event: "monitor.copilot_cli.recovered",
      monitor: "teams",
      outageRounds: 7,
    });
  });

  it("degraded telemetry carries probeReason from input", () => {
    const out = step({
      state: initialState,
      probe: "fail",
      monitor: "email",
      probeReason: "ENOENT",
    });
    expect(out.telemetry).toEqual({
      event: "monitor.copilot_cli.degraded",
      monitor: "email",
      probeReason: "ENOENT",
    });
  });

  it("pass-while-ok produces zero emission and zero telemetry", () => {
    const out = step({ state: initialState, probe: "pass", monitor: "email" });
    expect(out.emission).toBe("none");
    expect(out.telemetry).toBeNull();
    expect(out.state).toEqual({ phase: "ok" });
  });

  it("counter is 1-indexed: first suppressed line is round 2", () => {
    let state: DegradeState = initialState;
    state = step({ state, probe: "fail", monitor: "email" }).state; // fresh, round 1
    const second = step({ state, probe: "fail", monitor: "email" });
    expect(second.emission).toBe("suppressed");
    expect(second.round).toBe(2);
  });
});

describe("monitor degrade — canonical message strings (spec §Canonical strings verbatim)", () => {
  it("fresh message is the voice-matrix §4c block, verbatim, with monitor-email prefix", () => {
    expect(freshMessage("email")).toBe(
      [
        "[monitor-email] skipping round: Copilot CLI not on PATH.",
        "  Install:  https://github.com/github/copilot",
        "  Verify:   squad doctor",
      ].join("\n"),
    );
  });

  it("fresh message swaps only the bracketed prefix for teams variant", () => {
    expect(freshMessage("teams")).toContain("[monitor-teams] skipping round");
    expect(freshMessage("teams")).not.toContain("[monitor-email]");
  });

  it("suppressed message is single line, no URL, no remediation, round-suffixed", () => {
    const msg = suppressedMessage("email", 2);
    expect(msg).toBe("[monitor-email] still skipping: Copilot CLI not on PATH (round 2).");
    expect(msg).not.toMatch(/https?:\/\//);
    expect(msg).not.toMatch(/squad doctor/);
    expect(msg.split("\n")).toHaveLength(1);
  });

  it("recovered message keeps [monitor-*] prefix so grep ^\\[monitor- catches it", () => {
    expect(recoveredMessage("email")).toMatch(/^\[monitor-email\]/);
    expect(recoveredMessage("teams")).toMatch(/^\[monitor-teams\]/);
    expect(recoveredMessage("email")).toContain("resuming polling");
  });
});

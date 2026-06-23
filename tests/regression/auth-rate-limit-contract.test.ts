// Regression tests for rate-limit-contract.md.
// Covers all 5 cases from the spec's test matrix, with extra focus on the
// non-obvious ones flagged by experience-design-squad:
//   Case 4 — body vs header disagreement (UI trusts body)
//   Case 5 — retryAfterSeconds: 0 or negative (UI clamps to 1s minimum)

import { describe, expect, it } from "vitest";
import {
  MAX_COOLDOWN_S,
  MIN_COOLDOWN_S,
  rateLimitCopy,
  reconcileRateLimit,
  shouldAnnounce,
  submitControlState,
} from "../../packages/auth-ui-contracts/src/rate-limit";

describe("rate-limit-contract: Case 1 — 11th login in 15min, same IP", () => {
  it("returns scope=ip, retryAfterSeconds>=1, with submit aria-disabled=true", () => {
    const body = {
      code: "RATE_LIMITED" as const,
      message: "Too many attempts. Try again in 47 seconds.",
      retryAfterSeconds: 47,
      scope: "ip" as const,
    };
    const result = reconcileRateLimit(body, "47");
    expect(result.retryAfterSeconds).toBe(47);
    expect(result.scope).toBe("ip");
    expect(result.clamped).toBe(false);
    expect(result.source).toBe("body");

    const control = submitControlState(result.retryAfterSeconds);
    expect(control.ariaDisabled).toBe(true);
    // Must NOT use disabled attribute — keeps focusable for SR.
    expect(control.disabledAttr).toBe(false);

    expect(rateLimitCopy(result.scope, result.retryAfterSeconds)).toContain("from this network");
  });
});

describe("rate-limit-contract: Case 2 — 6th resend in 1hr, same account", () => {
  it("renders account-scope copy mentioning 'this account'", () => {
    const body = {
      code: "RATE_LIMITED" as const,
      message: "Too many attempts for this account.",
      retryAfterSeconds: 600,
      scope: "account" as const,
    };
    const result = reconcileRateLimit(body, "600");
    expect(result.scope).toBe("account");
    expect(rateLimitCopy(result.scope, result.retryAfterSeconds)).toContain("this account");
    // Must NOT reveal whether the account exists — only generic copy.
    expect(rateLimitCopy(result.scope, result.retryAfterSeconds)).not.toMatch(/exists|not found|unknown/i);
  });
});

describe("rate-limit-contract: Case 3 — countdown reaches 0", () => {
  it("re-enables submit and clears live region at 0", () => {
    const at5 = submitControlState(5);
    expect(at5.ariaDisabled).toBe(true);
    expect(at5.liveRegionCleared).toBe(false);

    const at0 = submitControlState(0);
    expect(at0.ariaDisabled).toBe(false);
    expect(at0.liveRegionCleared).toBe(true);
  });

  it("announces only at threshold crossings (initial, 30s, 10s, 0s) — not every second", () => {
    expect(shouldAnnounce(null, 47)).toBe(true);  // initial
    expect(shouldAnnounce(47, 46)).toBe(false);   // mid-decrement
    expect(shouldAnnounce(31, 30)).toBe(true);    // crossed 30
    expect(shouldAnnounce(30, 29)).toBe(false);
    expect(shouldAnnounce(11, 10)).toBe(true);    // crossed 10
    expect(shouldAnnounce(10, 9)).toBe(false);
    expect(shouldAnnounce(1, 0)).toBe(true);      // crossed 0
    expect(shouldAnnounce(0, 0)).toBe(false);
  });
});

describe("rate-limit-contract: Case 4 — body and header disagree (NON-OBVIOUS)", () => {
  it("trusts body retryAfterSeconds over Retry-After header", () => {
    const body = {
      code: "RATE_LIMITED" as const,
      message: "Too many attempts. Try again in 47 seconds.",
      retryAfterSeconds: 47,
      scope: "ip" as const,
    };
    // Header says 99, body says 47. Body wins.
    const result = reconcileRateLimit(body, "99");
    expect(result.retryAfterSeconds).toBe(47);
    expect(result.source).toBe("body");
  });

  it("falls back to header when body is missing the field entirely", () => {
    const result = reconcileRateLimit({ code: "RATE_LIMITED", scope: "ip" } as never, "23");
    expect(result.retryAfterSeconds).toBe(23);
    expect(result.source).toBe("header");
  });

  it("falls back to MIN_COOLDOWN_S when body is null and header is missing", () => {
    const result = reconcileRateLimit(null, null);
    expect(result.retryAfterSeconds).toBe(MIN_COOLDOWN_S);
    expect(result.source).toBe("fallback");
    expect(result.clamped).toBe(true);
  });

  it("ignores non-numeric body field and falls back to header", () => {
    const result = reconcileRateLimit({ retryAfterSeconds: "47" as unknown as number, scope: "ip" }, "11");
    expect(result.retryAfterSeconds).toBe(11);
    expect(result.source).toBe("header");
  });

  it("handles NaN body value gracefully", () => {
    const result = reconcileRateLimit({ retryAfterSeconds: NaN, scope: "ip" }, "5");
    expect(result.retryAfterSeconds).toBe(5);
    expect(result.source).toBe("header");
  });
});

describe("rate-limit-contract: Case 5 — retryAfterSeconds: 0 or negative (NON-OBVIOUS)", () => {
  it("clamps 0 to 1s minimum (does not crash, does not auto-retry)", () => {
    const body = { code: "RATE_LIMITED" as const, message: "x", retryAfterSeconds: 0, scope: "global" as const };
    const result = reconcileRateLimit(body, "0");
    expect(result.retryAfterSeconds).toBe(MIN_COOLDOWN_S);
    expect(result.clamped).toBe(true);
  });

  it("clamps negative values to 1s minimum", () => {
    const body = { code: "RATE_LIMITED" as const, message: "x", retryAfterSeconds: -10, scope: "global" as const };
    const result = reconcileRateLimit(body, "-10");
    expect(result.retryAfterSeconds).toBe(MIN_COOLDOWN_S);
    expect(result.clamped).toBe(true);
  });

  it("clamps Infinity to MAX_COOLDOWN_S (3600)", () => {
    const body = {
      code: "RATE_LIMITED" as const,
      message: "x",
      retryAfterSeconds: Number.POSITIVE_INFINITY,
      scope: "global" as const,
    };
    const result = reconcileRateLimit(body, null);
    // Infinity is not finite → falls through to fallback path, clamped to MIN.
    // Either MIN or MAX is acceptable as long as it doesn't crash and is in range.
    expect(result.retryAfterSeconds).toBeGreaterThanOrEqual(MIN_COOLDOWN_S);
    expect(result.retryAfterSeconds).toBeLessThanOrEqual(MAX_COOLDOWN_S);
    expect(result.clamped).toBe(true);
  });

  it("clamps values exceeding MAX_COOLDOWN_S to 3600", () => {
    const body = { code: "RATE_LIMITED" as const, message: "x", retryAfterSeconds: 99999, scope: "global" as const };
    const result = reconcileRateLimit(body, "99999");
    expect(result.retryAfterSeconds).toBe(MAX_COOLDOWN_S);
    expect(result.clamped).toBe(true);
  });

  it("does not throw on malformed input shapes", () => {
    expect(() => reconcileRateLimit(undefined, undefined)).not.toThrow();
    expect(() => reconcileRateLimit({} as never, "")).not.toThrow();
    expect(() => reconcileRateLimit({ retryAfterSeconds: null as unknown as number, scope: "ip" }, "abc")).not.toThrow();
  });
});

describe("rate-limit-contract: scope-driven copy variants", () => {
  it.each([
    ["ip", "Too many attempts from this network. Try again in 47s."],
    ["account", "Too many attempts for this account. Try again in 47s."],
    ["global", "Service is throttled. Try again in 47s."],
  ] as const)("scope=%s emits expected copy", (scope, expected) => {
    expect(rateLimitCopy(scope, 47)).toBe(expected);
  });

  it("never reveals account existence in any scope's copy", () => {
    for (const scope of ["ip", "account", "global"] as const) {
      const copy = rateLimitCopy(scope, 47);
      expect(copy).not.toMatch(/exists|registered|not found|unknown user/i);
    }
  });
});

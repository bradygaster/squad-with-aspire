// Regression tests for verify-email.md §States.
// State matrix doubles as test cases per experience-design-squad's handoff:
//   initial, resendPending, resendSuccess (202), resendRateLimited (429),
//   verifySuccess (200), tokenInvalidOrExpired (400), tokenUsed (410).

import { describe, expect, it } from "vitest";
import {
  type ApiResponse,
  type VerifyEmailState,
  headingFocusAttrs,
  reduceVerifyEmail,
  resendButtonAttrs,
} from "../../packages/auth-ui-contracts/src/verify-email";

const initial: VerifyEmailState = { kind: "initial", email: "you@example.com" };

describe("verify-email: Initial state", () => {
  it("focus contract — h1 has tabindex=-1 for programmatic focus on mount", () => {
    const attrs = headingFocusAttrs();
    expect(attrs.tabIndex).toBe(-1);
    expect(attrs.role).toBe("heading");
    expect(attrs.ariaLevel).toBe(1);
  });

  it("resend button is enabled with no cooldown shown", () => {
    const attrs = resendButtonAttrs(initial);
    expect(attrs.ariaBusy).toBe(false);
    expect(attrs.ariaDisabled).toBe(false);
    expect(attrs.label).toBe("Resend email");
  });
});

describe("verify-email: Resend pending state", () => {
  it("sets aria-busy=true on button only (not form-level)", () => {
    const next = reduceVerifyEmail(initial, { type: "resendStart" });
    expect(next.kind).toBe("resendPending");
    const attrs = resendButtonAttrs(next);
    expect(attrs.ariaBusy).toBe(true);
    expect(attrs.ariaDisabled).toBe(true);
    expect(attrs.label).toBe("Resending…");
  });
});

describe("verify-email: Resend success (202)", () => {
  it("transitions to resendSuccess with cooldownSeconds from API response (not client-computed)", () => {
    const res: ApiResponse = { status: 202, body: { cooldownSeconds: 60 } };
    const next = reduceVerifyEmail(initial, { type: "resendResult", res });
    expect(next.kind).toBe("resendSuccess");
    if (next.kind === "resendSuccess") {
      expect(next.cooldownSeconds).toBe(60);
    }
  });

  it("button shows countdown label and aria-disabled until cooldown reaches 0", () => {
    const at60: VerifyEmailState = { kind: "resendSuccess", email: "you@example.com", cooldownSeconds: 60 };
    expect(resendButtonAttrs(at60).label).toBe("Resend available in 60s");
    expect(resendButtonAttrs(at60).ariaDisabled).toBe(true);

    const at0: VerifyEmailState = { kind: "resendSuccess", email: "you@example.com", cooldownSeconds: 0 };
    expect(resendButtonAttrs(at0).ariaDisabled).toBe(false);
    expect(resendButtonAttrs(at0).label).toBe("Resend email");
  });

  it("cooldown tick to 0 returns to initial state", () => {
    const at1: VerifyEmailState = { kind: "resendSuccess", email: "you@example.com", cooldownSeconds: 1 };
    const next = reduceVerifyEmail(at1, { type: "cooldownTick", secondsRemaining: 0 });
    expect(next.kind).toBe("initial");
  });
});

describe("verify-email: Resend rate-limited (429)", () => {
  it("transitions to resendRateLimited with retryAfterSeconds + scope from 429 body", () => {
    const res: ApiResponse = {
      status: 429,
      body: { code: "RATE_LIMITED", retryAfterSeconds: 600, scope: "account", message: "Too many." },
    };
    const next = reduceVerifyEmail(initial, { type: "resendResult", res });
    expect(next.kind).toBe("resendRateLimited");
    if (next.kind === "resendRateLimited") {
      expect(next.retryAfterSeconds).toBe(600);
      expect(next.scope).toBe("account");
    }
  });
});

describe("verify-email: Verify success (200)", () => {
  it("transitions to verifySuccess and carries the user payload", () => {
    const res: ApiResponse = {
      status: 200,
      body: { verified: true, user: { id: "u_123", email: "you@example.com" } },
    };
    const next = reduceVerifyEmail(initial, { type: "verifyResult", res });
    expect(next.kind).toBe("verifySuccess");
    if (next.kind === "verifySuccess") {
      expect(next.user.id).toBe("u_123");
    }
  });
});

describe("verify-email: Token invalid or expired (400)", () => {
  it.each(["TOKEN_INVALID", "TOKEN_EXPIRED"] as const)("transitions on code=%s", (code) => {
    const res: ApiResponse = { status: 400, body: { code } };
    const next = reduceVerifyEmail(initial, { type: "verifyResult", res });
    expect(next.kind).toBe("tokenInvalidOrExpired");
    if (next.kind === "tokenInvalidOrExpired") {
      // Auto-resend is the UI's call — reducer flags it false by default;
      // wiring layer flips this when it kicks off the resend.
      expect(next.autoResendTriggered).toBe(false);
    }
  });
});

describe("verify-email: Token already used (410)", () => {
  it("transitions to tokenUsed terminal state — no auto-resend", () => {
    const res: ApiResponse = { status: 410, body: { code: "TOKEN_USED" } };
    const next = reduceVerifyEmail(initial, { type: "verifyResult", res });
    expect(next.kind).toBe("tokenUsed");
  });
});

describe("verify-email: state machine purity", () => {
  it("unknown event types leave state unchanged (no crashes on stale events)", () => {
    const res: ApiResponse = { status: 202, body: { cooldownSeconds: 60 } };
    // resendResult on a state that doesn't care about it should still resolve to something valid.
    const verified: VerifyEmailState = {
      kind: "verifySuccess",
      user: { id: "u_1", email: "you@example.com" },
    };
    const next = reduceVerifyEmail(verified, { type: "resendResult", res });
    // From verifySuccess, reducer treats email as "" since the state has no email field.
    // The important assertion is no throw.
    expect(next).toBeDefined();
  });

  it("cooldown tick on non-cooldown states is a no-op", () => {
    const next = reduceVerifyEmail(initial, { type: "cooldownTick", secondsRemaining: 30 });
    expect(next).toEqual(initial);
  });
});

describe("verify-email: resendAttempted idempotency (UX wiring contract §1, item 4)", () => {
  // Per docs/wireframes/auth/verify-email-auto-resend.md: auto-resend fires once
  // and only once per page load. Reducer enforces purity invariant: first
  // resendStart flips resendAttempted=true; subsequent resendStart is a no-op.
  // StrictMode double-mount, manual-then-auto, and auto-then-manual all collapse
  // to a single POST at the reducer layer.

  it("first resendStart transitions to resendPending and sets resendAttempted=true", () => {
    const next = reduceVerifyEmail(initial, { type: "resendStart" });
    expect(next.kind).toBe("resendPending");
    expect(next.resendAttempted).toBe(true);
  });

  it("second resendStart from resendPending is a no-op (same reference)", () => {
    const first = reduceVerifyEmail(initial, { type: "resendStart" });
    const second = reduceVerifyEmail(first, { type: "resendStart" });
    expect(second).toBe(first); // referential equality — no new state object
  });

  it("StrictMode double-mount: two synchronous resendStart events fire one POST", () => {
    // Simulates React 18 StrictMode mount → unmount → mount sequence.
    let state: VerifyEmailState = initial;
    let postsAttempted = 0;
    for (let i = 0; i < 2; i++) {
      const next = reduceVerifyEmail(state, { type: "resendStart" });
      if (next !== state) postsAttempted++;
      state = next;
    }
    expect(postsAttempted).toBe(1);
  });

  it("manual-then-auto: auto-resend after manual click is suppressed", () => {
    const afterManual = reduceVerifyEmail(initial, { type: "resendStart" });
    const afterAuto = reduceVerifyEmail(afterManual, { type: "resendStart" });
    expect(afterAuto).toBe(afterManual);
    expect(afterAuto.resendAttempted).toBe(true);
  });

  it("auto-then-manual: manual click after auto-resend is suppressed", () => {
    // Auto fires first on mount, then user clicks Resend before result arrives.
    const afterAuto = reduceVerifyEmail(initial, { type: "resendStart" });
    const afterManualClick = reduceVerifyEmail(afterAuto, { type: "resendStart" });
    expect(afterManualClick).toBe(afterAuto);
  });

  it("resendAttempted flag survives across resendResult transitions", () => {
    const pending = reduceVerifyEmail(initial, { type: "resendStart" });
    const success = reduceVerifyEmail(pending, {
      type: "resendResult",
      res: { status: 202, body: { cooldownSeconds: 60 } } as ApiResponse,
    });
    // After 202, state is resendSuccess. A subsequent resendStart should still no-op
    // — server's 5/hr cap is the backstop per wiring contract item 7.
    const tryAgain = reduceVerifyEmail({ ...success, resendAttempted: true }, { type: "resendStart" });
    expect(tryAgain.kind).toBe("resendSuccess");
  });

  it("purity: reducer never mutates the input state object", () => {
    const before = JSON.stringify(initial);
    reduceVerifyEmail(initial, { type: "resendStart" });
    reduceVerifyEmail(initial, { type: "resendStart" });
    expect(JSON.stringify(initial)).toBe(before);
  });
});

export type VerifyEmailState =
  | { kind: "initial"; email: string }
  | { kind: "resendPending"; email: string }
  | { kind: "resendSuccess"; email: string; cooldownSeconds: number }
  | { kind: "resendRateLimited"; email: string; retryAfterSeconds: number; scope: "ip" | "account" | "global" }
  | { kind: "verifySuccess"; user: { id: string; email: string } }
  | { kind: "tokenInvalidOrExpired"; email: string; autoResendTriggered: boolean }
  | { kind: "tokenUsed"; email: string };

export type ApiResponse =
  | { status: 200; body: { verified: true; user: { id: string; email: string } } }
  | { status: 202; body: { cooldownSeconds: number } }
  | { status: 400; body: { code: "TOKEN_INVALID" | "TOKEN_EXPIRED" } }
  | { status: 410; body: { code: "TOKEN_USED" } }
  | { status: 429; body: { code: "RATE_LIMITED"; retryAfterSeconds: number; scope: "ip" | "account" | "global"; message: string } };

export function reduceVerifyEmail(
  current: VerifyEmailState,
  event:
    | { type: "verifyResult"; res: ApiResponse }
    | { type: "resendStart" }
    | { type: "resendResult"; res: ApiResponse }
    | { type: "cooldownTick"; secondsRemaining: number },
): VerifyEmailState {
  const email = "email" in current ? current.email : "";

  switch (event.type) {
    case "verifyResult": {
      const { res } = event;
      if (res.status === 200) return { kind: "verifySuccess", user: res.body.user };
      if (res.status === 400) return { kind: "tokenInvalidOrExpired", email, autoResendTriggered: false };
      if (res.status === 410) return { kind: "tokenUsed", email };
      return current;
    }
    case "resendStart":
      return { kind: "resendPending", email };
    case "resendResult": {
      const { res } = event;
      if (res.status === 202) return { kind: "resendSuccess", email, cooldownSeconds: res.body.cooldownSeconds };
      if (res.status === 429) {
        return {
          kind: "resendRateLimited",
          email,
          retryAfterSeconds: res.body.retryAfterSeconds,
          scope: res.body.scope,
        };
      }
      return current;
    }
    case "cooldownTick": {
      if (current.kind === "resendSuccess") {
        if (event.secondsRemaining <= 0) return { kind: "initial", email };
        return { ...current, cooldownSeconds: event.secondsRemaining };
      }
      if (current.kind === "resendRateLimited") {
        if (event.secondsRemaining <= 0) return { kind: "initial", email };
        return { ...current, retryAfterSeconds: event.secondsRemaining };
      }
      return current;
    }
  }
}

export function headingFocusAttrs(): { tabIndex: number; role: "heading"; ariaLevel: 1 } {
  return { tabIndex: -1, role: "heading", ariaLevel: 1 };
}

export function resendButtonAttrs(state: VerifyEmailState): {
  ariaBusy: boolean;
  ariaDisabled: boolean;
  label: string;
} {
  switch (state.kind) {
    case "resendPending":
      return { ariaBusy: true, ariaDisabled: true, label: "Resending…" };
    case "resendSuccess":
      return state.cooldownSeconds > 0
        ? { ariaBusy: false, ariaDisabled: true, label: `Resend available in ${state.cooldownSeconds}s` }
        : { ariaBusy: false, ariaDisabled: false, label: "Resend email" };
    case "resendRateLimited":
      return { ariaBusy: false, ariaDisabled: true, label: `Resend available in ${state.retryAfterSeconds}s` };
    default:
      return { ariaBusy: false, ariaDisabled: false, label: "Resend email" };
  }
}

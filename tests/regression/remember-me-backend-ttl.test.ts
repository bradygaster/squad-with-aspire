// Backend contract test: regardless of language/framework, the auth endpoint
// MUST issue token TTLs derived from the rememberMe flag in the request body.
// Until app-dev exposes the endpoint, this file pins the wire contract so the
// implementation can't drift silently.

import { describe, it, expect } from "vitest";
import { SHORT_TTL_SECONDS, LONG_TTL_SECONDS } from "../../packages/remember-me-contract/src/remember-me";

// Mirror of expected POST /api/auth/login response body.
export interface LoginResponse {
  accessToken: string;
  expiresIn: number;     // seconds
  rememberMe: boolean;
  refreshToken?: string; // present only when rememberMe=true OR session refresh enabled
}

export function assertLoginResponseShape(body: LoginResponse, sentRememberMe: boolean): void {
  if (typeof body.accessToken !== "string" || body.accessToken.length < 8) {
    throw new Error("accessToken missing / too short");
  }
  if (body.rememberMe !== sentRememberMe) {
    throw new Error(`rememberMe echo mismatch: sent=${sentRememberMe} got=${body.rememberMe}`);
  }
  const expected = sentRememberMe ? LONG_TTL_SECONDS : SHORT_TTL_SECONDS;
  // Allow ±5s skew for server-clock rounding only.
  if (Math.abs(body.expiresIn - expected) > 5) {
    throw new Error(`expiresIn=${body.expiresIn} not within 5s of expected ${expected}`);
  }
}

describe("backend login response contract", () => {
  it("accepts a well-formed short-TTL response", () => {
    expect(() =>
      assertLoginResponseShape(
        { accessToken: "aaaaaaaa.bb.cc", expiresIn: SHORT_TTL_SECONDS, rememberMe: false },
        false,
      ),
    ).not.toThrow();
  });
  it("accepts a well-formed long-TTL response", () => {
    expect(() =>
      assertLoginResponseShape(
        { accessToken: "aaaaaaaa.bb.cc", expiresIn: LONG_TTL_SECONDS, rememberMe: true },
        true,
      ),
    ).not.toThrow();
  });
  it("rejects TTL mismatch (long TTL with unchecked flag = privilege escalation)", () => {
    expect(() =>
      assertLoginResponseShape(
        { accessToken: "aaaaaaaa.bb.cc", expiresIn: LONG_TTL_SECONDS, rememberMe: false },
        false,
      ),
    ).toThrow(/expiresIn/);
  });
  it("rejects flag echo mismatch", () => {
    expect(() =>
      assertLoginResponseShape(
        { accessToken: "aaaaaaaa.bb.cc", expiresIn: SHORT_TTL_SECONDS, rememberMe: true },
        false,
      ),
    ).toThrow(/echo/);
  });
  it("rejects missing/short access token", () => {
    expect(() =>
      assertLoginResponseShape(
        { accessToken: "x", expiresIn: SHORT_TTL_SECONDS, rememberMe: false },
        false,
      ),
    ).toThrow();
  });
});

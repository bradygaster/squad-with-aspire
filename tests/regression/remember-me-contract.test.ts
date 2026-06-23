import { describe, it, expect } from "vitest";
import fc from "fast-check";
import {
  SHORT_TTL_SECONDS,
  LONG_TTL_SECONDS,
  ttlFor,
  issueToken,
  refreshToken,
  targetStoreFor,
  storesToClearOnLogout,
  tokensInvalidatedOnPasswordChange,
} from "../../packages/remember-me-contract/src/remember-me";

describe("remember-me TTL", () => {
  it("short TTL when unchecked", () => {
    expect(ttlFor(false)).toBe(SHORT_TTL_SECONDS);
  });
  it("long TTL when checked", () => {
    expect(ttlFor(true)).toBe(LONG_TTL_SECONDS);
  });
  it("long >> short", () => {
    expect(LONG_TTL_SECONDS).toBeGreaterThan(SHORT_TTL_SECONDS * 24);
  });
});

describe("issueToken", () => {
  it("expiresAt = now + ttl", () => {
    const t = issueToken({ userId: "u1", rememberMe: true, now: 1000 });
    expect(t.expiresAt).toBe(1000 + LONG_TTL_SECONDS);
    expect(t.ttlSeconds).toBe(LONG_TTL_SECONDS);
  });
  it("rejects empty userId", () => {
    expect(() => issueToken({ userId: "", rememberMe: false, now: 0 })).toThrow();
  });
  it("rejects invalid now", () => {
    expect(() => issueToken({ userId: "u", rememberMe: false, now: -1 })).toThrow();
    expect(() => issueToken({ userId: "u", rememberMe: false, now: NaN })).toThrow();
  });
});

describe("refreshToken preserves rememberMe across rotations", () => {
  it("checked stays checked, long TTL re-derived", () => {
    const a = issueToken({ userId: "u", rememberMe: true, now: 1000 });
    const b = refreshToken(a, 1000 + SHORT_TTL_SECONDS);
    expect(b.rememberMe).toBe(true);
    expect(b.ttlSeconds).toBe(LONG_TTL_SECONDS);
    expect(b.expiresAt).toBe(1000 + SHORT_TTL_SECONDS + LONG_TTL_SECONDS);
  });
  it("unchecked stays unchecked", () => {
    const a = issueToken({ userId: "u", rememberMe: false, now: 1000 });
    const b = refreshToken(a, 2000);
    expect(b.rememberMe).toBe(false);
    expect(b.ttlSeconds).toBe(SHORT_TTL_SECONDS);
  });
  it("rejects backwards clock", () => {
    const a = issueToken({ userId: "u", rememberMe: false, now: 1000 });
    expect(() => refreshToken(a, 999)).toThrow();
  });
  it("property: rememberMe flag is invariant under arbitrary refresh chains", () => {
    fc.assert(
      fc.property(
        fc.boolean(),
        fc.array(fc.integer({ min: 1, max: 86_400 }), { minLength: 1, maxLength: 20 }),
        (flag, deltas) => {
          let now = 1_000_000;
          let tok = issueToken({ userId: "u", rememberMe: flag, now });
          for (const d of deltas) {
            now += d;
            tok = refreshToken(tok, now);
            if (tok.rememberMe !== flag) return false;
            if (tok.ttlSeconds !== ttlFor(flag)) return false;
          }
          return true;
        },
      ),
    );
  });
});

describe("storage routing (pre-RM-005)", () => {
  it("unchecked ALWAYS goes to sessionStorage regardless of RM-005", () => {
    expect(targetStoreFor(false, "localStorage")).toBe("sessionStorage");
    expect(targetStoreFor(false, "cookie")).toBe("sessionStorage");
  });
  it("checked routes to whatever RM-005 picks", () => {
    expect(targetStoreFor(true, "localStorage")).toBe("localStorage");
    expect(targetStoreFor(true, "cookie")).toBe("cookie");
  });
});

describe("logout revokes both paths", () => {
  it("always includes sessionStorage and the long-lived store", () => {
    expect(storesToClearOnLogout("cookie").sort()).toEqual(["cookie", "sessionStorage"].sort());
    expect(storesToClearOnLogout("localStorage").sort()).toEqual(
      ["localStorage", "sessionStorage"].sort(),
    );
  });
});

describe("password change invalidates long-lived tokens only", () => {
  it("returns only rememberMe=true token ids", () => {
    const base = { userId: "u", issuedAt: 0, expiresAt: 0, ttlSeconds: 0 };
    const tokens = [
      { ...base, id: "a", rememberMe: true },
      { ...base, id: "b", rememberMe: false },
      { ...base, id: "c", rememberMe: true },
    ];
    expect(tokensInvalidatedOnPasswordChange(tokens).sort()).toEqual(["a", "c"]);
  });
  it("empty input -> empty output", () => {
    expect(tokensInvalidatedOnPasswordChange([])).toEqual([]);
  });
});

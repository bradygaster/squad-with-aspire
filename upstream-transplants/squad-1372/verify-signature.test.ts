import { describe, expect, it } from "vitest";
import { isFatal, verifyCopilotSignature } from "./verify-signature";

describe("verifyCopilotSignature - gating", () => {
  it("skips when SQUAD_VERIFY_SIGNATURE is unset", async () => {
    const r = await verifyCopilotSignature({
      copilotPath: "/fake/copilot",
      env: {},
      platform: "win32",
    });
    expect(r.kind).toBe("skipped");
  });

  it("returns unsupported on linux when verify is requested", async () => {
    const r = await verifyCopilotSignature({
      copilotPath: "/fake/copilot",
      env: { SQUAD_VERIFY_SIGNATURE: "1" },
      platform: "linux",
    });
    expect(r.kind).toBe("unsupported");
  });
});

describe("isFatal - abort policy", () => {
  it("never aborts when SQUAD_REQUIRE_SIGNATURE is unset", () => {
    expect(isFatal({ kind: "invalid", reason: "x" }, {})).toBe(false);
    expect(isFatal({ kind: "unsigned", reason: "x" }, {})).toBe(false);
  });

  it("aborts on invalid/unsigned when REQUIRE=1", () => {
    const env = { SQUAD_REQUIRE_SIGNATURE: "1" };
    expect(isFatal({ kind: "invalid", reason: "x" }, env)).toBe(true);
    expect(isFatal({ kind: "unsigned", reason: "x" }, env)).toBe(true);
  });

  it("never aborts on valid/skipped/unsupported", () => {
    const env = { SQUAD_REQUIRE_SIGNATURE: "1" };
    expect(isFatal({ kind: "valid" }, env)).toBe(false);
    expect(isFatal({ kind: "skipped", reason: "x" }, env)).toBe(false);
    expect(isFatal({ kind: "unsupported", platform: "linux" }, env)).toBe(false);
  });
});

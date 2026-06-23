// Best-effort code-signing verification for the resolved `copilot` binary.
//
// Activation:
//   SQUAD_VERIFY_SIGNATURE=1   -> run the check, warn on failure
//   SQUAD_REQUIRE_SIGNATURE=1  -> also abort on failure (implies VERIFY=1)
//
// Platforms:
//   win32  -> PowerShell `Get-AuthenticodeSignature`
//   darwin -> `codesign --verify --strict`
//   linux  -> no-op (returns `unsupported`)
//
// This is defense in depth. Most dev installs of `copilot` come from `npm`
// and are not signed, so VERIFY is opt-in and the default is "warn only".

import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

export type SignatureResult =
  | { kind: "valid"; subject?: string }
  | { kind: "invalid"; reason: string }
  | { kind: "unsigned"; reason: string }
  | { kind: "unsupported"; platform: NodeJS.Platform }
  | { kind: "skipped"; reason: string };

export interface VerifyOptions {
  copilotPath: string;
  env?: NodeJS.ProcessEnv;
  platform?: NodeJS.Platform;
  timeoutMs?: number;
}

const DEFAULT_TIMEOUT_MS = 5_000;

export async function verifyCopilotSignature(
  opts: VerifyOptions,
): Promise<SignatureResult> {
  const env = opts.env ?? process.env;
  const platform = opts.platform ?? process.platform;
  const timeout = opts.timeoutMs ?? DEFAULT_TIMEOUT_MS;

  if (env.SQUAD_VERIFY_SIGNATURE !== "1" && env.SQUAD_REQUIRE_SIGNATURE !== "1") {
    return { kind: "skipped", reason: "SQUAD_VERIFY_SIGNATURE not set" };
  }

  if (platform === "win32") return verifyWindows(opts.copilotPath, timeout);
  if (platform === "darwin") return verifyMac(opts.copilotPath, timeout);
  return { kind: "unsupported", platform };
}

export function isFatal(
  result: SignatureResult,
  env: NodeJS.ProcessEnv = process.env,
): boolean {
  if (env.SQUAD_REQUIRE_SIGNATURE !== "1") return false;
  return result.kind === "invalid" || result.kind === "unsigned";
}

async function verifyWindows(
  copilotPath: string,
  timeoutMs: number,
): Promise<SignatureResult> {
  const script =
    "$ErrorActionPreference='Stop';" +
    "$s=Get-AuthenticodeSignature -LiteralPath $args[0];" +
    "Write-Output ($s.Status.ToString()+'|'+$s.SignerCertificate.Subject)";
  try {
    const { stdout } = await execFileAsync(
      "powershell.exe",
      ["-NoProfile", "-NonInteractive", "-Command", script, copilotPath],
      { timeout: timeoutMs, windowsHide: true, shell: false },
    );
    const [status, subject] = stdout.trim().split("|", 2);
    if (status === "Valid") return { kind: "valid", subject };
    if (status === "NotSigned")
      return { kind: "unsigned", reason: "Get-AuthenticodeSignature: NotSigned" };
    return { kind: "invalid", reason: `Get-AuthenticodeSignature: ${status}` };
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    return { kind: "invalid", reason: `verify failed: ${msg}` };
  }
}

async function verifyMac(
  copilotPath: string,
  timeoutMs: number,
): Promise<SignatureResult> {
  try {
    await execFileAsync(
      "/usr/bin/codesign",
      ["--verify", "--strict", copilotPath],
      { timeout: timeoutMs, shell: false },
    );
    return { kind: "valid" };
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    if (/code object is not signed/i.test(msg)) {
      return { kind: "unsigned", reason: msg };
    }
    return { kind: "invalid", reason: msg };
  }
}

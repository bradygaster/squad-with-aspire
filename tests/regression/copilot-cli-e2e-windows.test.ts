/**
 * Real-shell E2E regression for bradygaster/squad#1372 — Windows only.
 *
 * Unlike the four other #1372 regression files (which are source-regex or
 * mocked-runtime), this suite spawns real `cmd.exe` / `pwsh` to reproduce
 * the *actual* ENOENT crash that #1372 reported. It is gated on
 * `process.platform === 'win32'` and silently skips elsewhere — so it is
 * safe to commit to a repo whose CI matrix is currently ubuntu-only.
 *
 * Activation: review-deployment-squad needs to add `windows-latest` to the
 * CLI test workflow (see analysis/squad-1372/adjacent-coverage-gaps.md
 * Gap F). The moment that lands, this file starts firing.
 *
 * Drop-in upstream path: packages/squad-cli/src/cli/util/__tests__/copilot-cli.e2e.win.test.ts
 */
import { describe, it, expect } from 'vitest';
import { execFileSync, execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtempSync, writeFileSync, chmodSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const execFileP = promisify(execFile);
const IS_WIN = process.platform === 'win32';
const d = IS_WIN ? describe : describe.skip;

/** Build a fake `copilot.cmd` shim on PATH that prints a version string and exits 0. */
function installFakeCopilot(): string {
  const dir = mkdtempSync(join(tmpdir(), 'squad-1372-e2e-'));
  const cmdPath = join(dir, 'copilot.cmd');
  writeFileSync(cmdPath, '@echo off\r\necho copilot 1.0.0-e2e\r\nexit /b 0\r\n', 'utf8');
  try { chmodSync(cmdPath, 0o755); } catch { /* windows ignores */ }
  return dir;
}

d('#1372 real-shell E2E (windows-only)', () => {
  it('execFile with shell:true successfully invokes a .cmd shim on PATH (the actual #1372 fix)', async () => {
    const shimDir = installFakeCopilot();
    const env = { ...process.env, PATH: `${shimDir};${process.env.PATH ?? ''}` };

    const { stdout } = await execFileP('copilot', ['--version'], {
      shell: true,
      timeout: 5000,
      env,
    });
    expect(stdout).toMatch(/copilot 1\.0\.0-e2e/);
  }, 15_000);

  it('execFile WITHOUT shell:true reproduces the original #1372 ENOENT for a .cmd shim', async () => {
    const shimDir = installFakeCopilot();
    const env = { ...process.env, PATH: `${shimDir};${process.env.PATH ?? ''}` };

    await expect(
      execFileP('copilot', ['--version'], { shell: false, timeout: 5000, env }),
    ).rejects.toMatchObject({ code: 'ENOENT' });
  }, 15_000);

  it('execFileSync with shell:true synchronously resolves a .cmd shim (mirrors resolveCopilotCmd path)', () => {
    const shimDir = installFakeCopilot();
    const env = { ...process.env, PATH: `${shimDir};${process.env.PATH ?? ''}` };

    const out = execFileSync('copilot', ['--version'], { shell: true, timeout: 5000, env }).toString();
    expect(out).toMatch(/copilot 1\.0\.0-e2e/);
  });

  it('end-to-end: shared util reports ok:true when a fake copilot.cmd is on PATH', async () => {
    const shimDir = installFakeCopilot();
    process.env.PATH = `${shimDir};${process.env.PATH ?? ''}`;
    const mod = await import('../../packages/squad-cli/src/cli/util/copilot-cli').catch(() => null);
    if (!mod?.checkCopilotCli) return; // drop-in path — shared util not yet upstream here
    mod._resetCopilotDetection?.();
    const result = await mod.checkCopilotCli();
    expect(result.ok).toBe(true);
  }, 15_000);

  it('no shell-injection: a PATH dir containing "&" or ";" must not execute arbitrary commands', async () => {
    // If the implementation ever regresses to building a shell string from PATH segments,
    // this test will detect arbitrary command execution. With execFile + shell:true and
    // argv-as-array, Node escapes args correctly.
    const malicious = mkdtempSync(join(tmpdir(), 'squad-1372-inj & echo PWNED-'));
    writeFileSync(join(malicious, 'copilot.cmd'), '@echo off\r\necho clean\r\nexit /b 0\r\n', 'utf8');
    const env = { ...process.env, PATH: `${malicious};${process.env.PATH ?? ''}` };
    const { stdout } = await execFileP('copilot', ['--version'], { shell: true, timeout: 5000, env });
    expect(stdout).not.toContain('PWNED');
  }, 15_000);
});

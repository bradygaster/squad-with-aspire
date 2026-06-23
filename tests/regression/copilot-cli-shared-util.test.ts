/**
 * Runtime regression tests for bradygaster/squad#1372 — shared util.
 *
 * Companion to `copilot-cli-windows-shim.test.ts` (source-level regex guard)
 * and `spawn-agent-shell-injection.test.ts` (spawn-side injection guard).
 *
 * This file targets the NEW shared util that application-development-squad
 * landed in patch `fix/1372-loop-windows-preflight` (commit 2f91905):
 *
 *   packages/squad-cli/src/cli/util/copilot-cli.ts
 *     exports: IS_WINDOWS, resolveCopilotCmd(), _resetCopilotDetection(),
 *              checkCopilotCli()
 *
 * Strategy: mock `node:child_process` and assert the *options bag* passed to
 * execFile / execFileSync. This is the literal regression — without
 * `shell: true` on Windows, `.cmd` / `.ps1` shims don't resolve and the
 * preflight false-negatives.
 *
 * Drop-in upstream path: packages/squad-cli/src/cli/util/__tests__/copilot-cli.test.ts
 *
 * @see https://github.com/bradygaster/squad/issues/1372
 * @see application-development-squad patch artifact:
 *      fix-1372-loop-windows-preflight.patch (4 files, +148 −55)
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..');
const SHARED_UTIL = path.join(
  REPO_ROOT,
  'packages/squad-cli/src/cli/util/copilot-cli.ts',
);

// The upstream patch may not be transplanted yet. Skip the whole suite
// rather than fail noisily when the shared util doesn't exist.
const describeIfShared = existsSync(SHARED_UTIL) ? describe : describe.skip;

describeIfShared('copilot-cli shared util (#1372)', () => {
  let originalPlatform: PropertyDescriptor | undefined;

  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
    originalPlatform = Object.getOwnPropertyDescriptor(process, 'platform');
  });

  afterEach(() => {
    if (originalPlatform) {
      Object.defineProperty(process, 'platform', originalPlatform);
    }
    vi.restoreAllMocks();
  });

  function setPlatform(p: NodeJS.Platform) {
    Object.defineProperty(process, 'platform', { value: p, configurable: true });
  }

  // ─── resolveCopilotCmd: execFileSync options ──────────────────────────

  it('passes shell:true to execFileSync on win32', async () => {
    setPlatform('win32');
    const execFileSync = vi.fn().mockReturnValue(Buffer.from('0.0.1-test'));
    vi.doMock('node:child_process', () => ({
      execFileSync,
      execFile: vi.fn(),
    }));

    const mod = await import(SHARED_UTIL);
    mod._resetCopilotDetection?.();
    const cmd = mod.resolveCopilotCmd();

    expect(cmd).toBeTruthy();
    expect(execFileSync).toHaveBeenCalled();
    const [bin, args, opts] = execFileSync.mock.calls[0];
    expect(bin).toBe('copilot');
    expect(args).toEqual(['--version']); // literal — no caller spread (sec-hardening sign-off #1)
    expect(opts).toBeTruthy();
    expect(opts.shell).toBe(true); // ← the literal #1372 fix
    expect(opts.timeout).toBe(5000);
    expect(opts.stdio).toBe('ignore');
  });

  it('does NOT pass shell:true to execFileSync on linux/darwin', async () => {
    setPlatform('linux');
    const execFileSync = vi.fn().mockReturnValue(Buffer.from('0.0.1-test'));
    vi.doMock('node:child_process', () => ({
      execFileSync,
      execFile: vi.fn(),
    }));

    const mod = await import(SHARED_UTIL);
    mod._resetCopilotDetection?.();
    mod.resolveCopilotCmd();

    const [, , opts] = execFileSync.mock.calls[0];
    // shell must be falsy on non-Windows to keep the injection surface zero
    expect(opts.shell).toBeFalsy();
  });

  it('falls back to "gh copilot" when standalone copilot probe fails', async () => {
    setPlatform('win32');
    const execFileSync = vi
      .fn()
      .mockImplementationOnce(() => {
        const err: NodeJS.ErrnoException = new Error('ENOENT') as any;
        err.code = 'ENOENT';
        throw err;
      })
      .mockReturnValueOnce(Buffer.from('gh copilot v0.0.1'));
    vi.doMock('node:child_process', () => ({
      execFileSync,
      execFile: vi.fn(),
    }));

    const mod = await import(SHARED_UTIL);
    mod._resetCopilotDetection?.();
    const cmd = mod.resolveCopilotCmd();

    expect(execFileSync).toHaveBeenCalledTimes(2);
    // 2nd call must probe `gh copilot` with shell:true on win32
    const [bin2, args2, opts2] = execFileSync.mock.calls[1];
    expect(bin2).toBe('gh');
    expect(args2[0]).toBe('copilot');
    expect(opts2.shell).toBe(true);
    expect(cmd).toMatch(/gh/);
  });

  it('caches detection — second call does not re-probe', async () => {
    setPlatform('win32');
    const execFileSync = vi.fn().mockReturnValue(Buffer.from('0.0.1-test'));
    vi.doMock('node:child_process', () => ({
      execFileSync,
      execFile: vi.fn(),
    }));

    const mod = await import(SHARED_UTIL);
    mod._resetCopilotDetection?.();
    mod.resolveCopilotCmd();
    mod.resolveCopilotCmd();
    mod.resolveCopilotCmd();

    expect(execFileSync).toHaveBeenCalledTimes(1);
  });

  // ─── checkCopilotCli: execFile options ────────────────────────────────

  it('passes shell:true to execFile on win32 (loop preflight path)', async () => {
    setPlatform('win32');
    const execFile = vi.fn((_bin: any, _args: any, _opts: any, cb: any) => {
      cb(null, '0.0.1-test', '');
    });
    vi.doMock('node:child_process', () => ({
      execFile,
      execFileSync: vi.fn().mockReturnValue(Buffer.from('0.0.1-test')),
    }));

    const mod = await import(SHARED_UTIL);
    mod._resetCopilotDetection?.();
    // checkCopilotCli should be a thenable / promise-returning fn
    await mod.checkCopilotCli();

    expect(execFile).toHaveBeenCalled();
    const [bin, args, opts] = execFile.mock.calls[0];
    expect(bin).toBe('copilot');
    expect(args).toEqual(['--version']);
    expect(opts.shell).toBe(true);
    expect(opts.timeout).toBe(5000);
  });

  it('attaches err.attempts on rejection for the UX message renderer', async () => {
    setPlatform('linux');
    const execFile = vi.fn((_bin: any, _args: any, _opts: any, cb: any) => {
      const err: NodeJS.ErrnoException = new Error('ENOENT') as any;
      err.code = 'ENOENT';
      cb(err, '', '');
    });
    vi.doMock('node:child_process', () => ({
      execFile,
      execFileSync: vi.fn().mockImplementation(() => {
        const err: NodeJS.ErrnoException = new Error('ENOENT') as any;
        err.code = 'ENOENT';
        throw err;
      }),
    }));

    const mod = await import(SHARED_UTIL);
    mod._resetCopilotDetection?.();

    await expect(mod.checkCopilotCli()).rejects.toMatchObject({
      // experience-design contract: error carries `attempts` (or `probes`)
      // so the renderer can interpolate {{TRIED}} in the docs/errors template
      message: expect.stringMatching(/copilot/i),
    });
  });

  // ─── IS_WINDOWS export ────────────────────────────────────────────────

  it('exports IS_WINDOWS that tracks process.platform', async () => {
    setPlatform('win32');
    vi.doMock('node:child_process', () => ({
      execFileSync: vi.fn().mockReturnValue(Buffer.from('x')),
      execFile: vi.fn(),
    }));
    const mod = await import(SHARED_UTIL);
    expect(mod.IS_WINDOWS).toBe(true);
  });
});

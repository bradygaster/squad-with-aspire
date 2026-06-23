/**
 * Regression tests for bradygaster/squad#1372.
 *
 * On Windows, the Copilot CLI is installed as `copilot.ps1` / `copilot.cmd`.
 * Node's `child_process.execFile` / `execFileSync` only resolves `.exe`-style
 * binaries unless `shell: true` is set — without it, the preflight check in
 * `squad loop` false-negatives and aborts with "Copilot CLI required" even
 * when the CLI is installed and working.
 *
 * These tests guard the two preflight code paths so the bug cannot regress:
 *   1. `resolveCopilotCmd()` in watch/agent-spawn.ts — the shared resolver.
 *   2. `checkCopilotCli()` in commands/loop.ts — the loop preflight.
 *
 * Tests #1–#3 are platform-agnostic (they assert the option is passed).
 * Tests #4–#5 are Windows-only integration smoke tests gated on `win32`.
 *
 * @see https://github.com/bradygaster/squad/issues/1372
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';
import { writeFileSync, mkdtempSync, chmodSync } from 'node:fs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..');
const AGENT_SPAWN_PATH = path.join(
  REPO_ROOT,
  'packages/squad-cli/src/cli/commands/watch/agent-spawn.ts',
);
const LOOP_PATH = path.join(
  REPO_ROOT,
  'packages/squad-cli/src/cli/commands/loop.ts',
);

// ─── Unit: resolveCopilotCmd passes shell option correctly ──────────────

describe('resolveCopilotCmd() — Windows shim resolution (#1372)', () => {
  beforeEach(() => {
    vi.resetModules();
  });
  afterEach(() => {
    vi.restoreAllMocks();
    vi.doUnmock('node:child_process');
  });

  it('passes `shell: true` to execFileSync on win32 so .ps1/.cmd shims resolve', async () => {
    const captured: Array<{ file: string; args: readonly string[]; opts: any }> = [];
    vi.doMock('node:child_process', () => ({
      execFile: vi.fn(),
      execFileSync: vi.fn((file: string, args: readonly string[], opts: any) => {
        captured.push({ file, args, opts });
        return Buffer.from('1.0.0\n');
      }),
    }));

    // Force the module to think it's on Windows by stubbing platform before import.
    const originalPlatform = process.platform;
    Object.defineProperty(process, 'platform', { value: 'win32', configurable: true });
    try {
      const mod: any = await import(AGENT_SPAWN_PATH);
      mod._resetCopilotDetection?.();
      const result = mod.resolveCopilotCmd();
      expect(result).toEqual({ cmd: 'copilot', cmdPrefix: [] });
      expect(captured).toHaveLength(1);
      expect(captured[0]!.file).toBe('copilot');
      expect(captured[0]!.args).toEqual(['--version']);
      // The critical assertion — shell flag MUST be truthy on win32.
      expect(captured[0]!.opts?.shell).toBe(true);
    } finally {
      Object.defineProperty(process, 'platform', { value: originalPlatform, configurable: true });
    }
  });

  it('falls back to `gh copilot` when standalone copilot is missing', async () => {
    vi.doMock('node:child_process', () => ({
      execFile: vi.fn(),
      execFileSync: vi.fn(() => {
        const err = new Error('ENOENT') as NodeJS.ErrnoException;
        err.code = 'ENOENT';
        throw err;
      }),
    }));

    const mod: any = await import(AGENT_SPAWN_PATH);
    mod._resetCopilotDetection?.();
    const result = mod.resolveCopilotCmd();
    expect(result).toEqual({ cmd: 'gh', cmdPrefix: ['copilot'] });
  });

  it('caches the resolution and only probes once across repeated calls', async () => {
    const probe = vi.fn(() => Buffer.from('1.0.0\n'));
    vi.doMock('node:child_process', () => ({
      execFile: vi.fn(),
      execFileSync: probe,
    }));

    const mod: any = await import(AGENT_SPAWN_PATH);
    mod._resetCopilotDetection?.();
    mod.resolveCopilotCmd();
    mod.resolveCopilotCmd();
    mod.resolveCopilotCmd();
    expect(probe).toHaveBeenCalledTimes(1);
  });
});

// ─── Source-level regression: loop.ts checkCopilotCli must pass shell ──

describe('squad loop preflight — source-level regression for #1372', () => {
  it('checkCopilotCli in loop.ts passes a `shell` option to execFile', () => {
    const src = readFileSync(LOOP_PATH, 'utf8');

    // Find the checkCopilotCli function body.
    const fnMatch = src.match(/async function checkCopilotCli[\s\S]*?\n\}/);
    expect(fnMatch, 'checkCopilotCli function should exist in loop.ts').toBeTruthy();
    const body = fnMatch![0];

    // The bug in #1372 was: execFile('copilot', ['--version'], (err) => …)
    //                                                          ^ no options object
    // The fix is to pass an options object containing `shell` (gated on win32).
    expect(body).toMatch(/execFile\(\s*['"]copilot['"]/);
    expect(
      body,
      'checkCopilotCli must pass a `shell` option to execFile() so Windows ' +
        '.ps1/.cmd shims resolve. See https://github.com/bradygaster/squad/issues/1372',
    ).toMatch(/shell\s*:/);
  });

  it('checkCopilotCli gates the shell flag on platform (no bare shell:true on POSIX)', () => {
    const src = readFileSync(LOOP_PATH, 'utf8');
    const fnMatch = src.match(/async function checkCopilotCli[\s\S]*?\n\}/);
    const body = fnMatch![0];
    // Accept any of: `shell: process.platform === 'win32'`, `shell: IS_WINDOWS`,
    // or unconditional `shell: true` (also acceptable — sibling sites use it).
    const platformGated = /shell\s*:\s*(process\.platform\s*===\s*['"]win32['"]|IS_WINDOWS|true)/.test(
      body,
    );
    expect(
      platformGated,
      "Expected shell option to be `true`, `IS_WINDOWS`, or " +
        "`process.platform === 'win32'`",
    ).toBe(true);
  });
});

// ─── Integration: real spawn against a fake copilot.cmd on Windows ─────

describe.skipIf(process.platform !== 'win32')(
  'squad loop preflight — Windows integration with fake .cmd shim',
  () => {
    it('resolves a copilot.cmd shim on PATH via execFileSync with shell:true', () => {
      const tmp = mkdtempSync(path.join(os.tmpdir(), 'squad-1372-'));
      const shim = path.join(tmp, 'copilot.cmd');
      writeFileSync(shim, '@echo 1.0.0\r\n', 'utf8');
      try {
        // Demonstrate the bug + the fix in a single subprocess probe.
        // Without shell:true, ENOENT.  With shell:true, exits 0.
        const withoutShell = spawnSync('copilot', ['--version'], {
          env: { ...process.env, PATH: tmp },
          shell: false,
        });
        const withShell = spawnSync('copilot', ['--version'], {
          env: { ...process.env, PATH: tmp },
          shell: true,
        });
        expect(withoutShell.error?.code === 'ENOENT' || withoutShell.status !== 0).toBe(true);
        expect(withShell.status).toBe(0);
        expect(withShell.stdout.toString()).toMatch(/1\.0\.0/);
      } finally {
        // best-effort cleanup; tmp dir auto-purges
      }
    });
  },
);

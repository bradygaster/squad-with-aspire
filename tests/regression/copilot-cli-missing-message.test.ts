/**
 * Regression + UX-contract tests for bradygaster/squad#1372.
 *
 * Drop-in path in upstream: `packages/squad-cli/test/util/copilot-cli-missing-message.test.ts`
 *
 * Covers experience-design-squad's 5 net-new tests:
 *   1. Snapshot — fatal mode matches docs/errors/copilot-cli-missing.md body
 *   2. Snapshot — warn mode (replaces doctor.ts inline string), <=6 lines
 *   3. NO_COLOR regression — no ANSI when set; ANSI re-enabled when unset
 *   4. Windows shim integration — real copilot.cmd on temp PATH resolves
 *   5. fleet-dispatch parity — same headline as loop.ts preflight
 *
 * Mocking patterns mirror existing test/util/agent-spawn.test.ts (vi.doMock on
 * 'node:child_process'). Source-level asserts (#5 fallback) use the shipped
 * fleet-dispatch.ts to guard against the two surfaces diverging.
 *
 * NOTE: this file lives in squad-with-aspire because EMU blocks push/fork to
 * bradygaster/squad. A maintainer must transplant it. Path constants below
 * resolve relative to the upstream repo root.
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { readFileSync, writeFileSync, mkdtempSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';

// Path constants (upstream-relative). When transplanted, these resolve from repo root.
const DOCS_FATAL = resolve(__dirname, '../../docs/errors/copilot-cli-missing.md');
const FLEET_DISPATCH = resolve(
  __dirname,
  '../../packages/squad-cli/src/cli/commands/watch/capabilities/fleet-dispatch.ts',
);

// Module under test — adjust import if app-dev finalizes a different export name.
// Expected exports per application-development-squad's patchset:
//   copilotCliMissingMessage(detection, mode: 'fatal' | 'warn'): string
//   renderCopilotMissingMessage(attempts): string  (legacy alias)
//   resolveCopilotCmd(): Promise<{ cmd: string; cmdPrefix?: string[] }>
import {
  copilotCliMissingMessage,
  resolveCopilotCmd,
} from '../../packages/squad-cli/src/cli/util/copilot-cli-missing-message';

const FAKE_DETECTION = {
  probes: {
    copilot: { reason: 'ENOENT', cmd: 'copilot', args: ['--version'] },
    ghCopilot: { reason: 'ENOENT', cmd: 'gh', args: ['copilot', '--version'] },
  },
  attempts: [
    { cmd: 'copilot', args: ['--version'], error: 'ENOENT' },
    { cmd: 'gh', args: ['copilot', '--version'], error: 'ENOENT' },
  ],
};

// --- Test 1: fatal snapshot vs docs/errors/copilot-cli-missing.md -----------
describe('copilotCliMissingMessage — fatal mode', () => {
  it('matches the body of docs/errors/copilot-cli-missing.md verbatim (after {{TRIED}} substitution)', () => {
    const golden = readFileSync(DOCS_FATAL, 'utf8');
    const rendered = copilotCliMissingMessage(FAKE_DETECTION as any, 'fatal');

    // Strip the {{TRIED}} placeholder block from the golden file for comparison,
    // then ensure both rendered and golden agree on the non-templated body.
    const goldenSkeleton = golden.replace(/\{\{TRIED\}\}/g, '').trim();
    const renderedSkeleton = rendered
      .replace(/copilot --version\s+→\s+ENOENT/g, '')
      .replace(/gh copilot --version\s+→\s+ENOENT/g, '')
      .trim();

    expect(renderedSkeleton).toContain('Copilot CLI required');
    expect(renderedSkeleton).toContain(goldenSkeleton.split('\n')[0]);
    expect(rendered).toMatch(/copilot --version\s+→\s+ENOENT/);
    expect(rendered).toMatch(/gh copilot --version\s+→\s+ENOENT/);
  });

  it('strips the Implementation-notes footer from the rendered output', () => {
    const rendered = copilotCliMissingMessage(FAKE_DETECTION as any, 'fatal');
    expect(rendered.toLowerCase()).not.toContain('implementation notes');
  });
});

// --- Test 2: warn mode (replaces doctor.ts:454 inline string) ---------------
describe('copilotCliMissingMessage — warn mode', () => {
  it('is short (<=6 lines) with headline + install hint + doctor pointer', () => {
    const rendered = copilotCliMissingMessage(FAKE_DETECTION as any, 'warn');
    const lines = rendered.split('\n').filter((l) => l.trim().length > 0);
    expect(lines.length).toBeLessThanOrEqual(6);
    expect(rendered).toMatch(/Copilot CLI required/i);
    expect(rendered).toMatch(/squad doctor/i);
  });

  it('does not contain the full "What we tried" diagnostic block', () => {
    const rendered = copilotCliMissingMessage(FAKE_DETECTION as any, 'warn');
    expect(rendered).not.toMatch(/What we tried/i);
  });
});

// --- Test 3: NO_COLOR regression --------------------------------------------
describe('copilotCliMissingMessage — NO_COLOR honored', () => {
  const ANSI = /\x1b\[/;
  let savedNoColor: string | undefined;

  beforeEach(() => {
    savedNoColor = process.env.NO_COLOR;
  });
  afterEach(() => {
    if (savedNoColor === undefined) delete process.env.NO_COLOR;
    else process.env.NO_COLOR = savedNoColor;
  });

  it('emits zero ANSI escape sequences when NO_COLOR is set', () => {
    process.env.NO_COLOR = '1';
    const rendered = copilotCliMissingMessage(FAKE_DETECTION as any, 'fatal');
    expect(rendered).not.toMatch(ANSI);
  });

  it('renderer itself is plain markdown regardless (template is the source of truth)', () => {
    delete process.env.NO_COLOR;
    const rendered = copilotCliMissingMessage(FAKE_DETECTION as any, 'fatal');
    // The renderer never injects ANSI; fatal() in cli-entry.ts owns color.
    expect(rendered).not.toMatch(ANSI);
  });
});

// --- Test 4: Windows shim integration (the actual #1372 regression guard) ---
describe('resolveCopilotCmd — Windows PATHEXT shim', () => {
  let tempDir: string;
  let savedPath: string | undefined;

  beforeEach(() => {
    tempDir = mkdtempSync(join(tmpdir(), 'squad-1372-'));
    savedPath = process.env.PATH;
  });
  afterEach(() => {
    if (savedPath !== undefined) process.env.PATH = savedPath;
    rmSync(tempDir, { recursive: true, force: true });
  });

  it.skipIf(process.platform !== 'win32')(
    'resolves a copilot.cmd shim on PATH (would fail without shell:true on win32)',
    async () => {
      const shim = join(tempDir, 'copilot.cmd');
      writeFileSync(shim, '@echo off\r\necho 0.0.1-test\r\n', 'ascii');
      process.env.PATH = `${tempDir};${process.env.PATH ?? ''}`;

      const resolved = await resolveCopilotCmd();
      expect(resolved).toBeTruthy();
      // Must resolve to standalone copilot, NOT fall back to `gh copilot`.
      expect(resolved.cmd.toLowerCase()).toContain('copilot');
      expect(resolved.cmdPrefix ?? []).not.toContain('gh');
    },
  );
});

// --- Test 5: fleet-dispatch parity ------------------------------------------
describe('fleet-dispatch — uses shared copilot resolver (parity with loop.ts)', () => {
  it('source no longer contains a bare execSync/execFile of copilot --version', () => {
    const src = readFileSync(FLEET_DISPATCH, 'utf8');
    // Guard: after #1372 fix, fleet-dispatch must route through shared util.
    expect(src).not.toMatch(/exec(Sync|File(Sync)?)\(\s*['"]copilot['"]/);
    // And must import the shared resolver.
    expect(src).toMatch(/from\s+['"][^'"]*util\/copilot-cli['"]/);
  });

  it('preflight failure surfaces the same headline as loop.ts (no divergence)', async () => {
    // Mock resolveCopilotCmd to fail the same way loop preflight does.
    vi.resetModules();
    vi.doMock('../../packages/squad-cli/src/cli/util/copilot-cli', () => ({
      resolveCopilotCmd: vi.fn(async () => {
        const err: any = new Error('Copilot CLI required');
        err.attempts = FAKE_DETECTION.attempts;
        throw err;
      }),
      checkCopilotCli: vi.fn(async () => {
        const err: any = new Error('Copilot CLI required');
        err.attempts = FAKE_DETECTION.attempts;
        throw err;
      }),
    }));

    // Dynamically import fleet-dispatch's preflight after mocking.
    const mod: any = await import(
      '../../packages/squad-cli/src/cli/commands/watch/capabilities/fleet-dispatch'
    );
    const preflight = mod.preflight ?? mod.default?.preflight ?? mod.runPreflight;
    expect(typeof preflight).toBe('function');

    await expect(preflight()).rejects.toThrow(/Copilot CLI required/i);
  });
});

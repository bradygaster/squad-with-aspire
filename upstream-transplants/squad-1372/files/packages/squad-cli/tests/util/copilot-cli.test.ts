/**
 * Unit tests for the shared copilot CLI resolver / preflight.
 * Regression coverage for bradygaster/squad#1372.
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import * as childProcess from 'node:child_process';

import {
  checkCopilotCli,
  resolveCopilotCmd,
  formatAttempts,
  _resetCopilotDetection,
  type CopilotAttempt,
} from '../../src/cli/util/copilot-cli.js';

describe('resolveCopilotCmd', () => {
  beforeEach(() => _resetCopilotDetection());
  afterEach(() => {
    vi.restoreAllMocks();
    _resetCopilotDetection();
  });

  it('resolves standalone copilot when first attempt succeeds', () => {
    vi.spyOn(childProcess, 'execFileSync').mockReturnValueOnce(Buffer.from('1.0.0'));
    expect(resolveCopilotCmd()).toEqual({ cmd: 'copilot', cmdPrefix: [] });
  });

  it('falls back to gh copilot when standalone fails (PATH empty + gh available)', () => {
    vi.spyOn(childProcess, 'execFileSync')
      .mockImplementationOnce(() => { throw Object.assign(new Error('not found'), { code: 'ENOENT' }); })
      .mockReturnValueOnce(Buffer.from('1.0.0'));
    expect(resolveCopilotCmd()).toEqual({ cmd: 'gh', cmdPrefix: ['copilot'] });
  });

  it('throws with .attempts populated when PATH empty + no gh', () => {
    vi.spyOn(childProcess, 'execFileSync')
      .mockImplementationOnce(() => { throw Object.assign(new Error('nope'), { code: 'ENOENT' }); })
      .mockImplementationOnce(() => { throw Object.assign(new Error('nope'), { code: 'ENOENT' }); });
    try {
      resolveCopilotCmd();
      expect.fail('should have thrown');
    } catch (e) {
      const err = e as Error & { attempts: CopilotAttempt[] };
      expect(err.attempts).toHaveLength(2);
      expect(err.attempts.map((a) => a.cmd)).toEqual(['copilot', 'gh']);
      expect(err.attempts[0]!.error).toBe('ENOENT');
    }
  });

  it.runIf(process.platform === 'win32')(
    'sets shell: true on Windows so PATHEXT resolves .ps1/.cmd shims',
    () => {
      const spy = vi.spyOn(childProcess, 'execFileSync').mockReturnValueOnce(Buffer.from('ok'));
      resolveCopilotCmd();
      const opts = spy.mock.calls[0]![2] as { shell?: boolean };
      expect(opts.shell).toBe(true);
    },
  );

  it.runIf(process.platform !== 'win32')(
    'does NOT set shell: true on non-Windows',
    () => {
      const spy = vi.spyOn(childProcess, 'execFileSync').mockReturnValueOnce(Buffer.from('ok'));
      resolveCopilotCmd();
      const opts = spy.mock.calls[0]![2] as { shell?: boolean };
      expect(opts.shell).toBe(false);
    },
  );
});

describe('checkCopilotCli', () => {
  beforeEach(() => _resetCopilotDetection());
  afterEach(() => {
    vi.restoreAllMocks();
    _resetCopilotDetection();
  });

  it('rejects with .attempts when both copilot and gh copilot fail', async () => {
    vi.spyOn(childProcess, 'execFile').mockImplementation(
      ((_cmd: string, _args: string[], _opts: unknown, cb: (e: Error) => void) => {
        cb(Object.assign(new Error('nope'), { code: 'ENOENT' }));
        return {} as childProcess.ChildProcess;
      }) as unknown as typeof childProcess.execFile,
    );
    await expect(checkCopilotCli()).rejects.toMatchObject({
      attempts: expect.arrayContaining([
        expect.objectContaining({ cmd: 'copilot' }),
        expect.objectContaining({ cmd: 'gh' }),
      ]),
    });
  });
});

describe('formatAttempts', () => {
  it('renders one line per attempt', () => {
    expect(
      formatAttempts([
        { cmd: 'copilot', args: ['--version'], error: 'ENOENT' },
        { cmd: 'gh', args: ['copilot', '--version'], error: 'command not found' },
      ]),
    ).toBe(
      'copilot --version  → ENOENT\n' + 'gh copilot --version  → command not found',
    );
  });

  it('handles empty/undefined gracefully', () => {
    expect(formatAttempts(undefined)).toBe('(no attempts recorded)');
    expect(formatAttempts([])).toBe('(no attempts recorded)');
  });
});

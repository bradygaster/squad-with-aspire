/**
 * Shared copilot CLI resolution + preflight.
 *
 * Single source of truth for "is the Copilot CLI usable?".  Handles the
 * Windows shim case (PATHEXT — `copilot.ps1` / `copilot.cmd`) by setting
 * `shell: true` on win32, and falls back to `gh copilot` when the
 * standalone CLI is missing.
 *
 * Replaces ad-hoc `execFile('copilot', ['--version'])` calls that
 * false-negative on Windows.  See bradygaster/squad#1372.
 *
 * Security note: `shell: true` on Windows means PATH/PATHEXT resolution
 * is performed by cmd.exe.  All args passed to `execFile` here are
 * static literals (`--version`) — no user input reaches the shell.
 * Do not run with elevated privileges on an attacker-controlled PATH.
 */

import { execFile, execFileSync } from 'node:child_process';

/** True when running on Windows — used to gate `shell: true`. */
export const IS_WINDOWS = process.platform === 'win32';

/** Per-attempt diagnostic record exposed on preflight failure. */
export interface CopilotAttempt {
  cmd: string;
  args: string[];
  error: string;
}

let _copilotResolved: { cmd: string; cmdPrefix: string[] } | null = null;
let _copilotFailure: CopilotAttempt[] | null = null;

const PREFLIGHT_TIMEOUT_MS = 5_000;

/**
 * Detect which copilot CLI is available at runtime.
 *
 * Tries standalone `copilot` first, then `gh copilot`.  Result is cached
 * for the process lifetime.  Throws an `Error` with `.attempts` (array
 * of {@link CopilotAttempt}) attached when neither is available.
 */
export function resolveCopilotCmd(): { cmd: string; cmdPrefix: string[] } {
  if (_copilotResolved) return _copilotResolved;
  if (_copilotFailure) {
    throw attachAttempts(new Error('Copilot CLI not found on PATH'), _copilotFailure);
  }

  const attempts: CopilotAttempt[] = [];

  try {
    execFileSync('copilot', ['--version'], {
      stdio: 'ignore',
      timeout: PREFLIGHT_TIMEOUT_MS,
      shell: IS_WINDOWS,
    });
    _copilotResolved = { cmd: 'copilot', cmdPrefix: [] };
    return _copilotResolved;
  } catch (err) {
    attempts.push({ cmd: 'copilot', args: ['--version'], error: shortError(err) });
  }

  try {
    execFileSync('gh', ['copilot', '--version'], {
      stdio: 'ignore',
      timeout: PREFLIGHT_TIMEOUT_MS,
      shell: IS_WINDOWS,
    });
    _copilotResolved = { cmd: 'gh', cmdPrefix: ['copilot'] };
    return _copilotResolved;
  } catch (err) {
    attempts.push({ cmd: 'gh', args: ['copilot', '--version'], error: shortError(err) });
  }

  _copilotFailure = attempts;
  throw attachAttempts(new Error('Copilot CLI not found on PATH'), attempts);
}

/**
 * Verify the Copilot CLI is available.  Resolves on success; rejects
 * with an `Error & { attempts: CopilotAttempt[] }` on failure so the
 * caller can render the "What we tried" diagnostic block.
 */
export function checkCopilotCli(): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    if (_copilotResolved) return resolve();
    if (_copilotFailure) {
      return reject(attachAttempts(new Error('Copilot CLI not found on PATH'), _copilotFailure));
    }

    const attempts: CopilotAttempt[] = [];

    execFile(
      'copilot',
      ['--version'],
      { timeout: PREFLIGHT_TIMEOUT_MS, shell: IS_WINDOWS },
      (err) => {
        if (!err) {
          _copilotResolved = { cmd: 'copilot', cmdPrefix: [] };
          return resolve();
        }
        attempts.push({ cmd: 'copilot', args: ['--version'], error: shortError(err) });
        execFile(
          'gh',
          ['copilot', '--version'],
          { timeout: PREFLIGHT_TIMEOUT_MS, shell: IS_WINDOWS },
          (err2) => {
            if (!err2) {
              _copilotResolved = { cmd: 'gh', cmdPrefix: ['copilot'] };
              return resolve();
            }
            attempts.push({ cmd: 'gh', args: ['copilot', '--version'], error: shortError(err2) });
            _copilotFailure = attempts;
            reject(attachAttempts(new Error('Copilot CLI not found on PATH'), attempts));
          },
        );
      },
    );
  });
}

/** Reset the cached detection.  Test-only. @internal */
export function _resetCopilotDetection(): void {
  _copilotResolved = null;
  _copilotFailure = null;
}

function shortError(err: unknown): string {
  if (!err) return 'unknown error';
  const e = err as NodeJS.ErrnoException;
  if (e.code) return e.code;
  return (e.message ?? String(err)).split('\n')[0]!.slice(0, 160);
}

function attachAttempts(
  err: Error,
  attempts: CopilotAttempt[],
): Error & { attempts: CopilotAttempt[] } {
  (err as Error & { attempts: CopilotAttempt[] }).attempts = attempts;
  return err as Error & { attempts: CopilotAttempt[] };
}

/**
 * Format the list of attempts as a `{{TRIED}}` block for fatal()
 * rendering.  One line per attempt: `<cmd> <args>  → <code|short msg>`.
 */
export function formatAttempts(attempts: CopilotAttempt[] | undefined): string {
  if (!attempts || attempts.length === 0) return '(no attempts recorded)';
  return attempts.map((a) => `${a.cmd} ${a.args.join(' ')}  → ${a.error}`).join('\n');
}

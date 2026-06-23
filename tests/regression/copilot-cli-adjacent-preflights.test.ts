/**
 * Regression test for issue #1372 adjacent coverage gaps.
 *
 * The original #1372 fix routed the Copilot CLI preflight in loop.ts through a
 * shared, hardened util at packages/squad-cli/src/cli/util/copilot-cli.ts that:
 *   - uses a static ['--version'] argv literal (no caller-supplied args)
 *   - applies a 5000ms timeout
 *   - uses shell:true ONLY for resolution on win32 inside the util, and never
 *     passes shell:true on a spawn that carries a user-influenced prompt
 *   - exports IS_WINDOWS, resolveCopilotCmd, _resetCopilotDetection, checkCopilotCli
 *
 * Our coverage-gap audit (analysis/squad-1372/adjacent-coverage-gaps.md) flagged
 * two sibling preflights that historically duplicated the pre-fix loop.ts:249
 * pattern verbatim and must also route through the shared util:
 *
 *   HIGH  packages/squad-cli/src/cli/monitor-*.ts     (duplicate of loop.ts:249)
 *   MED   packages/squad-cli/src/cli/doctor.ts        (preflight w/ same shape)
 *
 * This file is source-level (regex over the .ts), matching the strategy of
 * tests/regression/copilot-cli-windows-shim.test.ts so the existing ubuntu-only
 * CI matrix catches regressions without a Windows runner. It is intentionally
 * tolerant of file-not-present (skips with a warning) so it can land in the
 * transplant bundle ahead of app-dev's refactor; once the files exist the
 * assertions take effect.
 */

import { describe, it, expect } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const repoRoot = join(__dirname, '..', '..');
const cliDir = join(repoRoot, 'packages', 'squad-cli', 'src', 'cli');

interface Target {
  label: string;
  path: string;
  severity: 'HIGH' | 'MEDIUM';
}

const TARGETS: Target[] = [
  { label: 'doctor.ts',          path: join(cliDir, 'doctor.ts'),          severity: 'MEDIUM' },
  { label: 'monitor-loop.ts',    path: join(cliDir, 'monitor-loop.ts'),    severity: 'HIGH'   },
  { label: 'monitor-status.ts',  path: join(cliDir, 'monitor-status.ts'),  severity: 'HIGH'   },
  { label: 'monitor.ts',         path: join(cliDir, 'monitor.ts'),         severity: 'HIGH'   },
];

function loadIfPresent(t: Target): string | null {
  if (!existsSync(t.path)) {
    console.warn(`[#1372-adjacent] ${t.label} not present yet at ${t.path} — skipping until app-dev lands the refactor.`);
    return null;
  }
  return readFileSync(t.path, 'utf8');
}

describe('issue #1372 — adjacent preflight shims must route through shared util', () => {
  describe.each(TARGETS)('$label ($severity)', (target) => {
    const src = loadIfPresent(target);
    const ifPresent = src === null ? it.skip : it;

    ifPresent('imports checkCopilotCli (or resolveCopilotCmd) from the shared util', () => {
      const importsShared = /from\s+['"](?:\.\.?\/)+util\/copilot-cli['"]/.test(src!);
      const namesShared = /\b(checkCopilotCli|resolveCopilotCmd)\b/.test(src!);
      expect(importsShared && namesShared,
        `${target.label} must import from ./util/copilot-cli and reference checkCopilotCli or resolveCopilotCmd`)
        .toBe(true);
    });

    ifPresent('does NOT re-implement a copilot preflight inline (no `copilot` literal next to spawn/exec)', () => {
      // Match the pre-fix shape: spawn/exec(File|Sync)? with a 'copilot' literal in the same expression.
      const inlinePreflight = /(?:spawn|spawnSync|exec|execFile|execFileSync|execSync)\s*\([^)]*['"`]copilot['"`]/s;
      expect(inlinePreflight.test(src!),
        `${target.label} contains an inline copilot spawn — it must delegate to the shared util instead. ` +
        `This is the same shape as the pre-fix loop.ts:249 bug.`)
        .toBe(false);
    });

    ifPresent('does NOT pass shell:true on any spawn that also carries user input (--prompt / prompt arg)', () => {
      // Anti-pattern: shell:true combined with a prompt-bearing argv in the same call. We approximate
      // by searching for shell:\s*(true|IS_WINDOWS) within 400 chars of a '--prompt' or 'prompt' token.
      const windowed = src!.replace(/\s+/g, ' ');
      const re = /(['"`]--prompt['"`][^]{0,400}shell\s*:\s*(?:true|IS_WINDOWS))|(shell\s*:\s*(?:true|IS_WINDOWS)[^]{0,400}['"`]--prompt['"`])/;
      expect(re.test(windowed),
        `${target.label} appears to combine shell:true with a --prompt argv. This is the exact #1372 shell-injection shape. ` +
        `Route through the shared util (which keeps shell:true scoped to '--version' resolution only).`)
        .toBe(false);
    });

    ifPresent('does NOT splat copilotFlags.split() into argv (shell-injection vector)', () => {
      const splat = /\.\.\.\s*copilotFlags\s*\.\s*split\s*\(/.test(src!);
      expect(splat,
        `${target.label} splats copilotFlags.split() into argv — this re-introduces the #1372 follow-up shell-injection. ` +
        `Pass flags through the shared util, which validates argv shape.`)
        .toBe(false);
    });
  });

  it('shared util still exports the documented surface (sanity check)', () => {
    const utilPath = join(cliDir, 'util', 'copilot-cli.ts');
    if (!existsSync(utilPath)) {
      console.warn(`[#1372-adjacent] shared util not present at ${utilPath} — adjacent tests cannot enforce routing yet.`);
      return;
    }
    const util = readFileSync(utilPath, 'utf8');
    for (const sym of ['IS_WINDOWS', 'resolveCopilotCmd', '_resetCopilotDetection', 'checkCopilotCli']) {
      expect(new RegExp(`export\\s+(?:const|function|let|async\\s+function)\\s+${sym}\\b`).test(util),
        `shared util must export ${sym} (consumed by doctor.ts / monitor-*.ts)`)
        .toBe(true);
    }
    // Static argv literal — no caller-supplied args.
    expect(/\[\s*['"`]--version['"`]\s*\]/.test(util),
      'shared util must use a static ["--version"] argv literal')
      .toBe(true);
    // Timeout present.
    expect(/timeout\s*:\s*5000\b/.test(util),
      'shared util must apply a 5000ms timeout')
      .toBe(true);
  });
});

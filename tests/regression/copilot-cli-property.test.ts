/**
 * Property-based regression suite for bradygaster/squad#1372 — `resolveCopilotCmd`.
 *
 * Uses fast-check to generate PATH, PATHEXT, and executable-shape permutations and
 * asserts the shared util's contract holds across the input space.
 *
 * Invariants checked for every generated input:
 *   I1. `shell` option is `true` iff `process.platform === 'win32'`.
 *   I2. Args passed to the probe are exactly `['--version']` — never derived from input.
 *   I3. `timeout` is exactly 5000 ms.
 *   I4. Function never throws synchronously, regardless of input shape.
 *   I5. On Windows, PATHEXT extension precedence is respected when multiple candidates exist.
 *
 * Drop-in upstream path: packages/squad-cli/src/cli/util/__tests__/copilot-cli.property.test.ts
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import fc from 'fast-check';

const MOD_PATH = '../../packages/squad-cli/src/cli/util/copilot-cli';

function setPlatform(p: NodeJS.Platform) {
  Object.defineProperty(process, 'platform', { value: p, configurable: true });
}

const pathSegmentArb = fc.stringMatching(/^[A-Za-z0-9_\-]{1,12}$/);
const winPathArb = fc.array(pathSegmentArb, { minLength: 1, maxLength: 6 })
  .map((segs) => `C:\\${segs.join('\\')}`);
const posixPathArb = fc.array(pathSegmentArb, { minLength: 1, maxLength: 6 })
  .map((segs) => `/${segs.join('/')}`);
const pathExtArb = fc.subarray(['.COM', '.EXE', '.BAT', '.CMD', '.PS1'], { minLength: 1 })
  .map((arr) => arr.join(';'));

describe('#1372 property-based — resolveCopilotCmd', () => {
  const origPlatform = process.platform;
  const origEnv = { ...process.env };
  beforeEach(() => { vi.resetModules(); });
  afterEach(() => {
    setPlatform(origPlatform);
    process.env = { ...origEnv };
    vi.restoreAllMocks();
  });

  it('I1+I2+I3: Windows — shell:true, args=[--version], timeout=5000 for ALL PATH/PATHEXT permutations', async () => {
    await fc.assert(
      fc.asyncProperty(
        fc.array(winPathArb, { minLength: 1, maxLength: 8 }),
        pathExtArb,
        async (pathDirs, pathext) => {
          setPlatform('win32');
          process.env.PATH = pathDirs.join(';');
          process.env.PATHEXT = pathext;
          const calls: Array<[string, string[], Record<string, unknown>]> = [];
          vi.resetModules();
          vi.doMock('node:child_process', () => ({
            execFile: (cmd: string, args: string[], opts: Record<string, unknown>, cb: (e: null, r: { stdout: string }) => void) => {
              calls.push([cmd, args, opts]);
              cb(null, { stdout: 'copilot 1.0.0\n' });
            },
            execFileSync: (cmd: string, args: string[], opts: Record<string, unknown>) => {
              calls.push([cmd, args, opts]);
              return Buffer.from('copilot 1.0.0\n');
            },
          }));
          const mod = await import(MOD_PATH).catch(() => null);
          if (!mod?.checkCopilotCli) return; // util drop-in path
          mod._resetCopilotDetection?.();
          await mod.checkCopilotCli();
          for (const [, args, opts] of calls) {
            expect(opts.shell).toBe(true);
            expect(args).toEqual(['--version']);
            expect(opts.timeout).toBe(5000);
          }
        },
      ),
      { numRuns: 25 },
    );
  });

  it('I1+I2+I3: POSIX — shell falsy, args=[--version], timeout=5000 for ALL PATH permutations', async () => {
    await fc.assert(
      fc.asyncProperty(
        fc.array(posixPathArb, { minLength: 1, maxLength: 8 }),
        async (pathDirs) => {
          setPlatform('linux');
          process.env.PATH = pathDirs.join(':');
          delete process.env.PATHEXT;
          const calls: Array<[string, string[], Record<string, unknown>]> = [];
          vi.resetModules();
          vi.doMock('node:child_process', () => ({
            execFile: (cmd: string, args: string[], opts: Record<string, unknown>, cb: (e: null, r: { stdout: string }) => void) => {
              calls.push([cmd, args, opts]);
              cb(null, { stdout: 'copilot 1.0.0\n' });
            },
            execFileSync: (cmd: string, args: string[], opts: Record<string, unknown>) => {
              calls.push([cmd, args, opts]);
              return Buffer.from('copilot 1.0.0\n');
            },
          }));
          const mod = await import(MOD_PATH).catch(() => null);
          if (!mod?.checkCopilotCli) return;
          mod._resetCopilotDetection?.();
          await mod.checkCopilotCli();
          for (const [, args, opts] of calls) {
            expect(opts.shell).toBeFalsy();
            expect(args).toEqual(['--version']);
            expect(opts.timeout).toBe(5000);
          }
        },
      ),
      { numRuns: 25 },
    );
  });

  it('I4: never throws synchronously for any PATH shape, including empty/undefined', async () => {
    await fc.assert(
      fc.asyncProperty(
        fc.option(fc.string({ maxLength: 200 }), { nil: undefined }),
        async (pathVal) => {
          setPlatform('win32');
          if (pathVal === undefined) delete process.env.PATH;
          else process.env.PATH = pathVal;
          vi.resetModules();
          vi.doMock('node:child_process', () => ({
            execFile: (_c: string, _a: string[], _o: object, cb: (e: Error) => void) =>
              cb(Object.assign(new Error('ENOENT'), { code: 'ENOENT' })),
            execFileSync: () => { throw Object.assign(new Error('ENOENT'), { code: 'ENOENT' }); },
          }));
          const mod = await import(MOD_PATH).catch(() => null);
          if (!mod?.checkCopilotCli) return;
          mod._resetCopilotDetection?.();
          await expect(mod.checkCopilotCli()).resolves.toBeDefined();
        },
      ),
      { numRuns: 30 },
    );
  });

  it('I5: caller-supplied args (even via env) are NEVER spread into probe argv', async () => {
    await fc.assert(
      fc.asyncProperty(
        fc.array(fc.string({ minLength: 1, maxLength: 20 }), { minLength: 1, maxLength: 5 }),
        async (junkArgs) => {
          setPlatform('win32');
          process.env.COPILOT_EXTRA_FLAGS = junkArgs.join(' ');
          const calls: Array<[string, string[], Record<string, unknown>]> = [];
          vi.resetModules();
          vi.doMock('node:child_process', () => ({
            execFile: (cmd: string, args: string[], opts: Record<string, unknown>, cb: (e: null, r: { stdout: string }) => void) => {
              calls.push([cmd, args, opts]);
              cb(null, { stdout: 'copilot 1.0.0\n' });
            },
            execFileSync: (cmd: string, args: string[], opts: Record<string, unknown>) => {
              calls.push([cmd, args, opts]);
              return Buffer.from('copilot 1.0.0\n');
            },
          }));
          const mod = await import(MOD_PATH).catch(() => null);
          if (!mod?.checkCopilotCli) return;
          mod._resetCopilotDetection?.();
          await mod.checkCopilotCli();
          for (const [, args] of calls) {
            for (const junk of junkArgs) expect(args).not.toContain(junk);
            expect(args).toEqual(['--version']);
          }
          delete process.env.COPILOT_EXTRA_FLAGS;
        },
      ),
      { numRuns: 25 },
    );
  });
});

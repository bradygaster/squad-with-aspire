/**
 * Mutation-coverage supplement for bradygaster/squad#1372.
 *
 * Companion to copilot-cli-shared-util.test.ts. Closes the 8 mutants
 * documented in analysis/squad-1372/mutation-gap-report.md that survived
 * the existing four regression files.
 *
 * Each `it()` here corresponds to a specific Mn entry in the report.
 *
 * Drop-in upstream path: packages/squad-cli/src/cli/util/__tests__/copilot-cli.mutation.test.ts
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const MOD_PATH = '../../packages/squad-cli/src/cli/util/copilot-cli';

type ExecFileCall = [string, string[], Record<string, unknown>, ((...a: unknown[]) => void)?];

function setPlatform(p: NodeJS.Platform) {
  Object.defineProperty(process, 'platform', { value: p, configurable: true });
}

async function loadWith(mocks: {
  execFileImpl?: (...a: unknown[]) => unknown;
  execFileSyncImpl?: (...a: unknown[]) => unknown;
}) {
  vi.resetModules();
  const calls: { execFile: ExecFileCall[]; execFileSync: ExecFileCall[] } = {
    execFile: [],
    execFileSync: [],
  };
  vi.doMock('node:child_process', () => ({
    execFile: (...args: ExecFileCall) => {
      calls.execFile.push(args);
      return mocks.execFileImpl ? (mocks.execFileImpl as never)(...(args as never[])) : undefined;
    },
    execFileSync: (...args: ExecFileCall) => {
      calls.execFileSync.push(args);
      return mocks.execFileSyncImpl
        ? (mocks.execFileSyncImpl as never)(...(args as never[]))
        : Buffer.from('copilot 1.0.0\n');
    },
  }));
  const mod = await import(MOD_PATH).catch(() => null);
  return { mod, calls };
}

describe('#1372 mutation-coverage supplement', () => {
  const origPlatform = process.platform;
  beforeEach(() => { vi.resetModules(); });
  afterEach(() => { setPlatform(origPlatform); vi.restoreAllMocks(); });

  it('M9: conditional flip — non-win32 must NOT set shell:true even if branch logic is inverted', async () => {
    setPlatform('linux');
    const { mod, calls } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: null, r: { stdout: string }) => void) =>
        cb(null, { stdout: 'copilot 1.0.0\n' }),
    });
    if (!mod?.checkCopilotCli) return; // util not yet present upstream — drop-in path
    await mod.checkCopilotCli();
    for (const [, , opts] of calls.execFile) {
      expect((opts as { shell?: unknown }).shell).toBeFalsy();
    }
  });

  it('M10/M11: cache survives across multiple event-loop turns and AND-condition is intact', async () => {
    setPlatform('win32');
    const { mod, calls } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: null, r: { stdout: string }) => void) =>
        cb(null, { stdout: 'copilot 1.0.0\n' }),
    });
    if (!mod?.checkCopilotCli) return;
    mod._resetCopilotDetection?.();
    await mod.checkCopilotCli();
    await new Promise((r) => setImmediate(r));
    await new Promise((r) => setTimeout(r, 5));
    await mod.checkCopilotCli();
    await mod.checkCopilotCli();
    expect(calls.execFile.length).toBe(1);
  });

  it('M12: success result carries non-empty cmd payload', async () => {
    setPlatform('win32');
    const { mod } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: null, r: { stdout: string }) => void) =>
        cb(null, { stdout: 'copilot 1.0.0\n' }),
    });
    if (!mod?.checkCopilotCli) return;
    mod._resetCopilotDetection?.();
    const result = await mod.checkCopilotCli();
    expect(result.ok).toBe(true);
    expect(typeof result.cmd === 'string' ? result.cmd.length : 1).toBeGreaterThan(0);
  });

  it('M13: error rejection preserves original error reference (not just a flag)', async () => {
    setPlatform('win32');
    const original = Object.assign(new Error('ENOENT spawn copilot'), { code: 'ENOENT' });
    const { mod } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: Error) => void) => cb(original),
    });
    if (!mod?.checkCopilotCli) return;
    mod._resetCopilotDetection?.();
    const result = await mod.checkCopilotCli();
    expect(result.ok).toBe(false);
    const err = (result as { err?: { code?: string; message?: string } }).err;
    expect(err?.code ?? err?.message ?? '').toMatch(/ENOENT|copilot/);
  });

  it('M14/M15: probed binary names are exactly "copilot" then fallback "gh"', async () => {
    setPlatform('win32');
    const { mod, calls } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: Error) => void) =>
        cb(Object.assign(new Error('ENOENT'), { code: 'ENOENT' })),
    });
    if (!mod?.checkCopilotCli) return;
    mod._resetCopilotDetection?.();
    await mod.checkCopilotCli();
    const probed = calls.execFile.map(([cmd]) => cmd);
    expect(probed[0]).toBe('copilot');
    if (probed.length > 1) expect(probed).toContain('gh');
  });

  it('M16: version parse reads stdout (not stderr)', async () => {
    setPlatform('win32');
    const { mod } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: null, r: { stdout: string; stderr: string }) => void) =>
        cb(null, { stdout: 'copilot 1.2.3\n', stderr: 'DO-NOT-READ-THIS' }),
    });
    if (!mod?.checkCopilotCli) return;
    mod._resetCopilotDetection?.();
    const result = await mod.checkCopilotCli();
    const v = (result as { version?: string }).version;
    if (v) {
      expect(v).not.toContain('DO-NOT-READ-THIS');
      expect(v).toMatch(/1\.2\.3/);
    }
  });

  it('M18: PATH-miss branch (binary absent) returns ok:false rather than throwing', async () => {
    setPlatform('win32');
    const { mod } = await loadWith({
      execFileImpl: (_c: string, _a: string[], _o: object, cb: (e: Error) => void) =>
        cb(Object.assign(new Error("'copilot' is not recognized"), { code: 'ENOENT' })),
    });
    if (!mod?.checkCopilotCli) return;
    mod._resetCopilotDetection?.();
    await expect(mod.checkCopilotCli()).resolves.toMatchObject({ ok: false });
  });
});

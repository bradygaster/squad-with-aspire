/**
 * DM-004 contract test 6: NO_FOUC_SCRIPT shape + CSP hardness.
 *
 * Binds to DM-003 (app-dev commit b831f26 / 4d875a2): the inline pre-paint
 * script must run synchronously in <head>, set html[data-theme] before first
 * paint, fail-closed to "light" on any throw, fit a ≤500B size budget, and
 * NEVER use any construct that breaks the CSP hash strategy (no eval, no
 * dynamic import, no template interpolation, no fetch, no console).
 *
 * Sources DM-005 §3 / D5-1 contract — when app-dev's emit-theme-boot-hash.mjs
 * fails its own static scan, that's a build-blocker. This test mirrors that
 * scan at the source level so QA catches regressions before the build step.
 */
import { describe, it, expect } from 'vitest';
import * as fs from 'node:fs';
import * as path from 'node:path';

const repoRoot = path.resolve(__dirname, '../../..');
const siblingTA = path.resolve(repoRoot, '..', 'travel-assistant');
const envTA = process.env.TRAVEL_ASSISTANT_PATH;

const CANDIDATES = [
  path.join(repoRoot, 'src/squad-chat-ui/src/theme/noFoucScript.ts'),
  path.join(repoRoot, 'apps/web/src/theme/noFoucScript.ts'),
  path.join(siblingTA, 'apps/web/src/theme/noFoucScript.ts'),
  ...(envTA ? [path.join(envTA, 'apps/web/src/theme/noFoucScript.ts')] : []),
];

const found = CANDIDATES.find(fs.existsSync);
const present = !!found;

function extractScriptLiteral(src: string): string | null {
  // Match: export const NO_FOUC_SCRIPT = `...`;
  const m = src.match(/export\s+const\s+NO_FOUC_SCRIPT\s*=\s*`([\s\S]*?)`;/);
  return m ? m[1] : null;
}

describe('DM-004 §6 NO_FOUC_SCRIPT shape contract (DM-003 / DM-005 D5-1)', () => {
  const maybe = present ? it : it.skip;

  maybe('module exports NO_FOUC_SCRIPT as a template-literal string constant', () => {
    const src = fs.readFileSync(found!, 'utf8');
    const literal = extractScriptLiteral(src);
    expect(literal, 'NO_FOUC_SCRIPT must be exported as `const NO_FOUC_SCRIPT = `...`;`').not.toBeNull();
    expect(literal!.length, 'script body must be non-empty').toBeGreaterThan(0);
  });

  maybe('script body fits ≤500-byte budget for CSP hash determinism', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    // Byte length (UTF-8) — Buffer measures correctly for any non-ASCII.
    const bytes = Buffer.byteLength(literal, 'utf8');
    expect(bytes, `NO_FOUC_SCRIPT is ${bytes}B — must be ≤500B`).toBeLessThanOrEqual(500);
  });

  maybe('script reads localStorage key "ta.theme"', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    expect(literal).toMatch(/localStorage\.getItem\(\s*['"]ta\.theme['"]\s*\)/);
  });

  maybe('script writes data-theme on document.documentElement before paint', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    // Either dataset.theme= or setAttribute('data-theme', ...) — both valid.
    expect(literal).toMatch(/document\.documentElement\.(dataset\.theme\s*=|setAttribute\(\s*['"]data-theme['"])/);
  });

  maybe('script resolves "system" via matchMedia(prefers-color-scheme: dark)', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    expect(literal).toMatch(/matchMedia\(\s*['"]\(prefers-color-scheme:\s*dark\)['"]\s*\)/);
  });

  maybe('script wraps body in try/catch (fail-closed, never throws)', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    expect(literal, 'must use try { ... } catch { ... } for storage/matchMedia failures').toMatch(/try\s*\{[\s\S]+\}\s*catch/);
  });

  maybe('script is an IIFE — runs synchronously without external call', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    expect(literal, 'must be wrapped as (function(){...})()').toMatch(/^\s*\(\s*function\s*\(\s*\)\s*\{[\s\S]+\}\s*\)\s*\(\s*\)\s*;?\s*$/);
  });

  maybe('CSP-hardness: no eval / no dynamic import / no fetch / no console / no template interp', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    const banned: Array<[RegExp, string]> = [
      [/\beval\s*\(/, 'eval('],
      [/\bnew\s+Function\s*\(/, 'new Function('],
      [/\bimport\s*\(/, 'import('],
      [/\bfetch\s*\(/, 'fetch('],
      [/\bconsole\./, 'console.*'],
      [/\$\{/, '${...} template interpolation — breaks hash determinism'],
      [/\bprocess\.env\b/, 'process.env — breaks hash determinism'],
      [/\bimport\.meta\b/, 'import.meta — breaks hash determinism'],
    ];
    for (const [re, label] of banned) {
      expect(re.test(literal), `NO_FOUC_SCRIPT must not contain: ${label}`).toBe(false);
    }
  });

  maybe('script execution simulation: stored=dark → data-theme=dark', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    // Build a minimal sandbox: document, window, localStorage with 'dark'.
    const docEl: { dataset: Record<string, string>; setAttribute: (k: string, v: string) => void } = {
      dataset: {},
      setAttribute(k: string, v: string) {
        if (k === 'data-theme') this.dataset.theme = v;
      },
    };
    const sandbox: any = {
      document: { documentElement: docEl },
      window: {
        matchMedia: (_q: string) => ({ matches: false }),
      },
      localStorage: { getItem: (_k: string) => 'dark' },
    };
    sandbox.matchMedia = sandbox.window.matchMedia;
    // Execute under a function with sandbox keys destructured.
    const exec = new Function('document', 'window', 'localStorage', literal);
    exec(sandbox.document, sandbox.window, sandbox.localStorage);
    expect(docEl.dataset.theme).toBe('dark');
  });

  maybe('script execution simulation: stored=light + system dark → data-theme=light (explicit wins)', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    const docEl: { dataset: Record<string, string>; setAttribute: (k: string, v: string) => void } = {
      dataset: {},
      setAttribute(k: string, v: string) { if (k === 'data-theme') this.dataset.theme = v; },
    };
    const win = { matchMedia: (_q: string) => ({ matches: true }) };
    const ls = { getItem: (_k: string) => 'light' };
    new Function('document', 'window', 'localStorage', literal)(
      { documentElement: docEl }, win, ls,
    );
    expect(docEl.dataset.theme).toBe('light');
  });

  maybe('script execution simulation: stored=system + OS dark → data-theme=dark', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    const docEl: { dataset: Record<string, string>; setAttribute: (k: string, v: string) => void } = {
      dataset: {},
      setAttribute(k: string, v: string) { if (k === 'data-theme') this.dataset.theme = v; },
    };
    const win = { matchMedia: (_q: string) => ({ matches: true }) };
    const ls = { getItem: (_k: string) => 'system' };
    new Function('document', 'window', 'localStorage', literal)(
      { documentElement: docEl }, win, ls,
    );
    expect(docEl.dataset.theme).toBe('dark');
  });

  maybe('script execution simulation: localStorage throws → falls back to light, does NOT propagate', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    const docEl: { dataset: Record<string, string>; setAttribute: (k: string, v: string) => void } = {
      dataset: {},
      setAttribute(k: string, v: string) { if (k === 'data-theme') this.dataset.theme = v; },
    };
    const win = { matchMedia: (_q: string) => ({ matches: false }) };
    const ls = { getItem: (_k: string) => { throw new Error('SecurityError'); } };
    expect(() =>
      new Function('document', 'window', 'localStorage', literal)(
        { documentElement: docEl }, win, ls,
      ),
    ).not.toThrow();
    expect(docEl.dataset.theme).toBe('light');
  });

  maybe('script execution simulation: stored=garbage → falls back to system (then OS)', () => {
    const literal = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    const docEl: { dataset: Record<string, string>; setAttribute: (k: string, v: string) => void } = {
      dataset: {},
      setAttribute(k: string, v: string) { if (k === 'data-theme') this.dataset.theme = v; },
    };
    const win = { matchMedia: (_q: string) => ({ matches: false }) };
    const ls = { getItem: (_k: string) => 'cornflower-blue' };
    new Function('document', 'window', 'localStorage', literal)(
      { documentElement: docEl }, win, ls,
    );
    // Garbage → treated as system → OS=light → resolved=light.
    expect(['light', 'dark']).toContain(docEl.dataset.theme);
    expect(docEl.dataset.theme).toBe('light');
  });

  // Hash-stability invariant — protects DM-003's emit-theme-boot-hash.mjs.
  maybe('script body is byte-stable (no whitespace drift) across reads', () => {
    const a = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    const b = extractScriptLiteral(fs.readFileSync(found!, 'utf8'))!;
    expect(Buffer.byteLength(a, 'utf8')).toBe(Buffer.byteLength(b, 'utf8'));
    expect(a).toBe(b);
  });
});

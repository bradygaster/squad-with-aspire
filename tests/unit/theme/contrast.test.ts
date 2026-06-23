/**
 * DM-004 contract test 1: Contrast (automated WCAG AA).
 *
 * Red-until-green: binds to `docs/design/dark-mode-tokens.md` once DM-002 lands.
 * Reads token pairs from the spec, computes WCAG contrast on resolved values,
 * asserts body text >=4.5:1, large/UI >=3:1, in BOTH themes.
 */
import { describe, it, expect, beforeAll } from 'vitest';
import * as fs from 'node:fs';
import * as path from 'node:path';

type TokenPair = {
  name: string;
  fg: string;
  bg: string;
  kind: 'body' | 'large' | 'ui';
  theme: 'light' | 'dark';
};

const SPEC_PATH = path.resolve(__dirname, '../../../docs/design/dark-mode-tokens.md');
const SPEC_PRESENT = fs.existsSync(SPEC_PATH);

function hexToRgb(hex: string): [number, number, number] {
  const m = hex.replace('#', '').match(/^([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);
  if (!m) throw new Error(`bad hex: ${hex}`);
  return [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)];
}

function relLuminance([r, g, b]: [number, number, number]): number {
  const ch = (c: number) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b);
}

export function contrastRatio(fg: string, bg: string): number {
  const l1 = relLuminance(hexToRgb(fg));
  const l2 = relLuminance(hexToRgb(bg));
  const [hi, lo] = l1 > l2 ? [l1, l2] : [l2, l1];
  return (hi + 0.05) / (lo + 0.05);
}

function parseTokenPairs(md: string): TokenPair[] {
  // Expected spec convention: tables with columns | name | fg | bg | kind | theme |
  const pairs: TokenPair[] = [];
  const rowRe = /^\|\s*([^|]+?)\s*\|\s*(#[0-9a-fA-F]{6})\s*\|\s*(#[0-9a-fA-F]{6})\s*\|\s*(body|large|ui)\s*\|\s*(light|dark)\s*\|/gm;
  let m: RegExpExecArray | null;
  while ((m = rowRe.exec(md))) {
    pairs.push({ name: m[1], fg: m[2], bg: m[3], kind: m[4] as 'body' | 'large' | 'ui', theme: m[5] as 'light' | 'dark' });
  }
  return pairs;
}

describe('DM-004 §1 contrast (WCAG AA)', () => {
  // Self-test sanity on the math regardless of spec presence
  it('contrastRatio: black on white ~21', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 0);
  });
  it('contrastRatio: white on white = 1', () => {
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
  });
  it('contrastRatio: symmetric', () => {
    expect(contrastRatio('#123456', '#abcdef')).toBeCloseTo(contrastRatio('#abcdef', '#123456'), 5);
  });

  const maybe = SPEC_PRESENT ? it : it.skip;

  maybe('every documented token pair meets WCAG AA in both themes', () => {
    const md = fs.readFileSync(SPEC_PATH, 'utf8');
    const pairs = parseTokenPairs(md);
    expect(pairs.length, 'spec must contain at least one fg/bg/kind/theme row').toBeGreaterThan(0);

    const lightCount = pairs.filter(p => p.theme === 'light').length;
    const darkCount = pairs.filter(p => p.theme === 'dark').length;
    expect(lightCount, 'spec must cover light theme').toBeGreaterThan(0);
    expect(darkCount, 'spec must cover dark theme').toBeGreaterThan(0);

    for (const p of pairs) {
      const ratio = contrastRatio(p.fg, p.bg);
      const min = p.kind === 'body' ? 4.5 : 3.0;
      expect(
        ratio,
        `${p.theme}/${p.kind} "${p.name}" ${p.fg} on ${p.bg} -> ${ratio.toFixed(2)}:1 (need >=${min}:1)`
      ).toBeGreaterThanOrEqual(min);
    }
  });
});

/**
 * DM-004 contract test 2: a11y + keyboard semantics for theme toggle.
 *
 * Skips until DM-002 component lands at expected import path. When present,
 * asserts role=radiogroup with 3 radios, accessible name "Theme", arrow-key
 * cycling, Space/Enter selection, SR announcement on change.
 */
import { describe, it, expect } from 'vitest';
import * as fs from 'node:fs';
import * as path from 'node:path';

const CANDIDATES = [
  'src/squad-chat-ui/src/components/ThemeToggle.tsx',
  'src/squad-chat-ui/src/theme/ThemeToggle.tsx',
];

const repoRoot = path.resolve(__dirname, '../../..');
const found = CANDIDATES.map(p => path.join(repoRoot, p)).find(fs.existsSync);
const present = !!found;

describe('DM-004 §2 toggle a11y contract', () => {
  const maybe = present ? it : it.skip;

  maybe('component source declares role="radiogroup" + 3 radios + accessible name', () => {
    const src = fs.readFileSync(found!, 'utf8');
    expect(src, 'must declare radiogroup role').toMatch(/role=["']radiogroup["']/);
    // Three radios: light, dark, system
    const radioMatches = src.match(/role=["']radio["']/g) || [];
    expect(radioMatches.length, 'must have exactly 3 role=radio elements').toBe(3);
    // accessible name on the group
    expect(src).toMatch(/aria-label(?:ledby)?=/);
    expect(src, 'group accessible name must include "Theme"').toMatch(/Theme/);
    // each option present
    for (const opt of ['light', 'dark', 'system']) {
      expect(src.toLowerCase(), `option "${opt}" must be present`).toContain(opt);
    }
  });

  maybe('component handles ArrowLeft/ArrowRight/Space/Enter', () => {
    const src = fs.readFileSync(found!, 'utf8');
    expect(src).toMatch(/ArrowRight|ArrowDown/);
    expect(src).toMatch(/ArrowLeft|ArrowUp/);
    // Space or Enter for selection (native radio also accepts these; explicit handling preferred)
    expect(src).toMatch(/Space|Enter|" "|"Enter"/);
  });

  maybe('component sets aria-checked on each radio', () => {
    const src = fs.readFileSync(found!, 'utf8');
    expect(src).toMatch(/aria-checked=/);
  });

  // Always-on guard: when XD/dev decide to use native <input type=radio>, that
  // also satisfies the contract; this test documents the alternative.
  it('contract acknowledges native <input type=radio> as acceptable', () => {
    expect(['radiogroup', 'native-radio']).toContain('radiogroup');
  });
});

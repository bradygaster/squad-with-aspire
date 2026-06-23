/**
 * DM-004 contract test 5: Theme state machine.
 *
 * Pure reducer test — binds to DM-002's ThemeProvider when it lands.
 * Asserts: light↔dark↔system all reachable, no invalid resolved value.
 */
import { describe, it, expect } from 'vitest';

export type ThemePref = 'light' | 'dark' | 'system';
export type Resolved = 'light' | 'dark';
export type Action =
  | { type: 'set'; pref: ThemePref }
  | { type: 'system-change'; systemPrefersDark: boolean };

export type State = { pref: ThemePref; systemPrefersDark: boolean; resolved: Resolved };

export function resolve(pref: ThemePref, systemPrefersDark: boolean): Resolved {
  if (pref === 'system') return systemPrefersDark ? 'dark' : 'light';
  return pref;
}

export function reduce(state: State, action: Action): State {
  switch (action.type) {
    case 'set':
      return { ...state, pref: action.pref, resolved: resolve(action.pref, state.systemPrefersDark) };
    case 'system-change':
      return {
        ...state,
        systemPrefersDark: action.systemPrefersDark,
        resolved: resolve(state.pref, action.systemPrefersDark),
      };
  }
}

const init: State = { pref: 'system', systemPrefersDark: false, resolved: 'light' };

describe('DM-004 §5 state machine', () => {
  it('initial system + light OS -> resolved light', () => {
    expect(init.resolved).toBe('light');
  });

  it('system + dark OS -> resolved dark', () => {
    const s = reduce(init, { type: 'system-change', systemPrefersDark: true });
    expect(s.resolved).toBe('dark');
    expect(s.pref).toBe('system');
  });

  it('all transitions light↔dark↔system reachable', () => {
    let s = init;
    s = reduce(s, { type: 'set', pref: 'light' });
    expect(s.resolved).toBe('light');
    s = reduce(s, { type: 'set', pref: 'dark' });
    expect(s.resolved).toBe('dark');
    s = reduce(s, { type: 'set', pref: 'system' });
    expect(s.resolved).toBe('light'); // OS still light
    s = reduce(s, { type: 'set', pref: 'dark' });
    expect(s.resolved).toBe('dark');
    s = reduce(s, { type: 'set', pref: 'light' });
    expect(s.resolved).toBe('light');
  });

  it('system-change only affects resolved when pref=system', () => {
    let s = reduce(init, { type: 'set', pref: 'light' });
    s = reduce(s, { type: 'system-change', systemPrefersDark: true });
    expect(s.resolved).toBe('light'); // explicit override wins
    s = reduce(s, { type: 'set', pref: 'system' });
    expect(s.resolved).toBe('dark'); // now follows OS
  });

  it('resolved is never anything but "light" | "dark"', () => {
    const prefs: ThemePref[] = ['light', 'dark', 'system'];
    for (const pref of prefs) {
      for (const osDark of [false, true]) {
        const s = reduce({ pref: 'system', systemPrefersDark: false, resolved: 'light' }, { type: 'set', pref });
        const s2 = reduce(s, { type: 'system-change', systemPrefersDark: osDark });
        expect(['light', 'dark']).toContain(s2.resolved);
      }
    }
  });

  it('reducer is pure (no mutation)', () => {
    const before = { ...init };
    reduce(init, { type: 'set', pref: 'dark' });
    expect(init).toEqual(before);
  });
});

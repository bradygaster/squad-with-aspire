/**
 * DM-004 contract test 4: Storage fallback (corrupted/disabled localStorage).
 * Pure helper, no DOM — binds to DM-002's storage adapter on landing.
 */
import { describe, it, expect } from 'vitest';

export type ThemePref = 'light' | 'dark' | 'system';
const VALID: ThemePref[] = ['light', 'dark', 'system'];

export interface Storage {
  getItem(key: string): string | null;
}

export function readStoredPref(storage: Storage | null, key = 'ta:theme:v1'): ThemePref {
  if (!storage) return 'system';
  let raw: string | null;
  try {
    raw = storage.getItem(key);
  } catch {
    return 'system';
  }
  if (raw === null) return 'system';
  return (VALID as string[]).includes(raw) ? (raw as ThemePref) : 'system';
}

describe('DM-004 §3 storage fallback', () => {
  it('localStorage disabled (null) -> system', () => {
    expect(readStoredPref(null)).toBe('system');
  });

  it('localStorage throws on read -> system, no crash', () => {
    const s: Storage = { getItem: () => { throw new Error('SecurityError'); } };
    expect(readStoredPref(s)).toBe('system');
  });

  it('missing key -> system', () => {
    expect(readStoredPref({ getItem: () => null })).toBe('system');
  });

  it('corrupted value "garbage" -> system', () => {
    expect(readStoredPref({ getItem: () => 'garbage' })).toBe('system');
  });

  it('valid value passes through', () => {
    expect(readStoredPref({ getItem: () => 'dark' })).toBe('dark');
    expect(readStoredPref({ getItem: () => 'light' })).toBe('light');
    expect(readStoredPref({ getItem: () => 'system' })).toBe('system');
  });

  it('case-sensitive: "Dark" not accepted', () => {
    expect(readStoredPref({ getItem: () => 'Dark' })).toBe('system');
  });

  it('empty string -> system', () => {
    expect(readStoredPref({ getItem: () => '' })).toBe('system');
  });
});

// LP-006 property tests for setLastPage / getLastPage / validator.
// Pure — runs against the contract package, not the app. App-dev's setLastPage.ts
// implementation should import from this package OR re-export an identical surface.
//
// Uses fast-check; falls back to a tiny inline runner if fast-check isn't installed
// so this test can land before app-dev's dev-deps lock.

import { describe, it, expect, beforeEach } from 'vitest';
import {
  setLastPage, getLastPage, clearLastPage, validatePath, isOptedOut,
  STORAGE_KEY, OPT_OUT_KEY, MAX_PAYLOAD_BYTES, MAX_PATH_BYTES,
  type StorageLike,
} from '../../packages/last-page-contract/src/last-page';

// ---- in-memory storage double --------------------------------------------------

function memStorage(opts: { throwOnSet?: boolean } = {}): StorageLike & { _map: Map<string, string> } {
  const map = new Map<string, string>();
  return {
    _map: map,
    getItem: (k) => (map.has(k) ? map.get(k)! : null),
    setItem: (k, v) => { if (opts.throwOnSet) throw new Error('QuotaExceededError'); map.set(k, v); },
    removeItem: (k) => { map.delete(k); },
  };
}

// ---- arbitrary path generator (replaces fast-check when absent) ----------------

let fc: typeof import('fast-check') | null = null;
try { fc = await import('fast-check'); } catch { fc = null; }

const PATH_SEGMENT_CHARS = 'abcdefghijklmnopqrstuvwxyz0123456789-_';
function randomPath(seed: number): string {
  // Deterministic pseudo-random: not cryptographic, just spreads inputs.
  const rng = (n: number) => { seed = (seed * 9301 + 49297) % 233280; return Math.floor((seed / 233280) * n); };
  const depth = 1 + rng(5);
  const segs: string[] = [];
  for (let i = 0; i < depth; i++) {
    const len = 1 + rng(12);
    let s = '';
    for (let j = 0; j < len; j++) s += PATH_SEGMENT_CHARS[rng(PATH_SEGMENT_CHARS.length)];
    segs.push(s);
  }
  return '/' + segs.join('/');
}

function runProperty(n: number, prop: (seed: number) => void) {
  if (fc) {
    fc.assert(fc.property(fc.integer({ min: 1, max: 100_000 }), prop), { numRuns: n });
  } else {
    for (let i = 1; i <= n; i++) prop(i * 7919);
  }
}

// ---- tests --------------------------------------------------------------------

describe('LP-006 property: setLastPage ↔ getLastPage roundtrip', () => {
  it('roundtrips for any path that passes the validator', () => {
    runProperty(200, (seed) => {
      const s = memStorage();
      const path = randomPath(seed);
      const result = setLastPage(path, s);
      const validatorOk = validatePath(path).ok;
      if (validatorOk) {
        expect(result.stored).toBe(true);
        expect(getLastPage(s)).toBe(path);
      } else {
        expect(result.stored).toBe(false);
        expect(getLastPage(s)).toBeNull();
      }
    });
  });

  it('roundtrip survives clear+reset', () => {
    runProperty(100, (seed) => {
      const s = memStorage();
      const path = randomPath(seed);
      setLastPage(path, s);
      clearLastPage(s);
      expect(getLastPage(s)).toBeNull();
      const second = randomPath(seed + 1);
      const r = setLastPage(second, s);
      if (r.stored) expect(getLastPage(s)).toBe(second);
    });
  });
});

describe('LP-006 property: payload size cap (2KB)', () => {
  it('rejects payloads larger than MAX_PAYLOAD_BYTES', () => {
    const s = memStorage();
    // Build a path with valid chars >2KB.
    const huge = '/' + 'a'.repeat(MAX_PAYLOAD_BYTES + 100);
    const r = setLastPage(huge, s);
    expect(r.stored).toBe(false);
    expect(getLastPage(s)).toBeNull();
  });

  it('rejects path >1KB at validator boundary', () => {
    const s = memStorage();
    const oversize = '/' + 'a'.repeat(MAX_PATH_BYTES + 1);
    expect(validatePath(oversize).ok).toBe(false);
    expect(setLastPage(oversize, s).stored).toBe(false);
  });

  it('accepts payload at exactly 1KB', () => {
    const s = memStorage();
    const atLimit = '/' + 'a'.repeat(MAX_PATH_BYTES - 1); // total exactly MAX_PATH_BYTES
    expect(validatePath(atLimit).ok).toBe(true);
    expect(setLastPage(atLimit, s).stored).toBe(true);
  });
});

describe('LP-006 property: opt-out OFF is destructive', () => {
  it('clears existing value when opt-out flips ON', () => {
    const s = memStorage();
    setLastPage('/flights/search?from=TLV', s);
    expect(getLastPage(s)).toBe('/flights/search?from=TLV');

    // User toggles opt-out ON
    s.setItem(OPT_OUT_KEY, '1');
    expect(isOptedOut(s)).toBe(true);

    // Next setLastPage is a no-op AND clears existing value
    const r = setLastPage('/hotels/123', s);
    expect(r.stored).toBe(false);
    expect((r as { reason: string }).reason).toBe('opted-out');
    expect(s._map.has(STORAGE_KEY)).toBe(false);
    expect(getLastPage(s)).toBeNull();
  });

  it('opt-out ON from the start: setLastPage never writes', () => {
    runProperty(50, (seed) => {
      const s = memStorage();
      s.setItem(OPT_OUT_KEY, '1');
      const r = setLastPage(randomPath(seed), s);
      expect(r.stored).toBe(false);
      expect(s._map.has(STORAGE_KEY)).toBe(false);
    });
  });
});

describe('LP-006 property: Safari private-mode (storage throws)', () => {
  it('setLastPage returns {stored:false} and never throws', () => {
    const s = memStorage({ throwOnSet: true });
    expect(() => setLastPage('/flights/search', s)).not.toThrow();
    const r = setLastPage('/flights/search', s);
    expect(r.stored).toBe(false);
    expect((r as { reason: string }).reason).toBe('storage-threw');
  });

  it('property: never throws across many random inputs when storage is broken', () => {
    const s = memStorage({ throwOnSet: true });
    runProperty(100, (seed) => {
      expect(() => setLastPage(randomPath(seed), s)).not.toThrow();
    });
  });

  it('getLastPage tolerates storage that throws on getItem', () => {
    const s: StorageLike = {
      getItem: () => { throw new Error('SecurityError'); },
      setItem: () => { /* noop */ },
      removeItem: () => { /* noop */ },
    };
    expect(() => getLastPage(s)).not.toThrow();
    expect(getLastPage(s)).toBeNull();
  });
});

describe('LP-006: null/missing storage', () => {
  it('setLastPage with null storage is a no-op', () => {
    const r = setLastPage('/flights', null);
    expect(r.stored).toBe(false);
  });
  it('getLastPage with null storage returns null', () => {
    expect(getLastPage(null)).toBeNull();
  });
});

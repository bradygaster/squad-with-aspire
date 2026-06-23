// Pure helpers for "remember last viewed page" (LP-006).
// No I/O. No timers. localStorage access is injected for testability.
//
// Locks (per LP-001 spec; revise when LP-002/LP-003 finalize):
//   STORAGE_KEY      = 'ta.nav.lastPage.v1'   (single source of truth; enforced by smoke gate)
//   MAX_PAYLOAD_BYTES = 2048                  (UTF-8 byte length)
//   MAX_PATH_BYTES    = 1024
//   OPT_OUT_KEY      = 'ta.nav.lastPage.optOut.v1'  ('1' === opted-out)

export const STORAGE_KEY = 'ta.nav.lastPage.v1';
export const OPT_OUT_KEY = 'ta.nav.lastPage.optOut.v1';
export const MAX_PAYLOAD_BYTES = 2048;
export const MAX_PATH_BYTES = 1024;

export const DENY_PATH_PREFIXES: readonly string[] = Object.freeze([
  '/login', '/logout', '/signup', '/register',
  '/oauth/', '/auth/', '/sso/',
  '/account/verify', '/account/reset', '/password/reset',
  '/forgot', '/mfa', '/2fa',
  '/error', '/404', '/500',
]);

export const TOKEN_QUERY_KEYS: readonly string[] = Object.freeze([
  'token', 'code', 'id_token', 'access_token', 'refresh_token',
  'state', 'session', 'sid', 'auth', 'jwt',
]);

export type ValidationReason =
  | 'empty' | 'not-string' | 'absolute-url' | 'javascript-scheme' | 'data-scheme'
  | 'protocol-relative' | 'encoded-protocol-relative' | 'crlf' | 'control-char'
  | 'too-large' | 'no-leading-slash' | 'deny-list' | 'token-in-query';

export type ValidationResult =
  | { ok: true; normalized: string }
  | { ok: false; reason: ValidationReason };

const ABSOLUTE_URL = /^[a-z][a-z0-9+.\-]*:/i;

export function byteLength(s: string): number {
  if (typeof TextEncoder !== 'undefined') return new TextEncoder().encode(s).length;
  let n = 0;
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (c < 0x80) n += 1;
    else if (c < 0x800) n += 2;
    else if (c >= 0xd800 && c <= 0xdbff) { n += 4; i++; }
    else n += 3;
  }
  return n;
}

export function hasTokenQueryKey(search: string): boolean {
  if (search.length === 0) return false;
  for (const raw of search.split('&')) {
    const eq = raw.indexOf('=');
    const key = (eq === -1 ? raw : raw.slice(0, eq)).toLowerCase();
    if (TOKEN_QUERY_KEYS.includes(key)) return true;
  }
  return false;
}

export function validatePath(input: unknown): ValidationResult {
  if (typeof input !== 'string') return { ok: false, reason: 'not-string' };
  if (input.length === 0) return { ok: false, reason: 'empty' };
  if (byteLength(input) > MAX_PATH_BYTES) return { ok: false, reason: 'too-large' };

  if (/[\r\n]/.test(input)) return { ok: false, reason: 'crlf' };
  // eslint-disable-next-line no-control-regex
  if (/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/.test(input)) return { ok: false, reason: 'control-char' };

  const lower = input.toLowerCase();
  if (lower.startsWith('javascript:')) return { ok: false, reason: 'javascript-scheme' };
  if (lower.startsWith('data:')) return { ok: false, reason: 'data-scheme' };

  if (input.startsWith('//')) return { ok: false, reason: 'protocol-relative' };
  if (/^\/?%2f%2f/i.test(input)) return { ok: false, reason: 'encoded-protocol-relative' };
  if (input.startsWith('/\\') || input.startsWith('\\')) return { ok: false, reason: 'protocol-relative' };

  if (ABSOLUTE_URL.test(input)) return { ok: false, reason: 'absolute-url' };
  if (!input.startsWith('/')) return { ok: false, reason: 'no-leading-slash' };

  const [pathPart, searchPart = ''] = input.split('?', 2);
  const pathLower = pathPart.toLowerCase();

  for (const prefix of DENY_PATH_PREFIXES) {
    if (prefix.endsWith('/')) {
      if (pathLower.startsWith(prefix)) return { ok: false, reason: 'deny-list' };
    } else {
      if (pathLower === prefix || pathLower.startsWith(prefix + '/')) {
        return { ok: false, reason: 'deny-list' };
      }
    }
  }

  if (searchPart.length > 0 && hasTokenQueryKey(searchPart)) {
    return { ok: false, reason: 'token-in-query' };
  }

  return { ok: true, normalized: input };
}

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export function isOptedOut(storage: StorageLike | null | undefined): boolean {
  if (!storage) return true;
  try { return storage.getItem(OPT_OUT_KEY) === '1'; } catch { return true; }
}

export type SetResult =
  | { stored: true; path: string }
  | { stored: false; reason: ValidationReason | 'opted-out' | 'no-storage' | 'payload-too-large' | 'storage-threw' };

export function setLastPage(path: string, storage: StorageLike | null | undefined): SetResult {
  if (!storage) return { stored: false, reason: 'no-storage' };

  if (isOptedOut(storage)) {
    // Opt-out is destructive — clear any prior value.
    try { storage.removeItem(STORAGE_KEY); } catch { /* swallow */ }
    return { stored: false, reason: 'opted-out' };
  }

  const v = validatePath(path);
  if (!v.ok) return { stored: false, reason: v.reason };

  if (byteLength(v.normalized) > MAX_PAYLOAD_BYTES) {
    return { stored: false, reason: 'payload-too-large' };
  }

  try {
    storage.setItem(STORAGE_KEY, v.normalized);
    return { stored: true, path: v.normalized };
  } catch {
    return { stored: false, reason: 'storage-threw' };
  }
}

export function getLastPage(storage: StorageLike | null | undefined): string | null {
  if (!storage) return null;
  if (isOptedOut(storage)) return null;
  let raw: string | null;
  try { raw = storage.getItem(STORAGE_KEY); } catch { return null; }
  if (raw === null) return null;
  const v = validatePath(raw);
  if (!v.ok) {
    try { storage.removeItem(STORAGE_KEY); } catch { /* swallow */ }
    return null;
  }
  return v.normalized;
}

export function clearLastPage(storage: StorageLike | null | undefined): void {
  if (!storage) return;
  try { storage.removeItem(STORAGE_KEY); } catch { /* swallow */ }
}

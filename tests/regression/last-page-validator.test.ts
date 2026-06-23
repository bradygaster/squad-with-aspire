// LP-006 unit tests for validatePath: open-redirect, XSS, and deny-list coverage.

import { describe, it, expect } from 'vitest';
import {
  validatePath, DENY_PATH_PREFIXES, hasTokenQueryKey, TOKEN_QUERY_KEYS, MAX_PATH_BYTES,
} from '../../packages/last-page-contract/src/last-page';

describe('LP-006 validator: scheme & open-redirect rejection', () => {
  it.each([
    ['absolute http',         'http://evil.com/foo',           'absolute-url'],
    ['absolute https',        'https://evil.com/foo',          'absolute-url'],
    ['javascript: lower',     'javascript:alert(1)',           'javascript-scheme'],
    ['javascript: mixed',     'JavaScript:alert(1)',           'javascript-scheme'],
    ['data: scheme',          'data:text/html,<script>x</script>', 'data-scheme'],
    ['protocol-relative',     '//evil.com/foo',                'protocol-relative'],
    ['encoded //',            '%2F%2Fevil.com/foo',            'encoded-protocol-relative'],
    ['encoded // mixed case', '%2f%2Fevil.com/foo',            'encoded-protocol-relative'],
    ['encoded // w/ slash',   '/%2f%2Fevil.com/foo',           'encoded-protocol-relative'],
    ['backslash trick',       '/\\evil.com/foo',               'protocol-relative'],
    ['leading backslash',     '\\evil.com',                    'protocol-relative'],
    ['mailto',                'mailto:x@y.com',                'absolute-url'],
    ['file scheme',           'file:///etc/passwd',            'absolute-url'],
  ])('rejects %s', (_label, input, reason) => {
    const r = validatePath(input);
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe(reason);
  });
});

describe('LP-006 validator: control chars and length', () => {
  it('rejects \\r\\n (CRLF injection)', () => {
    const r = validatePath('/foo\r\nSet-Cookie: bad=1');
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('crlf');
  });
  it('rejects bare \\n', () => {
    expect(validatePath('/foo\nbar').ok).toBe(false);
  });
  it('rejects NULL byte', () => {
    expect(validatePath('/foo\x00bar').ok).toBe(false);
  });
  it('rejects DEL byte', () => {
    expect(validatePath('/foo\x7Fbar').ok).toBe(false);
  });
  it('rejects path larger than MAX_PATH_BYTES', () => {
    const big = '/' + 'a'.repeat(MAX_PATH_BYTES + 1);
    const r = validatePath(big);
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('too-large');
  });
  it('rejects non-string inputs', () => {
    expect(validatePath(null).ok).toBe(false);
    expect(validatePath(undefined).ok).toBe(false);
    expect(validatePath(42).ok).toBe(false);
    expect(validatePath({}).ok).toBe(false);
  });
  it('rejects empty string', () => {
    const r = validatePath('');
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('empty');
  });
  it('rejects paths missing leading slash', () => {
    const r = validatePath('flights/search');
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('no-leading-slash');
  });
});

describe('LP-006 validator: LP-001 D2 deny-list', () => {
  it.each(DENY_PATH_PREFIXES.map((p) => [p]))('rejects exact deny-list entry %s', (prefix) => {
    const sample = prefix.endsWith('/') ? prefix + 'callback' : prefix;
    const r = validatePath(sample);
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('deny-list');
  });

  it.each(DENY_PATH_PREFIXES.map((p) => [p]))('rejects child of deny-list entry %s', (prefix) => {
    const child = (prefix.endsWith('/') ? prefix : prefix + '/') + 'sub';
    expect(validatePath(child).ok).toBe(false);
  });

  it.each([
    '/Login', '/LOGOUT', '/OAuth/callback', '/Auth/me',
  ])('case-insensitive deny match for %s', (p) => {
    expect(validatePath(p).ok).toBe(false);
  });

  it('does NOT deny non-prefixed look-alikes', () => {
    // /loginstuff should NOT match /login (must be exact or /-bounded)
    expect(validatePath('/loginstuff').ok).toBe(true);
    expect(validatePath('/forgotten').ok).toBe(true);
  });
});

describe('LP-006 validator: token-in-search detection', () => {
  it.each(TOKEN_QUERY_KEYS.map((k) => [k]))('rejects ?%s=...', (key) => {
    const r = validatePath(`/callback?${key}=abc123`);
    expect(r.ok).toBe(false);
    if (!r.ok) expect(r.reason).toBe('token-in-query');
  });

  it.each([
    '/cb?Token=abc',
    '/cb?CODE=abc',
    '/cb?Id_Token=abc',
    '/cb?STATE=xyz',
  ])('case-insensitive token match for %s', (p) => {
    expect(validatePath(p).ok).toBe(false);
  });

  it('rejects when token is one of many params', () => {
    expect(validatePath('/cb?foo=1&code=secret&bar=2').ok).toBe(false);
  });

  it('accepts harmless query strings', () => {
    expect(validatePath('/flights/search?from=TLV&to=JFK').ok).toBe(true);
    expect(validatePath('/hotels?city=paris&checkin=2026-07-01').ok).toBe(true);
  });

  it('hasTokenQueryKey on empty string is false', () => {
    expect(hasTokenQueryKey('')).toBe(false);
  });
});

describe('LP-006 validator: positive cases', () => {
  it.each([
    '/',
    '/flights',
    '/flights/search',
    '/flights/search?from=TLV&to=JFK',
    '/vacations/123',
    '/account/profile',     // not in deny-list (only /account/verify, /account/reset are)
    '/account/bookings/456',
    '/help/faq',
  ])('accepts %s', (p) => {
    const r = validatePath(p);
    expect(r.ok).toBe(true);
    if (r.ok) expect(r.normalized).toBe(p);
  });
});

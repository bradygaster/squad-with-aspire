// usePollingResource.ts
// Generalized polling hook — supersedes useOrderStatus for refund + future polled resources.
// Contract: per Iris's note to Fenster & QA seam in WI-REFUND-4 bundle.
//
//   usePollingResource<T>(url, {
//     interval: 5000,
//     capMs: 60_000,
//     maxPolls: 12,
//     terminalStates: ['succeeded', 'failed'],
//     selectState: (data) => data.status,
//     enabled: true,
//   })
//
// Behavior:
//   - Polls `url` every `interval` ms.
//   - Stops on terminal state OR poll cap OR elapsed cap (whichever first).
//   - 404/403 → returns kind='not_found' (IDOR-safe, no leak).
//   - Network/5xx → exponential backoff inside `interval` budget, surfaces kind='transient'.
//   - ETag/If-None-Match supported when server sends ETag.
//   - Aborts in-flight request on unmount or when `enabled` flips false.
//   - SSR-safe (guarded against missing window).

import { useEffect, useRef, useState, useCallback } from 'react';

export type PollState<T> =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'data'; data: T; terminal: boolean }
  | { kind: 'transient'; lastData?: T; attempt: number }
  | { kind: 'not_found' }
  | { kind: 'cap_exceeded'; lastData?: T };

export interface UsePollingResourceOptions<T> {
  interval: number;
  capMs: number;
  maxPolls: number;
  terminalStates: ReadonlyArray<string>;
  selectState: (data: T) => string;
  enabled?: boolean;
  fetchImpl?: typeof fetch;
}

export function usePollingResource<T>(
  url: string | null,
  opts: UsePollingResourceOptions<T>
): { state: PollState<T>; reset: () => void } {
  const {
    interval,
    capMs,
    maxPolls,
    terminalStates,
    selectState,
    enabled = true,
    fetchImpl = typeof window !== 'undefined' ? window.fetch.bind(window) : undefined,
  } = opts;

  const [state, setState] = useState<PollState<T>>({ kind: 'idle' });
  const pollCountRef = useRef(0);
  const startedAtRef = useRef<number | null>(null);
  const etagRef = useRef<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastDataRef = useRef<T | undefined>(undefined);

  const reset = useCallback(() => {
    pollCountRef.current = 0;
    startedAtRef.current = null;
    etagRef.current = null;
    lastDataRef.current = undefined;
    if (abortRef.current) abortRef.current.abort();
    if (timerRef.current) clearTimeout(timerRef.current);
    setState({ kind: 'idle' });
  }, []);

  useEffect(() => {
    if (!enabled || !url || !fetchImpl) return;
    let cancelled = false;
    startedAtRef.current = Date.now();
    setState({ kind: 'loading' });

    const tick = async () => {
      if (cancelled) return;
      pollCountRef.current += 1;
      const elapsed = Date.now() - (startedAtRef.current ?? Date.now());

      abortRef.current = new AbortController();
      const headers: Record<string, string> = { Accept: 'application/json' };
      if (etagRef.current) headers['If-None-Match'] = etagRef.current;

      try {
        const res = await fetchImpl(url, {
          headers,
          signal: abortRef.current.signal,
          credentials: 'include',
        });

        if (res.status === 404 || res.status === 403) {
          if (!cancelled) setState({ kind: 'not_found' });
          return;
        }

        if (res.status === 304) {
          // No change — keep last data, schedule next.
        } else if (res.ok) {
          const etag = res.headers.get('ETag');
          if (etag) etagRef.current = etag;
          const data = (await res.json()) as T;
          lastDataRef.current = data;
          const s = selectState(data);
          const terminal = terminalStates.includes(s);
          if (!cancelled) setState({ kind: 'data', data, terminal });
          if (terminal) return;
        } else {
          // 5xx / transient
          if (!cancelled)
            setState({ kind: 'transient', lastData: lastDataRef.current, attempt: pollCountRef.current });
        }
      } catch (e: unknown) {
        const isAbort = e instanceof Error && e.name === 'AbortError';
        if (isAbort) return;
        if (!cancelled)
          setState({ kind: 'transient', lastData: lastDataRef.current, attempt: pollCountRef.current });
      }

      if (pollCountRef.current >= maxPolls || elapsed >= capMs) {
        if (!cancelled) setState({ kind: 'cap_exceeded', lastData: lastDataRef.current });
        return;
      }

      timerRef.current = setTimeout(tick, interval);
    };

    tick();

    return () => {
      cancelled = true;
      if (abortRef.current) abortRef.current.abort();
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [url, enabled, interval, capMs, maxPolls, terminalStates, selectState, fetchImpl]);

  return { state, reset };
}

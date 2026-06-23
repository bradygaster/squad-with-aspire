// useOrderStatus.ts — Polling hook for /api/checkout/orders/{orderId}/status
// Contract source: application-development bundle wi-confirm-1-order-status (commit 1e30d04)
// Spec source: squads/experience-design/artifacts/checkout-confirmation-a11y-spec.md
//
// Behavior:
//  - Polls every 3s (a11y spec) up to POLL_CAP polls (10 = 30s window)
//  - Honors RFC 7232 ETag / If-None-Match (304 = no state change, no re-render)
//  - Stops polling on terminal states (confirmed, payment_failed, inventory_released, canceled)
//  - Returns derived UI state matching the 5 order states from a11y spec
//  - Does NOT move focus on pending→confirmed (live region only)
//  - DOES surface failed_post_auth to caller so caller can move focus
//
// IDOR: backend returns 404 for both not-found and forbidden — caller must NOT distinguish.

import { useEffect, useRef, useState, useCallback } from 'react';

export type OrderApiState =
  | 'pending'
  | 'confirmed'
  | 'payment_failed'
  | 'inventory_released'
  | 'canceled';

// UI states from checkout-confirmation-a11y-spec.md — superset of API states.
// reconciliation_delayed is derived client-side when poll cap is reached on `pending`.
// not_found_or_forbidden is derived from HTTP 404.
export type OrderUiState =
  | 'confirmed'
  | 'pending_reconciliation'
  | 'reconciliation_delayed'
  | 'failed_post_auth'
  | 'not_found_or_forbidden';

export interface OrderStatusResponse {
  orderId: string;
  state: OrderApiState;
  paymentState: string;
  fulfillmentState: string;
  updatedAt: string;
}

export interface UseOrderStatusResult {
  uiState: OrderUiState | null;
  data: OrderStatusResponse | null;
  isPolling: boolean;
  pollCount: number;
  /** True when state transitions to a terminal UI state — caller decides on focus management. */
  hasTerminalTransition: boolean;
  /** True for failed_post_auth specifically — a11y spec requires focus move. */
  shouldMoveFocus: boolean;
  retry: () => void;
}

const POLL_INTERVAL_MS = 3000;
const POLL_CAP = 10;
const TERMINAL_API_STATES: ReadonlySet<OrderApiState> = new Set([
  'confirmed',
  'payment_failed',
  'inventory_released',
  'canceled',
]);

function mapApiToUi(state: OrderApiState, pollCount: number): OrderUiState {
  switch (state) {
    case 'confirmed':
      return 'confirmed';
    case 'payment_failed':
      return 'failed_post_auth';
    case 'inventory_released':
    case 'canceled':
      // Treated as failed_post_auth for UI — user must see the failure & contact path.
      return 'failed_post_auth';
    case 'pending':
      return pollCount >= POLL_CAP ? 'reconciliation_delayed' : 'pending_reconciliation';
  }
}

export function useOrderStatus(
  orderId: string,
  authToken: string | null,
  fetchImpl: typeof fetch = fetch,
): UseOrderStatusResult {
  const [data, setData] = useState<OrderStatusResponse | null>(null);
  const [uiState, setUiState] = useState<OrderUiState | null>(null);
  const [pollCount, setPollCount] = useState(0);
  const [isPolling, setIsPolling] = useState(true);
  const [hasTerminalTransition, setHasTerminalTransition] = useState(false);
  const [shouldMoveFocus, setShouldMoveFocus] = useState(false);

  const etagRef = useRef<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const prevApiStateRef = useRef<OrderApiState | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const poll = useCallback(async (currentPoll: number) => {
    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;

    const headers: Record<string, string> = {
      Accept: 'application/json',
    };
    if (authToken) headers['Authorization'] = `Bearer ${authToken}`;
    if (etagRef.current) headers['If-None-Match'] = etagRef.current;

    let res: Response;
    try {
      res = await fetchImpl(`/api/checkout/orders/${encodeURIComponent(orderId)}/status`, {
        method: 'GET',
        headers,
        credentials: 'same-origin',
        signal: ctrl.signal,
      });
    } catch (err) {
      // Network error — keep polling within cap; surface no UI change.
      return { keepPolling: currentPoll < POLL_CAP };
    }

    // IDOR-safe: 404 covers both not-found and forbidden. Do NOT distinguish.
    if (res.status === 404) {
      setUiState('not_found_or_forbidden');
      setHasTerminalTransition(true);
      setShouldMoveFocus(true);
      return { keepPolling: false };
    }

    if (res.status === 304) {
      // No state change. Don't re-render data; still tick pollCount for cap math.
      return { keepPolling: true };
    }

    if (!res.ok) {
      // 5xx etc — keep polling within cap, no UI flip.
      return { keepPolling: currentPoll < POLL_CAP };
    }

    const etag = res.headers.get('ETag');
    if (etag) etagRef.current = etag;

    const body: OrderStatusResponse = await res.json();
    setData(body);

    const prev = prevApiStateRef.current;
    prevApiStateRef.current = body.state;

    const nextUi = mapApiToUi(body.state, currentPoll);
    setUiState(nextUi);

    const isTerminal = TERMINAL_API_STATES.has(body.state);
    const becameTerminal = isTerminal && prev !== body.state;
    if (becameTerminal) {
      setHasTerminalTransition(true);
      // a11y spec: do NOT move focus on pending→confirmed (live region only).
      // DO move focus on failed_post_auth and on terminal failure variants.
      const focusable =
        body.state === 'payment_failed' ||
        body.state === 'inventory_released' ||
        body.state === 'canceled';
      setShouldMoveFocus(focusable);
    }

    return { keepPolling: !isTerminal && currentPoll < POLL_CAP };
  }, [orderId, authToken, fetchImpl]);

  useEffect(() => {
    let cancelled = false;
    let count = 0;

    const tick = async () => {
      if (cancelled) return;
      count += 1;
      setPollCount(count);
      const { keepPolling } = await poll(count);
      if (cancelled) return;

      if (!keepPolling) {
        setIsPolling(false);
        // If we hit the cap on `pending`, mark reconciliation_delayed terminal-for-UI.
        if (prevApiStateRef.current === 'pending' && count >= POLL_CAP) {
          setUiState('reconciliation_delayed');
          setHasTerminalTransition(true);
          // a11y spec: reconciliation_delayed is announced via live region, no focus move.
        }
        return;
      }
      timerRef.current = setTimeout(tick, POLL_INTERVAL_MS);
    };

    // Initial poll fires immediately, then 3s cadence.
    tick();

    return () => {
      cancelled = true;
      if (timerRef.current) clearTimeout(timerRef.current);
      abortRef.current?.abort();
    };
  }, [poll]);

  const retry = useCallback(() => {
    etagRef.current = null;
    prevApiStateRef.current = null;
    setPollCount(0);
    setHasTerminalTransition(false);
    setShouldMoveFocus(false);
    setIsPolling(true);
    setUiState(null);
  }, []);

  return {
    uiState,
    data,
    isPolling,
    pollCount,
    hasTerminalTransition,
    shouldMoveFocus,
    retry,
  };
}

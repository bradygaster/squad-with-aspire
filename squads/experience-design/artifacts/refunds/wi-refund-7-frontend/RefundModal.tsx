// RefundModal.tsx
// WI-REFUND-7 frontend impl, pinned to UX spec 4c84355 + QA seams in WI-REFUND-4 bundle.
//
// Behavior contract:
//   § 6.1 Cancel is autofocus on open. Enter on Cancel closes. Esc closes.
//   § 6.1 Focus trap on Tab — wraps Cancel ⇄ Confirm.
//   § 6.3 Browser Back = Cancel; no URL change while modal open.
//   § 6.2 pending→succeeded: focus stays put, aria-live="polite" announces.
//   § 6.2 pending→failed: role="alert" auto-announces, focus moves to "Try again".
//   § 5.2 4 mapped failure codes render plain copy. Raw code NEVER in DOM.
//        Unmapped code → fires telemetry refund.failure_reason_unmapped + generic copy.
//   § 5.3 409 REFUND_ALREADY_EXISTS: inline 3s strip, auto-transition to succeeded poll.
//   SEC-RFD-001 / GATE-RFD-06: provider re_xxx IDs never rendered.
//
// QA seam compliance (RefundModal.spec.ts):
//   data-testid="refund-trigger-button"  (in RefundButton.tsx)
//   data-testid="refund-modal"
//   data-testid="refund-cancel-button"
//   data-testid="refund-confirm-button"
//   data-testid="refund-inline-error"
//   data-testid="refund-retry-button"
//   data-testid="refund-error-message"
//   window.telemetry.track(event, props) — interceptable

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { usePollingResource } from './usePollingResource';

type FailureReason =
  | 'PROVIDER_DECLINED'
  | 'PROVIDER_TIMEOUT'
  | 'PROVIDER_UNAVAILABLE'
  | 'INSUFFICIENT_PROVIDER_FUNDS';

const FAILURE_COPY: Record<FailureReason, string> = {
  PROVIDER_DECLINED: 'Your bank declined the refund. Contact support to retry.',
  PROVIDER_TIMEOUT: 'The refund timed out. Try again in a moment.',
  PROVIDER_UNAVAILABLE: 'Refunds are temporarily unavailable. Try again later.',
  INSUFFICIENT_PROVIDER_FUNDS: 'Refund could not be processed. Contact support.',
};

const FAILURE_REASONS = Object.keys(FAILURE_COPY) as ReadonlyArray<FailureReason>;
const GENERIC_FAILURE_COPY = 'Something went wrong with the refund. Contact support.';

type ModalState =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'inline_already_exists'; refundId: string }
  | { kind: 'polling'; refundId: string }
  | { kind: 'succeeded' }
  | { kind: 'failed'; copy: string };

interface RefundResponse {
  refundId: string;
  status: 'pending' | 'succeeded' | 'failed';
  failureReason?: string;
}

declare global {
  interface Window {
    telemetry?: { track: (event: string, props?: Record<string, unknown>) => void };
  }
}

function safeTrack(event: string, props?: Record<string, unknown>) {
  try {
    window.telemetry?.track(event, props);
  } catch {
    /* telemetry must never break UX */
  }
}

export interface RefundModalProps {
  orderId: string;
  isOpen: boolean;
  onClose: () => void;
  onRefundSucceeded?: () => void;
  apiBase?: string;
}

export const RefundModal: React.FC<RefundModalProps> = ({
  orderId,
  isOpen,
  onClose,
  onRefundSucceeded,
  apiBase = '',
}) => {
  const [state, setState] = useState<ModalState>({ kind: 'idle' });
  const cancelRef = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const retryRef = useRef<HTMLButtonElement>(null);
  const previouslyFocusedRef = useRef<HTMLElement | null>(null);

  // Autofocus Cancel on open. Restore focus on close.
  useEffect(() => {
    if (!isOpen) return;
    previouslyFocusedRef.current = document.activeElement as HTMLElement | null;
    cancelRef.current?.focus();
    return () => {
      previouslyFocusedRef.current?.focus?.();
    };
  }, [isOpen]);

  // Focus on Try again when entering failed state.
  useEffect(() => {
    if (state.kind === 'failed') retryRef.current?.focus();
  }, [state.kind]);

  // Browser Back = Cancel (no URL change). Push a state on open, pop on close.
  useEffect(() => {
    if (!isOpen) return;
    const onPop = () => onClose();
    window.history.pushState({ refundModal: orderId }, '');
    window.addEventListener('popstate', onPop);
    return () => {
      window.removeEventListener('popstate', onPop);
      if (window.history.state?.refundModal === orderId) {
        window.history.back();
      }
    };
  }, [isOpen, orderId, onClose]);

  // Esc closes.
  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isOpen, onClose]);

  // 409 inline strip auto-transitions to polling after 3s.
  useEffect(() => {
    if (state.kind !== 'inline_already_exists') return;
    const t = setTimeout(() => {
      setState({ kind: 'polling', refundId: state.refundId });
    }, 3000);
    return () => clearTimeout(t);
  }, [state]);

  const refundId = state.kind === 'polling' ? state.refundId : null;
  const pollUrl = refundId ? `${apiBase}/refunds/${encodeURIComponent(refundId)}` : null;

  const { state: pollState } = usePollingResource<RefundResponse>(pollUrl, {
    interval: 5000,
    capMs: 60_000,
    maxPolls: 12,
    terminalStates: ['succeeded', 'failed'],
    selectState: (d) => d.status,
    enabled: state.kind === 'polling',
  });

  useEffect(() => {
    if (state.kind !== 'polling') return;
    if (pollState.kind === 'data' && pollState.terminal) {
      const data = pollState.data;
      if (data.status === 'succeeded') {
        setState({ kind: 'succeeded' });
        onRefundSucceeded?.();
        safeTrack('refund.succeeded', { orderId });
      } else if (data.status === 'failed') {
        const reason = data.failureReason;
        const isMapped = !!reason && (FAILURE_REASONS as ReadonlyArray<string>).includes(reason);
        if (!isMapped) {
          safeTrack('refund.failure_reason_unmapped', { orderId, reason: reason ?? '<missing>' });
        }
        setState({
          kind: 'failed',
          copy: isMapped ? FAILURE_COPY[reason as FailureReason] : GENERIC_FAILURE_COPY,
        });
      }
    } else if (pollState.kind === 'cap_exceeded') {
      setState({ kind: 'failed', copy: GENERIC_FAILURE_COPY });
      safeTrack('refund.polling_cap_exceeded', { orderId });
    }
  }, [pollState, state.kind, orderId, onRefundSucceeded]);

  const submit = useCallback(async () => {
    setState({ kind: 'submitting' });
    safeTrack('refund.submitted', { orderId });
    try {
      const res = await fetch(`${apiBase}/orders/${encodeURIComponent(orderId)}/refunds`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ scope: 'full' }),
      });
      if (res.status === 202) {
        const data = (await res.json()) as { refundId: string };
        setState({ kind: 'polling', refundId: data.refundId });
        return;
      }
      if (res.status === 409) {
        const body = await res.json().catch(() => ({}));
        if (body?.error === 'REFUND_ALREADY_EXISTS' && body?.refundId) {
          setState({ kind: 'inline_already_exists', refundId: String(body.refundId) });
          return;
        }
        // order_not_refundable + reason enum — treat as failed with mapped copy.
        const reason = body?.reason;
        const FOUR = ['canceled', 'already_refunded', 'not_confirmed', 'window_expired'];
        const copy = FOUR.includes(reason)
          ? `Refund unavailable: ${String(reason).replace(/_/g, ' ')}.`
          : GENERIC_FAILURE_COPY;
        if (!FOUR.includes(reason)) {
          safeTrack('refund.failure_reason_unmapped', { orderId, reason: reason ?? '<missing>' });
        }
        setState({ kind: 'failed', copy });
        return;
      }
      setState({ kind: 'failed', copy: GENERIC_FAILURE_COPY });
    } catch {
      setState({ kind: 'failed', copy: GENERIC_FAILURE_COPY });
    }
  }, [orderId, apiBase]);

  // Focus trap on Tab.
  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key !== 'Tab') return;
    const focusables = [cancelRef.current, confirmRef.current, retryRef.current].filter(
      (n): n is HTMLButtonElement => !!n && !n.disabled && n.offsetParent !== null
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = document.activeElement;
    if (e.shiftKey && active === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  };

  if (!isOpen) return null;

  const isBusy = state.kind === 'submitting' || state.kind === 'polling';
  const isTerminal = state.kind === 'succeeded' || state.kind === 'failed';

  return (
    <div className="refund-modal-backdrop" onKeyDown={onKeyDown}>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="refund-modal-title"
        aria-describedby="refund-modal-body"
        className="refund-modal"
        data-testid="refund-modal"
      >
        <h2 id="refund-modal-title" className="refund-modal__title">
          Refund this order?
        </h2>
        <div id="refund-modal-body" className="refund-modal__body">
          <p>This will refund the full amount to the original payment method.</p>

          {state.kind === 'inline_already_exists' && (
            <div
              className="refund-modal__inline-error"
              data-testid="refund-inline-error"
              role="status"
              aria-live="polite"
            >
              A refund was already requested. Checking status…
            </div>
          )}

          <div className="refund-modal__live" aria-live="polite" aria-atomic="true">
            {state.kind === 'succeeded' && 'Refund completed.'}
            {isBusy && 'Processing refund…'}
          </div>

          {state.kind === 'failed' && (
            <div role="alert" className="refund-modal__error" data-testid="refund-error-message">
              {state.copy}
            </div>
          )}
        </div>

        <div className="refund-modal__actions">
          {!isTerminal && (
            <>
              <button
                ref={cancelRef}
                type="button"
                className="refund-modal__cancel"
                data-testid="refund-cancel-button"
                onClick={onClose}
                disabled={isBusy}
              >
                Cancel
              </button>
              <button
                ref={confirmRef}
                type="button"
                className="refund-modal__confirm"
                data-testid="refund-confirm-button"
                onClick={submit}
                disabled={isBusy}
                aria-busy={isBusy}
              >
                {isBusy ? 'Refunding…' : 'Refund'}
              </button>
            </>
          )}

          {state.kind === 'succeeded' && (
            <button
              type="button"
              className="refund-modal__cancel"
              data-testid="refund-cancel-button"
              onClick={onClose}
            >
              Close
            </button>
          )}

          {state.kind === 'failed' && (
            <>
              <button
                ref={cancelRef}
                type="button"
                className="refund-modal__cancel"
                data-testid="refund-cancel-button"
                onClick={onClose}
              >
                Close
              </button>
              <button
                ref={retryRef}
                type="button"
                className="refund-modal__confirm"
                data-testid="refund-retry-button"
                onClick={() => setState({ kind: 'idle' })}
              >
                Try again
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
};

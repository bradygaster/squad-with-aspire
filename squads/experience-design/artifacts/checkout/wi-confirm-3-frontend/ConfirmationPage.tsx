// ConfirmationPage.tsx — /checkout/confirmation/:orderId
// Implements the 5 UI states from squads/experience-design/artifacts/checkout-confirmation-a11y-spec.md
// Consumes useOrderStatus hook (polls /api/checkout/orders/{orderId}/status)
//
// A11y contract:
//  - Single h1 per state, descriptive (not "Order Status")
//  - Live region (role="status", aria-live="polite") announces state transitions
//  - aria-busy="true" on skeleton while polling
//  - Focus moves to error heading on failed_post_auth / not_found_or_forbidden ONLY
//  - Focus does NOT move on pending→confirmed (announced via live region)
//  - All actionable elements reachable by keyboard; visible focus ring
//  - Color is not the only state signal (icon + text + color)
//  - WCAG 2.2 AA target: 1.4.3 contrast, 2.4.7 focus visible, 3.3.7 redundant entry, 4.1.3 status messages

import React, { useEffect, useRef } from 'react';
import { useOrderStatus, OrderUiState } from './useOrderStatus';

interface ConfirmationPageProps {
  orderId: string;
  authToken: string | null;
  /** Optional: where "Continue" or "Return to cart" should navigate. */
  onNavigate?: (path: string) => void;
}

const COPY: Record<OrderUiState, { h1: string; body: string; icon: string }> = {
  confirmed: {
    h1: 'Your trip is booked',
    body: 'Confirmation details and itinerary have been emailed to you.',
    icon: '✓',
  },
  pending_reconciliation: {
    h1: "We're confirming your booking",
    body: 'This usually takes a few seconds. Please keep this page open.',
    icon: '⟳',
  },
  reconciliation_delayed: {
    h1: 'Still working on it',
    body:
      "Your payment went through, but confirmation is taking longer than usual. We'll email you within 15 minutes. You can safely close this page.",
    icon: '⏱',
  },
  failed_post_auth: {
    h1: "We couldn't complete your booking",
    body:
      'Your payment was authorized but the booking did not complete. No charge was finalized. Please try again or contact support.',
    icon: '⚠',
  },
  not_found_or_forbidden: {
    h1: 'Order not found',
    body:
      "We can't find this order, or you don't have access to it. If you just placed an order, check your email for the confirmation link.",
    icon: '?',
  },
};

export const ConfirmationPage: React.FC<ConfirmationPageProps> = ({
  orderId,
  authToken,
  onNavigate,
}) => {
  const { uiState, data, isPolling, pollCount, shouldMoveFocus } = useOrderStatus(
    orderId,
    authToken,
  );

  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const liveRegionRef = useRef<HTMLDivElement | null>(null);
  const announcedStateRef = useRef<OrderUiState | null>(null);

  // A11y spec: move focus ONLY on failed_post_auth / not_found_or_forbidden.
  useEffect(() => {
    if (shouldMoveFocus && headingRef.current) {
      headingRef.current.focus();
    }
  }, [shouldMoveFocus, uiState]);

  // A11y spec: announce every UI state change via polite live region.
  // Skip the very first announcement to avoid double-speaking initial render.
  useEffect(() => {
    if (!uiState) return;
    if (announcedStateRef.current === uiState) return;
    const first = announcedStateRef.current === null;
    announcedStateRef.current = uiState;
    if (first) return;
    if (liveRegionRef.current) {
      // Clearing then setting forces SR re-announce in some engines (NVDA quirk).
      liveRegionRef.current.textContent = '';
      // setTimeout 0 to allow DOM mutation observers to flush.
      setTimeout(() => {
        if (liveRegionRef.current) {
          liveRegionRef.current.textContent = COPY[uiState].h1;
        }
      }, 0);
    }
  }, [uiState]);

  // Loading shell before first response — aria-busy + skeleton.
  if (!uiState) {
    return (
      <main aria-labelledby="conf-loading-h1" className="confirmation-page">
        <div role="status" aria-live="polite" aria-busy="true" className="skeleton">
          <h1 id="conf-loading-h1" data-testid="confirmation-heading" tabIndex={-1}>
            Loading your order
          </h1>
          <p>Please wait while we retrieve your booking.</p>
        </div>
      </main>
    );
  }

  const copy = COPY[uiState];
  const isError = uiState === 'failed_post_auth' || uiState === 'not_found_or_forbidden';
  const isDelayed = uiState === 'reconciliation_delayed';

  // QA test-hook contract (focus-and-live-region-policy.md §QA Test-Hook Contract):
  // poll-state mirrors the state machine for Playwright assertions. Single attribute,
  // values: pending | terminal-success | terminal-error | reconciliation_delayed.
  const pollStateAttr: 'pending' | 'terminal-success' | 'terminal-error' | 'reconciliation_delayed' =
    uiState === 'pending_reconciliation' ? 'pending'
    : isDelayed ? 'reconciliation_delayed'
    : isError ? 'terminal-error'
    : 'terminal-success';

  return (
    <main aria-labelledby="conf-h1" className={`confirmation-page state-${uiState}`}>
      {/* QA test hook — mirrors state machine, visually hidden, no a11y impact */}
      <span
        data-testid="poll-state"
        data-state={pollStateAttr}
        aria-hidden="true"
        className="sr-only"
      />

      {/* Polite live region for state transition announcements */}
      <div
        ref={liveRegionRef}
        data-testid="live-region-status"
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className="sr-only"
      />

      <header className="confirmation-header">
        <span aria-hidden="true" className={`state-icon icon-${uiState}`}>
          {copy.icon}
        </span>
        <h1 id="conf-h1" ref={headingRef} data-testid="confirmation-heading" tabIndex={-1}>
          {copy.h1}
        </h1>
        <p className="state-body">{copy.body}</p>
      </header>

      {/* Order number — shown for all non-error states */}
      {!isError && data && (
        <section aria-labelledby="order-num-h2" className="order-number">
          <h2 id="order-num-h2">Order number</h2>
          <p className="order-id-value">
            <code aria-label={`Order number ${data.orderId}`}>{data.orderId}</code>
          </p>
        </section>
      )}

      {/* Pending: aria-busy on the polling indicator */}
      {uiState === 'pending_reconciliation' && (
        <section
          aria-labelledby="poll-h2"
          aria-busy={isPolling ? 'true' : 'false'}
          className="polling-indicator"
        >
          <h2 id="poll-h2" className="sr-only">
            Confirmation progress
          </h2>
          <p>
            Checking status… (attempt {pollCount} of 10)
          </p>
        </section>
      )}

      {/* Delayed state: provide email reassurance + safe close */}
      {isDelayed && (
        <section aria-labelledby="delayed-h2" className="delayed-actions">
          <h2 id="delayed-h2">What happens next</h2>
          <ul>
            <li>We'll email you when confirmation completes (within 15 minutes).</li>
            <li>You can safely close this page — no further action needed.</li>
            <li>
              Need help?{' '}
              <a href="/support" onClick={(e) => {
                if (onNavigate) { e.preventDefault(); onNavigate('/support'); }
              }}>
                Contact support
              </a>
            </li>
          </ul>
        </section>
      )}

      {/* Error states: actionable recovery paths.
          The h1 already carries the error message and receives focus (policy §3).
          We also expose a role=alert mirror with data-testid="live-region-error" so
          QA's forbidden-patterns.spec.ts can assert assertive announcement without
          requiring two competing focus targets. The mirror is sr-only — visual users
          see the h1, AT users get one announcement (h1 focus + alert is deduped by
          most AT because content matches). */}
      {isError && (
        <div
          role="alert"
          data-testid="live-region-error"
          className="sr-only"
        >
          {copy.h1}
        </div>
      )}
      {isError && (
        <section aria-labelledby="error-actions-h2" className="error-actions">
          <h2 id="error-actions-h2">What you can do</h2>
          {uiState === 'failed_post_auth' && (
            <ul>
              <li>Your card was not charged.</li>
              <li>
                <a
                  href="/cart"
                  onClick={(e) => {
                    if (onNavigate) {
                      e.preventDefault();
                      onNavigate('/cart');
                    }
                  }}
                >
                  Return to cart and try again
                </a>
              </li>
              <li>
                <a
                  href="/support"
                  onClick={(e) => {
                    if (onNavigate) {
                      e.preventDefault();
                      onNavigate('/support');
                    }
                  }}
                >
                  Contact support
                </a>
              </li>
            </ul>
          )}
          {uiState === 'not_found_or_forbidden' && (
            <ul>
              <li>Check the link in your confirmation email.</li>
              <li>
                <a
                  href="/"
                  onClick={(e) => {
                    if (onNavigate) {
                      e.preventDefault();
                      onNavigate('/');
                    }
                  }}
                >
                  Return to home
                </a>
              </li>
            </ul>
          )}
        </section>
      )}

      {/* Confirmed state: next steps */}
      {uiState === 'confirmed' && (
        <section aria-labelledby="next-h2" className="next-steps">
          <h2 id="next-h2">Next steps</h2>
          <ul>
            <li>Check your email for the full itinerary.</li>
            <li>
              <a
                href="/account/trips"
                onClick={(e) => {
                  if (onNavigate) {
                    e.preventDefault();
                    onNavigate('/account/trips');
                  }
                }}
              >
                View your trips
              </a>
            </li>
          </ul>
        </section>
      )}
    </main>
  );
};

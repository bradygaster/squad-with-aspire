// RefundButton.tsx
// Server-driven eligibility — button is ABSENT (not disabled) when not eligible.
// Pinned: UX spec § 3.1, § 5.1. QA seam: RefundModal.spec.ts test #1.
//
// Visibility rule:
//   order.eligibleActions includes 'refund_full' → render
//   otherwise → render NOTHING (no tooltip, no disabled state, no DOM node)

import React from 'react';

export interface RefundButtonProps {
  order: { orderId: string; eligibleActions: ReadonlyArray<string> };
  onClick: () => void;
}

export const RefundButton: React.FC<RefundButtonProps> = ({ order, onClick }) => {
  if (!order.eligibleActions.includes('refund_full')) return null;
  return (
    <button
      type="button"
      className="refund-button"
      data-testid="refund-trigger-button"
      onClick={onClick}
    >
      Refund order
    </button>
  );
};

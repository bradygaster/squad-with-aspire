"use client";

import { useEffect, useRef } from "react";
import { mountPaymentBridge, type PaymentBridge } from "@/checkout/paymentBridge";

export function PaymentBridgeClient({
  nonce,
  provider,
}: {
  nonce: string;
  provider: "stripe" | "adyen";
}) {
  const iframeRef = useRef<HTMLIFrameElement | null>(null);
  const bridgeRef = useRef<PaymentBridge | null>(null);

  useEffect(() => {
    if (!iframeRef.current) return;
    bridgeRef.current = mountPaymentBridge({
      iframe: iframeRef.current,
      nonce,
      provider,
      onTokenized: async (_evt) => {
        // Do NOT trust iframe-asserted amount/currency/orderId. Server reconciles.
        await fetch("/api/checkout/confirm", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": crypto.randomUUID(),
          },
          body: JSON.stringify({}),
        });
      },
    });
    return () => bridgeRef.current?.dispose();
  }, [nonce, provider]);

  const src =
    provider === "stripe"
      ? "https://js.stripe.com/v3/elements-inner-payment.html"
      : "https://checkoutshopper-live.adyen.com/checkoutshopper/services/PaymentInitiation/v68/embedded";

  return (
    <iframe
      ref={iframeRef}
      src={src}
      title="Payment provider"
      sandbox="allow-scripts allow-forms allow-same-origin"
      style={{ width: "100%", height: 480, border: 0 }}
    />
  );
}

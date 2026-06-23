// WI-1b — Payment step: server-renders nonce from x-payment-bridge-nonce response header,
// mounts the paymentBridge in client. Server reconciles amount/currency/orderId via provider API.

import { headers } from "next/headers";
import { PaymentBridgeClient } from "./PaymentBridgeClient";

export const dynamic = "force-dynamic";

export default async function PaymentPage() {
  const h = await headers();
  const nonce = h.get("x-payment-bridge-nonce") ?? "";
  const provider = (h.get("x-payment-provider") ?? "stripe") as "stripe" | "adyen";

  if (!nonce) {
    return (
      <main>
        <h1>Payment unavailable</h1>
        <p>Please retry or contact support.</p>
      </main>
    );
  }

  return (
    <main>
      <h1>Payment</h1>
      <PaymentBridgeClient nonce={nonce} provider={provider} />
    </main>
  );
}

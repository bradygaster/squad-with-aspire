# Travel-Assistant Checkout Flow — Design Spec

**Owner:** experience-design-squad
**Status:** Ready for build
**Date:** 2026-06-23

> Note: GitHub issue creation on `tamirdresher/travel-assistant` is blocked by EMU policy. This spec is shared as an in-repo artifact for the application-development-squad to consume.

---

## 1. UX Flow & Wireframes — Iris (UX Lead)

### Flow Diagram

```
                          START
                            │
              ┌─────────────┴─────────────┐
           SIGNED-IN                    GUEST
              └─────────────┬─────────────┘
                            │
                       CART REVIEW
                            │
                    TRAVELER DETAILS  ← prefill if signed-in
                            │
                         PAYMENT
                ┌───────────┼───────────────┐
              SUCCESS   CARD DECLINED   SESSION TIMEOUT
                │           │                │
                │       (inline retry)   (modal → restore draft)
                ▼
                         REVIEW
                            │
                ┌───────────┴───────────┐
            CONFIRM               INVENTORY GONE
                │              (release hold, alternates)
                ▼
                       CONFIRMATION
```

Branching: guest vs signed-in differs only at TRAVELER DETAILS (prefill) and on CARD DECLINED (signed-in offers saved cards; guest offers login prompt).

### Wireframes (mobile-first)

**1. CART REVIEW**
```
┌──────────────────────┐
│ ← CHECKOUT           │
├──────────────────────┤
│ Trip: NYC 3 nights   │
│ $450 × 2 travelers   │
│ [ ] Insurance $29    │
│ Subtotal: $929       │
│ [CONTINUE]           │
└──────────────────────┘
```
Desktop: 2-col, sticky order summary sidebar.

**2. TRAVELER DETAILS**
```
┌──────────────────────┐
│ ← BACK               │
│ Traveler 1 of 2      │
│ Name  [_____________]│
│ Email [_____________]│
│ Phone [_____________]│
│ □ Same as signee     │
│ [NEXT] [SAVE DRAFT]  │
└──────────────────────┘
```

**3. PAYMENT**
```
┌──────────────────────┐
│ ← BACK               │
│ ○ New Card           │
│ ○ Saved (signed-in)  │
│ Card [VISA] •••4242  │
│ Exp [__/__] CVC [__] │
│ [PAY $929]           │
│ [PayPal] [Apple Pay] │
└──────────────────────┘
```

**4. REVIEW**
```
┌──────────────────────┐
│ CONFIRM              │
│ ✓ 2 travelers        │
│ ✓ Visa ending 4242   │
│ ✓ Depart Jun 25      │
│ □ I agree to Terms   │
│ [CONFIRM] [EDIT]     │
└──────────────────────┘
```

**5. CONFIRMATION**
```
┌──────────────────────┐
│ ✓ ORDER COMPLETE     │
│ Order #TR-2026-0847  │
│ [VIEW ITINERARY]     │
│ [DOWNLOAD RECEIPT]   │
└──────────────────────┘
```

### State Management
- **Cart:** LocalStorage + IndexedDB (offline resilience).
- **Session:** JWT, 30-min timeout, 5-min warning modal.
- **Inventory hold:** 15-min server-side reservation, released on cancel/fail.
- **Draft recovery:** Auto-saved at TRAVELER DETAILS; restored after timeout.

> *"The best checkout isn't invisible — it's invisible to the anxious traveler."* — Iris

---

## 2. Interaction & Visual Design — Vela

### Progress Indicator
- **Desktop:** Horizontal 5-step stepper with labels (Cart → Details → Payment → Review → Done). Active = primary color, completed = checkmark.
- **Mobile:** Compact "Step 2/5" with dot indicators; label below.
- **a11y:** `role="progressbar"` + `aria-current="step"`.

### Micro-Interactions
- **Inline validation:** on blur, 500ms debounce before showing errors. Success checkmark on valid.
- **Button states:** idle / loading (300ms spinner fade-in) / disabled (50% opacity) / success (1.2s checkmark, auto-advance 800ms) / error (100ms shake, red border).
- **Focus:** 2px primary-color ring, focus-visible only.
- **Optimistic UI (Add to cart):** instant counter increment; 3s undo toast if sync fails.

### Error Feedback
- **Inline field:** red border + icon + 12px message below.
- **Banner:** sticky dismissible alert for multi-field issues.
- **Session timeout:** centered modal with focus trap, countdown, [Login Again] / [Discard].

### Design Tokens
- **Spacing:** 8px base (4, 8, 12, 16, 24, 32, 48, 64).
- **Type:** body 16/1.5; h1–h3 = 28/24/20 bold.
- **Color roles:** primary `#0066CC`, danger `#DC3545`, success `#28A745`, warning `#FFC107`, neutral bg `#F5F5F5`, text `#333`.

> *"Design the silence between clicks as thoughtfully as the click itself."* — Vela

---

## 3. WCAG 2.2 AA Checklist — Orin (Accessibility)

### Keyboard
- [ ] Logical tab order across all 5 steps.
- [ ] Focus indicator ≥3:1 contrast on every interactive element.
- [ ] Skip-to-main on each page.
- [ ] Escape closes modals; focus returns to trigger.
- [ ] No keyboard trap, including payment iframe handoff.

### Screen Reader
- [ ] `<label>` on every input (not placeholder-only).
- [ ] Errors announced via `aria-live="polite"` within 1s.
- [ ] Step progress: `aria-current="step"`, read as "Step 2 of 5, Traveler Details".
- [ ] Payment iframe has descriptive `title`.
- [ ] Detected card type announced ("Visa detected").

### Contrast
- [ ] Text ≥4.5:1, UI/borders ≥3:1.
- [ ] Errors never color-only — icon + text + aria-live.

### Forms
- [ ] Autocomplete: `cc-number`, `cc-exp-month`, `cc-exp-year`, `cc-csc`, `given-name`, `family-name`, `email`, `tel`.
- [ ] `inputmode="numeric"` for card/phone; `inputmode="email"` for email.
- [ ] `aria-describedby` links field → error message.
- [ ] `aria-required="true"` + visual asterisk.

### WCAG 2.2 New SCs
- [ ] **2.4.11 Focus Not Obscured** — sticky headers / payment widgets don't cover focused element.
- [ ] **2.5.7 Dragging Alternatives** — card reorder offers button alternatives.
- [ ] **3.2.6 Consistent Help** — help link same location every page.
- [ ] **2.5.8 Target Size** — interactive targets ≥24×24 CSS px.

### Sign-off
- ✓ Full screen-reader pass (NVDA + JAWS + VoiceOver).
- ✓ Full keyboard-only completion.
- ✓ Axe/WAVE clean + manual contrast verified.

> *"Accessibility is not a feature — it's the foundation of trust."* — Orin

---

## 4. Competitor Pattern Scan — Pell (Research)

| Platform | Steps | Guest Checkout | Payment Options | Standout Pattern |
|---|---|---|---|---|
| **Booking.com** | 9 (linear) | ✅ | Cards, PayPal, Apple Pay, Klarna, BNPL, Pay-at-property | Explicit ~15-min room lock — reduces overbooking anxiety |
| **Expedia** | 5–6 | ✅ | Cards, Pay-at-property, Affirm/Klarna, regional wallets | BNPL surfaced prominently at payment selection |
| **Airbnb** | 3–4 | ✅ | Cards, PayPal, Apple/Google Pay, Klarna, Alipay | Payment held by Airbnb, released 24h post check-in |

### Recommendations for travel-assistant
1. **Adopt minimalist step count** — target Airbnb's 3–4 effective steps (we've designed 5 with optional cart review).
2. **Surface BNPL** — show Affirm/Klarna at payment step to capture budget-conscious segment and lift AOV.
3. **Frictionless guest checkout** — never require account creation before payment.
4. **Trust signals** — adopt visible inventory hold messaging (Booking-style) to reduce abandonment.
5. **Mobile-first** — all wireframes designed mobile-first; ensure autofill works on iOS/Android.

> *"The best checkout is the one guests forget they're in."* — Pell

---

## 5. Information Architecture — Cass

- **URL structure:** `/checkout/cart`, `/checkout/travelers`, `/checkout/payment`, `/checkout/review`, `/checkout/confirmation/{orderId}`.
- **Back-button safety:** every step survives browser back without re-submitting.
- **Persistence keys:** `cart`, `travelers`, `paymentMethodId` (NOT raw card data).
- **Deep-link policy:** later steps redirect to earliest incomplete step if prerequisites missing.

> *"Structure is the silent UI — invisible when right, painful when wrong."* — Cass

---

## Handoff Notes for application-development-squad

- Use these wireframes + tokens as the **source of truth** for Dallas (Frontend).
- API contract (Ripley) must expose endpoints that match the 5-step flow with a `step` field on session state so frontend can resume mid-flow.
- Form validation strategy: client-side immediate (Vela's debounce rules) + server-side authoritative on submit.
- Coordinate with security-hardening-squad: payment fields go through provider-hosted iframe (PCI scope minimization); JWT 30-min expiry matches our session timeout.

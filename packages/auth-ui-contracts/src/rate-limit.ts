export type RateLimitScope = "ip" | "account" | "global";

export interface RateLimitedBody {
  code: "RATE_LIMITED";
  message: string;
  retryAfterSeconds: number;
  scope: RateLimitScope;
}

export interface ReconciledRateLimit {
  retryAfterSeconds: number;
  scope: RateLimitScope;
  clamped: boolean;
  source: "body" | "header" | "fallback";
}

export const MIN_COOLDOWN_S = 1;
export const MAX_COOLDOWN_S = 3600;

export function reconcileRateLimit(
  body: Partial<RateLimitedBody> | null | undefined,
  headerRetryAfter: string | number | null | undefined,
): ReconciledRateLimit {
  let source: ReconciledRateLimit["source"] = "fallback";
  let raw: number = NaN;

  if (body && typeof body.retryAfterSeconds === "number" && Number.isFinite(body.retryAfterSeconds)) {
    raw = body.retryAfterSeconds;
    source = "body";
  } else if (headerRetryAfter !== null && headerRetryAfter !== undefined) {
    const parsed = typeof headerRetryAfter === "number"
      ? headerRetryAfter
      : parseInt(String(headerRetryAfter), 10);
    if (Number.isFinite(parsed)) {
      raw = parsed;
      source = "header";
    }
  }

  let clamped = false;
  let value = raw;
  if (!Number.isFinite(value) || value < MIN_COOLDOWN_S) {
    value = MIN_COOLDOWN_S;
    clamped = true;
  } else if (value > MAX_COOLDOWN_S) {
    value = MAX_COOLDOWN_S;
    clamped = true;
  }

  const scope: RateLimitScope = body?.scope ?? "global";
  return { retryAfterSeconds: value, scope, clamped, source };
}

export function shouldAnnounce(prev: number | null, current: number): boolean {
  if (prev === null) return true;
  if (current <= 0 && prev > 0) return true;
  if (current <= 10 && prev > 10) return true;
  if (current <= 30 && prev > 30) return true;
  return false;
}

export function rateLimitCopy(scope: RateLimitScope, seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  switch (scope) {
    case "ip":
      return `Too many attempts from this network. Try again in ${s}s.`;
    case "account":
      return `Too many attempts for this account. Try again in ${s}s.`;
    case "global":
      return `Service is throttled. Try again in ${s}s.`;
  }
}

export function submitControlState(secondsRemaining: number): {
  ariaDisabled: boolean;
  disabledAttr: false;
  liveRegionCleared: boolean;
} {
  const cooling = secondsRemaining > 0;
  return {
    ariaDisabled: cooling,
    disabledAttr: false,
    liveRegionCleared: !cooling,
  };
}

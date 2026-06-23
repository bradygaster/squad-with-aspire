// Framework-agnostic pure helpers for the "remember me" auth flow.
// Decisions still pending (do not hard-code in tests until locked):
//   RM-002: form selector / DOM contract (experience-design-squad)
//   RM-005: storage decision — httpOnly cookie vs localStorage (security-hardening-squad)
//
// What IS contract today (per ideation-research-planning-squad RM plan):
//   - checkbox default = unchecked
//   - unchecked  -> short-lived token, MUST live in sessionStorage (cleared on tab close)
//   - checked    -> long-lived token, stored in the RM-005 store with explicit expiry
//   - backend issues short vs long TTL based on the flag
//   - /refresh preserves the flag across rotations
//   - logout revokes both paths
//   - password change invalidates all long-lived tokens for the user

export type RememberMe = boolean;

export const SHORT_TTL_SECONDS = 60 * 60;           // 1h
export const LONG_TTL_SECONDS  = 60 * 60 * 24 * 30; // 30d

export interface IssueTokenInput {
  userId: string;
  rememberMe: RememberMe;
  now: number;
}

export interface IssuedToken {
  userId: string;
  rememberMe: RememberMe;
  issuedAt: number;
  expiresAt: number;
  ttlSeconds: number;
}

export function ttlFor(rememberMe: RememberMe): number {
  return rememberMe ? LONG_TTL_SECONDS : SHORT_TTL_SECONDS;
}

export function issueToken(input: IssueTokenInput): IssuedToken {
  if (!input.userId) throw new Error("userId required");
  if (!Number.isFinite(input.now) || input.now < 0) throw new Error("invalid now");
  const ttl = ttlFor(input.rememberMe);
  return {
    userId: input.userId,
    rememberMe: input.rememberMe,
    issuedAt: input.now,
    expiresAt: input.now + ttl,
    ttlSeconds: ttl,
  };
}

export function refreshToken(prev: IssuedToken, now: number): IssuedToken {
  if (now < prev.issuedAt) throw new Error("clock skew: now < issuedAt");
  return issueToken({ userId: prev.userId, rememberMe: prev.rememberMe, now });
}

export type StorageKind = "sessionStorage" | "localStorage" | "cookie";

export function targetStoreFor(
  rememberMe: RememberMe,
  longLivedStore: Exclude<StorageKind, "sessionStorage">,
): StorageKind {
  return rememberMe ? longLivedStore : "sessionStorage";
}

export function storesToClearOnLogout(
  longLivedStore: Exclude<StorageKind, "sessionStorage">,
): StorageKind[] {
  return ["sessionStorage", longLivedStore];
}

export function tokensInvalidatedOnPasswordChange<T extends IssuedToken & { id: string }>(
  userTokens: readonly T[],
): string[] {
  return userTokens.filter(t => t.rememberMe).map(t => t.id);
}

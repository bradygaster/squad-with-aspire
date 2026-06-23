# Storage-key reconcile — DECISION LOCKED

**Decision owner:** review-deployment-squad (release captain, dark-mode v0.5.0)
**Decision date:** 2026-06-23
**Decision:** `ta:theme:v1` wins. App-dev revs the patch. Gate stays as-is.

---

## Why this direction (and not the other)

| Factor | `ta:theme:v1` (release-captain lock) | `ta.theme` (app-dev patch) |
|---|---|---|
| Version suffix protects future schema breaks | ✅ yes | ❌ no |
| Already enforced by `.github/workflows/dark-mode-gate.yml` `contract-invariants` job (eb6b8c5) | ✅ yes — required check, branch-protection candidate | ❌ would force gate downgrade |
| Already documented in `docs/dark-mode/RELEASE-v0.5.0.md` §1 row 5 + §3 release notes | ✅ yes | ❌ would force doc rev |
| Already in DM-005 sec-hard contract (semgrep `auth-ui-token-hygiene` + `e193c39` storage gate) | Confirmed `e193c39` exists only on squad-with-aspire safety mirror; the live travel-assistant storage.ts is the one app-dev's patch touches | n/a |
| Number of lines to change | 2 (noFoucScript.ts string literal + storage.ts const) + ~8 in test setup | dozens (gate workflow, release notes, threat model, runbook, sign-off ledger) |

**Net:** revving 2 source-of-truth lines is cheaper than revving 5+ gate/release artifacts. Release captain holds the lock.

---

## What app-dev needs to change (verbatim diff fragments)

App-dev applies the following to `feature/dm-002-dm-003-theme-toggle` (HEAD `b831f26`), produces `b831f26..NEW_SHA`, and re-bundles. No new file ops, no new dependencies, no new tests required — only string-literal edits + test-string updates.

### 1. `apps/web/src/theme/types.ts` (or wherever `THEME_STORAGE_KEY` is exported)

```diff
-export const THEME_STORAGE_KEY = 'ta.theme';
+export const THEME_STORAGE_KEY = 'ta:theme:v1';
```

If the constant is not exported (i.e. each file inlines the literal), export it from `./types` and import it from `noFoucScript.ts` + `storage.ts`. This is desirable for cross-cutting renames anyway. Either pattern is acceptable; the literal must end up as `ta:theme:v1` everywhere it appears.

### 2. `apps/web/src/theme/noFoucScript.ts`

Inside the 463-byte IIFE — wherever the literal `'ta.theme'` appears as the `localStorage.getItem` argument, change to `'ta:theme:v1'`. The byte budget remains under 500 (new literal is 4 bytes longer; current IIFE is 463 B → 467 B, still safe).

```diff
-    var c = localStorage.getItem('ta.theme');
+    var c = localStorage.getItem('ta:theme:v1');
```

### 3. `apps/web/src/lib/storage.ts` (DM-005 surface)

Whatever constant or literal `getThemeChoice` / `setThemeChoice` reads/writes must become `ta:theme:v1`. If sec-hard's `e193c39` is not actually on the live travel-assistant branch (this APPLY.md previously flagged it as squad-with-aspire-only), app-dev's local storage.ts is the only live source and the rename happens there.

### 4. `apps/web/src/theme/__tests__/noFoucScript.test.ts` + any other storage-key test setup

The 8 vitest cases mock `localStorage` with the storage key as a string key. Every occurrence of `'ta.theme'` in test setup → `'ta:theme:v1'`. Expected count: ~8 occurrences (one per case's `localStorage.setItem` priming line).

### 5. Re-bundle

```pwsh
# in app-dev's local clone, feature branch checked out
git add -A
git commit -m "fix(dark-mode): rename storage key ta.theme -> ta:theme:v1 (release-captain lock)"
git format-patch b831f26~1..HEAD -o ..\dm-002-dm-003-pr\
git bundle create ..\dm-002-dm-003-pr\dm-002-dm-003.bundle feature/dm-002-dm-003-theme-toggle
```

App-dev updates `upstream-transplants/dark-mode-DM-002-DM-003/APPLY.md` source commit + patch byte-counts when the rev is done.

---

## Gate behavior after reconcile

Once the patch is applied to PR #28 and CI runs:

- `dark-mode-gate.yml::contract-invariants` — passes (grep for `ta:theme:v1` succeeds).
- `dark-mode-gate.yml::build-and-test` — passes (vitest sees consistent storage key end-to-end).
- `dark-mode-gate.yml::csp-and-threat-model` — unaffected (orthogonal to storage key).
- `dark-mode-gate.yml::gate` aggregator — green → branch-protection-candidate, PR #28 mergeable.

No changes required to any release-captain or sec-hard artifact. The gate is the source of truth; the gate says `ta:theme:v1`.

---

## Maintainer apply order (updated)

1. Wait for app-dev to publish revved patchset (subject prefix `fix(dark-mode): rename storage key`).
2. Apply DM-001 XD refinement (`upstream-transplants/dark-mode-DM-001/APPLY.md`).
3. Apply DM-002+DM-003 revved patchset (`upstream-transplants/dark-mode-DM-002-DM-003/APPLY.md`).
4. Push to `dm-001-design-tokens` (the integration branch backing PR #28).
5. Verify `dark-mode-gate.yml::gate` is green on PR #28.
6. Merge PR #28 to main (squash, subject locked per `docs/dark-mode/RELEASE-v0.5.0.md` §2).
7. Tag `v0.5.0` per release-captain runbook.

---

## Sign-off

- **Review-deployment-squad** (release captain): decision locked above. Gate not changing.
- **Application-development-squad**: rev requested — 2 source files + test setup string updates. No new deps, no new tests, no new ops.
- **Security-hardening-squad**: notified via planning routing. semgrep `auth-ui-token-hygiene` regex on `'ta:theme:v1'` is the live rule; no rule changes needed.
- **Experience-design-squad**: no surface impact (tokens + wireframes don't reference storage key).
- **Quality-testing-squad**: DM-004 contract suite (`b81f74e`) already asserts `ta:theme:v1` — confirms gate alignment.
- **Azure-infrastructure-squad**: N/A.
- **Ideation-research-planning-squad**: no IRP action — release-captain decision within scope.

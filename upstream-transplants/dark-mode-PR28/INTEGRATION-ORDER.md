# PR #28 (`dm-001-design-tokens`) — Final integration order

Release-captain lock. Maintainer applies in **this exact order** on top of current `dm-001-design-tokens` HEAD. All three are pure-additive or string-rename; no conflicts expected.

| # | Patch | Source | Subject prefix | Files |
|---|-------|--------|----------------|-------|
| 1 | App-dev v2 (storage-key rev) | `squads/application-development/.squad/session-state/.../files/` (forthcoming, replaces `b831f26`) | `fix(dark-mode): rename storage key to ta:theme:v1` | `apps/web/src/theme/types.ts`, `noFoucScript.ts`, `lib/storage.ts`, `theme/__tests__/noFoucScript.test.ts` |
| 2 | XD contrast matrix | `squads/experience-design/.squad/session-state/b530c00a-502a-44e3-bdac-87ca6dbc1361/files/0001-DM-001-follow-up-add-DM-004-contrast-verification-ma.patch` | `docs(design): add DM-004 contrast verification matrix` | `docs/design/dark-mode-tokens.md` (+49 lines, append-only) |

## Why this order

1. **App-dev v2 first** — `ta:theme:v1` rename is the only blocker keeping `contract-invariants` red. Lands source-of-truth flip before any other touch.
2. **XD contrast matrix second** — purely additive to `docs/design/dark-mode-tokens.md`. Lifts DM-004's last skipped test (WCAG AA validator) to green. No code paths touched.
3. **No reorder permitted** — patch 1 modifies test files; patch 2 modifies only docs.

## Gate verification after both apply

- `dark-mode-gate / contract-invariants` — flips green on patch 1 (`ta:theme:v1` literal match)
- `dark-mode-gate / build-and-test` — pnpm lint/typecheck/test/build green
- `dark-mode-gate / csp-and-threat-model` — already green (`DM-005: APPROVED` marker present)
- `dark-mode-gate / gate` (aggregator) — green when above three pass
- QT `tests/unit/theme/` suite: **39/39 green** (was 38/39 with 1 skip)

## Squash subject (locked)

```
feat(web): dark-mode toggle (light | dark | system) — closes DM-001 DM-002 DM-003 DM-005

DM-001: design tokens + contrast verification matrix
DM-002: ThemeProvider + ThemeToggle (APG radiogroup, single tab stop)
DM-003: no-FOUC boot script (~467B post-rename, CSP-hardness clean, sha256 emitted)
DM-005: storage hygiene + semgrep rules + threat model approved
DM-004: contract test suite (39/39 green)
DM-006: client telemetry DEFERRED to follow-up (az-infra owner)

Storage key: ta:theme:v1
CSP directive: 'sha256-<emitted by prebuild>'

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

## Six-squad sign-off ledger

| Squad | Artifact | SHA | Status |
|-------|----------|-----|--------|
| ideation-research-planning | DM-006 deferral IRP ratification | inline | ✅ |
| experience-design | DM-001 tokens + DM-001 contrast matrix | `c31d897` + xd `8a17db7` | ✅ |
| application-development | DM-002+DM-003 base + storage-key v2 rev | `460caf9` + v2 (forthcoming) | ⏳ patch 1 |
| security-hardening | DM-005 + semgrep + threat model | `e193c39` (squad-mirror) | ✅ |
| quality-testing | DM-004 contract suite | `5b87243` | ✅ pending app-dev v2 |
| review-deployment | dark-mode-gate workflow + this doc | `eb6b8c5` + this commit | ✅ |
| azure-infrastructure | N/A | — | N/A |

## Rollback

Per `docs/dark-mode/RELEASE-v0.5.0.md` §5. `git revert <squash-sha>` on `main`, redeploy. No storage migration (key only ever read, never written by prior release).

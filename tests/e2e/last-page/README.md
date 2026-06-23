# LP-006 — Remember Last Viewed Page (QT test bundle)

Owner: quality-testing-squad
Plan: LP-001..LP-006 in ideation-research-planning brief

## Locked invariants (smoke-gate enforced)

| Key | Value |
|---|---|
| Storage key | `ta.nav.lastPage.v1` |
| Opt-out key | `ta.nav.lastPage.optOut.v1` |
| Max payload | 2048 bytes |
| Max path | 1024 bytes |
| Deny-list | see `DENY_PATH_PREFIXES` |
| Token query keys | see `TOKEN_QUERY_KEYS` |

If you change any of these, you change the smoke gate too.

## Artifacts

- `packages/last-page-contract/src/last-page.ts` — pure helpers
- `tests/regression/last-page-setLastPage-property.test.ts` — property tests
- `tests/regression/last-page-validator.test.ts` — validator unit tests
- `tests/e2e/last-page/restore.spec.ts` — Playwright S1–S8 (env-gated)
- `.github/workflows/last-page-gate.yml` — required check

## Status

| Scenario | Status | Blocker |
|---|---|---|
| S1 deep restore | LIVE | — |
| S2 auth-gated drop | fixme | LP-003 auth hook |
| S3 deny-list /login | LIVE | — |
| S4 token deny | LIVE | — |
| S5 external deep-link skip | LIVE | — |
| S6 opt-out clears | fixme | LP-002 settings selector |
| S7 404 toast | LIVE | needs app-dev toast wiring |
| S8 no FOWP | LIVE | needs app-dev `<root-skeleton>` |

## Coordination

- **app-dev**: implement `apps/web/src/navigation/setLastPage.ts` re-exporting
  from `@ta/last-page-contract`. Anything else trips the grep-guard.
- **xd (LP-002)**: deliver opt-out toggle selector + label copy; QT will un-fixme S6.
- **sec-hard (LP-005)**: land `docs/security/last-viewed-page-threat-model.md`
  with `LP-005: APPROVED` marker; otherwise gate fails.
- **rev-deploy**: transplant via `upstream-transplants/last-page/` bundle.

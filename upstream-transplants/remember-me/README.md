# upstream-transplants/remember-me/

Release-deployment squad owns this directory. EMU blocks XD, app-dev, az-infra,
sec-hard, and QT from pushing directly to `tamirdresher/travel-assistant`, so
each squad lands its work here as a patch + APPLY.md pair under one of:

- `RM-002-xd-checkbox/`
- `RM-003-client-persist/`
- `RM-004-api-token-ttl/`
- `RM-005-sec/`

Release-deployment then applies them onto `feature/remember-me` in the order
defined by `MERGE-RUNBOOK.md` §1, opens PR against `tamirdresher/travel-assistant:main`
once §2 gates pass, runs §3 smoke, and squash-merges with the §4 subject.

**No release tag** — feature rides the next routine deploy. See dark-mode
`upstream-transplants/dark-mode-PR28/` for the precedent pattern (with tag);
remember-me follows the same shape minus §4 tag-cut and `gh release create`.

Required workflow: `remember-me-gate.yml` (in this directory). Maintainer copies
it to `.github/workflows/` on `feature/remember-me` when applying the first patch.

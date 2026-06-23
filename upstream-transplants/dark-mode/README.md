# Dark-Mode v0.5.0 — Upstream Transplant Bundle

**Target repo:** `tamirdresher/travel-assistant` (active token has pull-only — EMU block).
**Owner:** review-deployment-squad (release captain for DM-007).
**Status:** ready for maintainer apply — zero modifications needed.

## Why this bundle exists

The squad's active GH identity (`tamirdresher_microsoft`, EMU) holds `pull` only on `tamirdresher/travel-assistant`. The merge gate workflow + release runbook must land in that repo before DM-001..005 PRs open, so review-deployment ships them here under the same `upstream-transplants/` pattern used for `squad-1372/`, `team-retro/`, and `auth-verify-email/`.

## What's in `files/`

| Repo destination                              | Source                                | Purpose                                                          |
|-----------------------------------------------|---------------------------------------|------------------------------------------------------------------|
| `.github/workflows/dark-mode-gate.yml`        | `files/.github/workflows/...`         | Required check on PRs touching dark-mode paths (4 jobs + gate)  |
| `docs/dark-mode/RELEASE-v0.5.0.md`            | `files/docs/dark-mode/...`            | Release runbook: merge order, smoke, rollback, sign-off ledger  |

## One-shot apply (maintainer, in a `tamirdresher/travel-assistant` clone with admin)

```bash
# from this repo (squad-with-aspire) root:
SRC=upstream-transplants/dark-mode/files
DST=/path/to/travel-assistant

cp -R "$SRC/." "$DST/"
cd "$DST"
git checkout -b ci/dark-mode-merge-gate
git add .github/workflows/dark-mode-gate.yml docs/dark-mode/RELEASE-v0.5.0.md
git commit -m "ci(dark-mode): merge gate + release runbook for v0.5.0 (DM-007)"
git push -u origin ci/dark-mode-merge-gate
gh pr create --fill --base main \
  --title "ci(dark-mode): merge gate + release runbook for v0.5.0 (DM-007)" \
  --body "From upstream-transplants/dark-mode in squad-with-aspire. Required check name: dark-mode-gate / gate."
```

After merge, **add `dark-mode-gate / gate` as a required check** on the `main` branch protection rule so DM-001..005 PRs gate on it.

## Squad sign-off (this bundle only — DM-007 scope)

- review-deployment-squad: ✅ (this commit)

Other DM-NNN sign-offs flow through their own PRs against `travel-assistant` once the gate is live.

## Notes for downstream squads

- **experience-design (DM-001):** put tokens under `apps/web/src/styles/tokens/`. The gate's `no hard-coded color hex` check exempts that path.
- **security-hardening (DM-005):** ship `docs/dark-mode/threat-model.md` containing the literal marker `DM-005: APPROVED`, and `docs/dark-mode/csp.md` containing a `sha256-...` hash for the no-FOUC inline script.
- **application-development (DM-002):** export the union `'light' | 'dark' | 'system'` from `apps/web/src/lib/theme/types.ts` and use `localStorage` key `ta:theme:v1`.
- **application-development (DM-003):** no-FOUC script path is `apps/web/src/app/no-foul-script.ts`. The gate refuses to pass if that file exists without DM-005 docs landed.
- **quality-testing (DM-004):** unit + a11y tests must live under `apps/web/src/**/*.test.{ts,tsx}`. The gate runs `pnpm test`, `pnpm lint`, `pnpm exec tsc --noEmit`, `pnpm build`.

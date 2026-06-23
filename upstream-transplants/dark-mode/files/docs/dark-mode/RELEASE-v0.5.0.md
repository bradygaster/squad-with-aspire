# Release Runbook — v0.5.0 "Dark Mode"

**Release captain:** review-deployment-squad
**Repo:** `tamirdresher/travel-assistant`
**Target tag:** `v0.5.0` (next minor — additive, no flag)

---

## 1. Merge order (hard dependencies)

| Step | Issue  | Owner          | Branch hint                       | Blocking gate                          |
|-----:|--------|----------------|-----------------------------------|----------------------------------------|
| 1    | DM-001 | experience-design | `dm/001-tokens-wireframes`     | `dark-mode-gate / contract-invariants` |
| 2    | DM-005 | security-hardening | `dm/005-csp-threat-model`     | `dark-mode-gate / csp-and-threat-model` (must be merged or its docs landed via DM-001 PR) |
| 3    | DM-002 | application-development | `dm/002-theme-provider`  | full `dark-mode-gate` green            |
| 4    | DM-003 | application-development | `dm/003-no-fouc`         | full `dark-mode-gate` + DM-005 CSP hash present |
| 5    | DM-004 | quality-testing  | `dm/004-test-suite`              | runs in parallel with DM-002/003; gates them green |
| 6    | DM-006 | azure-infrastructure | _n/a_                        | **DEFERRED** — no browser telemetry pipeline in repo (no Aspire AppHost, no App Insights/OTel web SDK in `apps/web`, no `instrumentation-client.ts`). Tracked as az-infra follow-up; not blocking v0.5.0. |

Reviewer rule: any squad may review; **only review-deployment-squad squashes**. Exact squash subject:

```
feat(web): dark-mode toggle (light | dark | system) — closes DM-NNN
```

## 2. Required checks (branch protection on `main`)

- `dark-mode-gate / contract-invariants`
- `dark-mode-gate / build-and-test (DM-004)`
- `dark-mode-gate / csp-and-threat-model (DM-005)`
- `dark-mode-gate / gate` ← aggregator, the actual required check

Branch protection JSON payload available at `docs/dark-mode/branch-protection.json` (maintainer applies — squads lack admin).

## 3. Release steps

1. Confirm all 5 required PRs (DM-001..005) merged into `main` with green `gate`. **DM-006 is deferred** — do not block on it.
2. From `main`:
   ```bash
   cd apps/web
   pnpm version minor   # 0.4.x -> 0.5.0
   cd ../..
   git commit -am "release(web): v0.5.0 — dark mode"
   git tag -a v0.5.0 -m "v0.5.0 — dark mode"
   git push origin main --tags
   ```
3. `gh release create v0.5.0 --notes-file docs/dark-mode/release-notes-v0.5.0.md`
4. Run smoke (§5). Only then mark release as `latest`.

## 4. Release notes (paste verbatim into the release body)

```markdown
### ✨ New: Dark mode

Travel-assistant now ships a three-state theme toggle.

- **Light / Dark / System** — `System` follows OS `prefers-color-scheme` and updates live.
- **Persisted** across reloads via `localStorage` (`ta:theme:v1`).
- **No flash of unstyled content** — theme is applied before first paint via a CSP-hashed inline script.
- **Fully keyboard- and screen-reader-accessible** — `aria-pressed` reflects state; reduced-motion respected.
- **Tokens-only** — all colors come from CSS variables; no component owns a literal hex.

No flag, no migration. Hard-reload after upgrading clears any stale cache.

**Telemetry:** `ui.theme.changed` event is **not emitted** in this release; client-side telemetry pipeline is a tracked follow-up (owner: azure-infrastructure-squad). Server-side feature behavior is unaffected.
```

## 5. Post-merge smoke test (15 min, incognito window required)

| ID  | Step                                                              | Expected                                                              |
|-----|-------------------------------------------------------------------|-----------------------------------------------------------------------|
| S1  | Open incognito → load app root                                    | No FOUC; theme matches OS                                             |
| S2  | Toggle to **Light**                                               | Instant repaint; `localStorage['ta:theme:v1'] === 'light'`            |
| S3  | Toggle to **Dark**                                                | Instant repaint; `aria-pressed="true"` on the dark state              |
| S4  | Toggle to **System**                                              | Repaints to OS preference; storage value is `'system'`                |
| S5  | Hard reload (Ctrl+Shift+R)                                        | No FOUC; persisted state restored                                     |
| S6  | OS-level theme flip while on `System`                             | App repaints live (no reload needed)                                  |
| S7  | DevTools → axe scan on toggle component                           | Zero serious/critical violations                                      |
| S8  | DevTools → Network → reload                                       | CSP header present; no inline-script CSP violations in console        |
| S9  | Keyboard nav: Tab to toggle, Space/Enter to cycle states          | Focus ring visible; cycle is light → dark → system → light            |
| S10 | Reduced-motion OS setting on                                      | No transition animations on repaint                                   |

Any S-row red → block release-as-latest and follow §6.

## 6. Rollback (≤10 min)

The feature is additive and unflagged. Rollback = revert merges in reverse order:

```bash
git revert -m 1 <DM-004 SHA>      # tests
git revert -m 1 <DM-003 SHA>      # no-FOUC
git revert -m 1 <DM-002 SHA>      # provider/toggle
git revert -m 1 <DM-005 SHA>      # CSP/threat model
git revert -m 1 <DM-001 SHA>      # tokens/wireframes
git push origin main
git tag -a v0.5.1 -m "v0.5.1 — revert dark mode (rollback)"
git push origin v0.5.1
gh release create v0.5.1 --notes "Rolls back v0.5.0 dark mode."
```

CSS variables are additive; no DB migration; no infra change. **No data loss possible.**

## 7. Squad sign-off ledger

| Squad                       | Item                              | Status   |
|-----------------------------|-----------------------------------|----------|
| experience-design           | DM-001 tokens + wireframes        | pending  |
| security-hardening          | DM-005 CSP + threat model         | pending  |
| application-development     | DM-002 provider/toggle            | pending  |
| application-development     | DM-003 no-FOUC inline script      | pending  |
| quality-testing             | DM-004 test suite                 | pending  |
| azure-infrastructure        | DM-006 telemetry                  | **deferred** — no client telemetry pipeline; az-infra follow-up |
| review-deployment (this doc) | DM-007 release captain           | **ready** |

Flip cells to `green` in PR descriptions as each merges.

# SEC-5b — supply-chain scan + allowlist (CI wiring)

Sibling workflow to `prompt-injection-gate.yml`. Three jobs:

| Job | Tool | Failure threshold |
|---|---|---|
| `vulnerable` | `dotnet list package --vulnerable --include-transitive` | **fail** on High/Critical, **warn** on Moderate |
| `deprecated` | `dotnet list package --deprecated` | warn only (advisory) |
| `allowlist`  | parses `dotnet list package --format json` against `.github/supply-chain-allowlist.yml` | **fail** if any top-level dep is missing from the allowlist |

## MudBlazor — incoming flagged dep

Per relay from ideation-research-planning-squad (2026-06-23): experience-design closed XD-6 → **MudBlazor 7.x**. App-dev will add `AddMudServices()` + MudThemeProvider/Dialog/Snackbar wrap to the web host root.

The allowlist ships with `MudBlazor` pre-registered under **`review_required:`** — the workflow will warn (not fail) when app-dev's PR lands. Security-hardening-squad reviews license/maintainer/CVE history, then opens a follow-up PR moving the entry up to `packages:`. After that, no further CI noise.

XD pre-cleared MudBlazor 7.x latest stable via `dotnet list package --vulnerable` — see ADR-0002 at `squads/experience-design/artifacts/adr/0002-blazor-component-library.md`.

## Self-skip

The `allowlist` job self-skips with a GH notice if `.github/supply-chain-allowlist.yml` is absent. The `vulnerable` and `deprecated` jobs always run. Safe to merge ahead of the allowlist file.

## Install

```bash
cp squads/review-deployment/artifacts/sec-5b-supply-chain/supply-chain-scan.yml \
   .github/workflows/supply-chain-scan.yml
cp squads/review-deployment/artifacts/sec-5b-supply-chain/supply-chain-allowlist.yml \
   .github/supply-chain-allowlist.yml
git add .github/workflows/supply-chain-scan.yml .github/supply-chain-allowlist.yml
git commit -m "ci(sec-5b): supply-chain scan + top-level dep allowlist

- dotnet list package --vulnerable (fail on High/Critical)
- dotnet list package --deprecated (advisory)
- top-level allowlist enforcement
- MudBlazor 7.x pre-registered under review_required pending sec-squad signoff"
```

EMU still blocks me from pushing — patch is the artifact, owner installs.

## Adding a new top-level dep (process)

1. Add the package reference in your PR.
2. Add an entry to `.github/supply-chain-allowlist.yml` under `review_required:` in the same PR (otherwise the `allowlist` job fails).
3. After merge, security-hardening-squad opens a follow-up PR moving the entry to `packages:` once reviewed.

## Tuning thresholds

The `vulnerable` job's pass/fail line is the regex `> .* (High|Critical)`. To allow High-severity to merge with a warning (e.g., during a CVE response window), edit the workflow's `if grep -E ... ; then exit 1` block. Don't relax the Critical gate.

## Why two files, not one

`supply-chain-allowlist.yml` is config (humans edit it often). `supply-chain-scan.yml` is the runner (humans rarely touch it). Splitting them keeps PR diffs scoped and reviewer attention on the actual dep change.

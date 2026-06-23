# Pull Request

## Summary
<!-- One paragraph: what changes and why. -->

## Linked items
- Closes #
- Design / spec link:
- Decision (`.squad/decisions.md` entry, if applicable):

## Changes
-
-
-

## Testing (REL-3 — required)
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated (REQUIRED for any change under `src/**/Booking/**`, `src/**/Payments/**`, `src/**/Auth/**`)
- [ ] LLM eval added/updated (REQUIRED if prompts, model selection, or planner logic changed)
- [ ] Manual smoke against local Aspire AppHost
- [ ] `dotnet test` green locally

## Security checklist (REL-3 — required)
- [ ] No secrets, tokens, or connection strings added to source / config / logs
- [ ] All new external HTTP calls use the typed gateway (no raw `HttpClient` to user-supplied URLs → SSRF)
- [ ] User input is validated before reaching providers or the LLM
- [ ] PII (passport, payment hint, traveler info) is NOT placed in LLM prompts, logs, or telemetry
- [ ] If booking / payments / auth touched: security-hardening-squad reviewer is requested
- [ ] If a new feature flag is introduced: documented in `docs/feature-flags.md` with sunset date

## Reviewer routing (auto via CODEOWNERS)
- [ ] Touches `src/**/Booking|Payments|Auth/**` → security-hardening-squad must approve
- [ ] Touches `infra/**` → azure-infrastructure-squad must approve
- [ ] Touches `.github/workflows/**` → review-deployment-squad must approve

## Rollout
- [ ] Backward-compatible (no breaking API or DB changes), OR
- [ ] Behind a feature flag (`docs/feature-flags.md`) defaulted off in prod
- [ ] Rollback plan: revert PR + revision pin (`docs/runbooks/rollback.md`)

## Checklist
- [ ] Code follows project style (`dotnet format` passes)
- [ ] Documentation updated if applicable
- [ ] No `TODO` / `FIXME` left without a linked issue

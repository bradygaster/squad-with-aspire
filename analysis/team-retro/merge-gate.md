# Team Retro — Merge Gate (TR-001 .. TR-010)

**Owner:** review-deployment-squad
**Status:** Locked
**Date:** 2026-06-23
**Anchors:** `specs/team-retro/PRD.md` (commit `731b737`), `.github/workflows/team-retro-gate.yml`

This is the single source of truth for what merges and what doesn't on any PR
touching `specs/team-retro/**`, `packages/team-retro/**`, `.squad/retros/**`,
or the `retro-action` issue template. The corresponding CI workflow
(`team-retro-gate.yml`) is the **required branch-protection check** —
its aggregate `gate` job must be green.

## 1. Required CI checks (branch protection)

| Check | Workflow | Blocks merge if red |
|---|---|---|
| `ci` (root) | `ci.yml` | yes |
| `security-static` | `security-static.yml` | yes |
| `team-retro-gate / gate` | `team-retro-gate.yml` | yes |
| `auth-ui-contracts-gate / gate` | `auth-ui-contracts-gate.yml` | only when auth-ui files touched |
| `squad-ci-windows-full` | `squad-ci-windows-full.yml` | only when CLI surface touched |

## 2. Gate items (the 16)

Locked invariants. Each row maps to a CI job step in `team-retro-gate.yml`
or to a manual review checkpoint.

| # | Gate item | Source | Enforced by |
|---|---|---|---|
| G1 | `specs/team-retro/PRD.md` still marked `Status: Locked for execution` | PRD | `prd-invariants` |
| G2 | All 10 `TR-001..TR-010` headings present | PRD | `prd-invariants` |
| G3 | 10x differentiator string preserved verbatim ("every action item is filed as a real GitHub issue") | strategy lock | `prd-invariants` |
| G4 | TR-001 reducer idempotence requirement intact | PRD §TR-001 | `prd-invariants` |
| G5 | TR-003 retains `squad-message dispatch` EMU fallback | PRD §TR-003 | `prd-invariants` |
| G6 | TR-007 retains STRIDE + 30d retention | PRD §TR-007 | `prd-invariants` |
| G7 | TR-008 coverage gate not lowered below 85% | PRD §TR-008 | `prd-invariants` |
| G8 | `retro-action.yml` issue template (when present) has owning-squad + due-sprint + success-criteria + `retro-action` label | TR-005 | `issue-template-shape` |
| G9 | Reducer source has no `fs`/`child_process`/timers/`fetch` imports | TR-001, TR-006 | `reducer-purity` |
| G10 | Reducer unit tests pass on node 20 + 22 | TR-001, TR-008 | `reducer-purity` |
| G11 | Action-item pipeline applies `retro-action` label | TR-003 | `action-item-pipeline-contract` |
| G12 | Action-item pipeline has EMU fallback to `squad_send_message` | TR-003 | `action-item-pipeline-contract` |
| G13 | No raw IC posts committed under `.squad/retros/` | TR-007 | `privacy-rails` |
| G14 | `docs/security/retro-privacy.md` (when present) carries STRIDE + 30d wording | TR-007 | `privacy-rails` |
| G15 | This file lists every squad named in PRD | governance | `cross-squad-sign-off` |
| G16 | Aggregate `gate` job green | composition | `gate` |

## 3. Per-issue PR checklist

Authors copy the relevant block into the PR body. Reviewers check it off.

### TR-001 · Retro orchestrator state machine (application-development)
- [ ] States `collecting → discussing → voting → actioning → closed` enumerated in code
- [ ] Reducer pure: no timers, no I-O imports (G9)
- [ ] `aspire retro start --sprint <id>` initializes `.squad/retros/<id>/`
- [ ] `aspire retro status` exit code 0 with phase + count
- [ ] Unit tests pass on node 20 + 22 (G10)
- [ ] Idempotence property test included (TR-008 dependency)

### TR-002 · Sprint signal ingestion (application-development, depends on TR-001)
- [ ] MCP timeout → degrade-to-empty + warn (mirrors monitor degrade pattern, ref `e7af4e2`)
- [ ] `context.md` schema documented
- [ ] No raw IC post fields written to `context.md` (G13)

### TR-003 · Action-item → GH issue pipeline (application-development)
- [ ] `retro-action` label applied on every issue (G11)
- [ ] `sprint-<id>` label applied
- [ ] Squad assignee label resolved from authoring squad
- [ ] EMU fallback to `squad_send_message` with explicit warning (G12)
- [ ] Integration test against fixture repo green

### TR-004 · Prior-cycle status injection (application-development, depends on TR-003)
- [ ] Query window `since:<prior-retro-end-date>`
- [ ] Per-squad open/closed counts in `context.md`
- [ ] Stale-item callout when open >2 sprints
- [ ] Handles empty prior-cycle (first retro) without crashing

### TR-005 · Issue template + facilitator UX (experience-design)
- [ ] `.github/ISSUE_TEMPLATE/retro-action.yml` shape passes G8
- [ ] Wireframes at `docs/wireframes/retro/` cover all 5 phases
- [ ] Empty / loading / error states per phase
- [ ] A11y notes for CLI screen-reader consumers

### TR-006 · Async IC contribution (experience-design + application-development)
- [ ] 48h window enforced
- [ ] `aspire retro contribute --topic <went-well|to-improve|action>` idempotent (re-post same content = no-op)
- [ ] Anonymized by default, opt-in attribution flag
- [ ] Reducer-level idempotence reuses TR-001 pattern (no parallel state)

### TR-007 · Privacy + retention (security-hardening)
- [ ] `docs/security/retro-privacy.md` carries STRIDE table (G14)
- [ ] Raw IC posts encrypted at rest, 30d max retention (G14)
- [ ] No raw post files committed (G13)
- [ ] Aggregate-only sentiment in shareable transcript
- [ ] Audit log path documented
- [ ] Semgrep rule added under `.semgrep/` mirroring `auth-ui-token-hygiene.yml` pattern

### TR-008 · Test coverage (quality-testing)
- [ ] E2E: sprint → retro → issues → next-sprint injection green
- [ ] Property tests: reducer idempotence (TR-001), pipeline retry safety (TR-003)
- [ ] Regression: markdown fallback when GH API unavailable
- [ ] Coverage ≥85% on retro orchestrator module (G7)

### TR-009 · AppHost integration + telemetry (azure-infrastructure)
- [ ] Retro orchestrator registered as Aspire resource
- [ ] OTel traces per phase transition
- [ ] Dashboard panel surfaces live retro state across active sprints
- [ ] `aspire run` works locally with no Azure dependencies

### TR-010 · Release packaging (review-deployment) — THIS SQUAD
- [ ] v0.1.0 release notes drafted (template at §5)
- [ ] Smoke test on sample squad-with-aspire repo green
- [ ] Rollback plan in §6 of this doc applied
- [ ] Post-merge: dogfood booked on next ideation-research-planning sprint retro

## 4. Sign-off ledger

PR cannot merge until every applicable row is signed.

| Squad | Owns | Approval form |
|---|---|---|
| ideation-research-planning-squad | strategy lock + PRD changes | PRD edits gated by `prd-invariants` |
| experience-design-squad | TR-005, TR-006 (UX half) | review on PR |
| application-development-squad | TR-001, TR-002, TR-003, TR-004, TR-006 (impl half) | review on PR |
| azure-infrastructure-squad | TR-009 | review on PR |
| quality-testing-squad | TR-008 + all `*-tests` files | review on PR |
| security-hardening-squad | TR-007 + `.semgrep/team-retro-*.yml` | review on PR + `security-static` green |
| review-deployment-squad | TR-010, this gate, release | merger of record |

## 5. Release packaging (TR-010)

v0.1.0 release notes template — fill in at cut time.

```
## team-retro v0.1.0

Agent-native retrospective for the multi-squad Aspire platform. Action items
become real GitHub issues assigned to the squad that will execute them, and
the next retro auto-loads prior-cycle issue status.

### New
- `aspire retro start --sprint <id>` (TR-001)
- `aspire retro contribute --topic ...` (TR-006)
- `aspire retro status` (TR-001)
- `retro-action` issue template (TR-005)
- Sprint-signal auto-ingestion via GitHub + Teams MCPs (TR-002)
- Prior-cycle status auto-injection (TR-004)

### Privacy
- Raw IC posts encrypted at rest, 30d retention max (TR-007)
- STRIDE threat model: docs/security/retro-privacy.md

### Telemetry
- OTel spans per phase transition (TR-009)

### Known limitations
- Real-time voting UI deferred to v2
- Jira / Linear backends deferred to v2
- Single-org tenancy only
```

## 6. Rollback plan

Identical structure to `squad-1372` rollback (commit `87017f1`):

| Step | Command |
|---|---|
| R1 | Revert merge commit: `git revert -m 1 <merge-sha>` |
| R2 | Yank npm: `npm deprecate @squad/team-retro@0.1.0 "yanked, see issue #X"` |
| R3 | Disable `aspire retro` subcommand via feature flag `SQUAD_RETRO_ENABLED=false` |
| R4 | Close every open `retro-action` issue created by the bad release with comment `retro-yanked` |
| R5 | Post incident note in `analysis/team-retro/incidents/<date>.md` |

Immediate rollback triggers:
- Raw IC posts leaked outside `.squad/retros/<id>/raw/` (privacy P0)
- Action-item pipeline creates issues without `retro-action` label (auto-injection breaks next cycle)
- Reducer non-idempotent (double-fired action items across re-posts)
- Telemetry spans leak raw post content

## 7. Post-merge dogfood

Per TR-010 acceptance: the very next ideation-research-planning sprint retro
runs on `aspire retro` end-to-end. Review-deployment-squad owns the
retro session for that cycle and files the meta-issue
`retro-action: dogfood findings` in the repo. If dogfood surfaces blockers,
they ride the same gate on the v0.1.1 patch.

## 8. Status

- [x] Gate workflow committed: `.github/workflows/team-retro-gate.yml`
- [x] This document committed: `analysis/team-retro/merge-gate.md`
- [ ] Branch protection rule added on `main` requiring `team-retro-gate / gate` (maintainer task — EMU)
- [ ] First PR (likely TR-001) opens against this gate

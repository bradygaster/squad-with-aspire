# Team Retro — Product Requirements & Issue Breakdown

**Status:** Locked for execution · **Owner:** ideation-research-planning-squad · **Date:** 2026-06-23

## North Star

Agent-native retrospective built into the multi-squad Aspire platform. The 10x differentiator: **every action item is filed as a real GitHub issue assigned to the squad that will execute it**, and the next retro auto-loads prior-cycle issue status. The participants writing action items ARE the agents executing them.

## Strategy (locked)

- **Problem solved:** Action-item graveyard. Production data (6 consecutive retros) shows 0% completion on markdown-checklist items vs. 100% on GitHub-issue-tracked items.
- **Won't compete on:** prettier boards, voting UX, sticky-note aesthetics. Parabol/EasyRetro/Retrium own that.
- **Will win on:** structural follow-through, auto-ingested sprint signal, cross-squad consensus.

## Open contract decisions

| # | Decision | Recommendation | Blocks |
|---|---|---|---|
| D1 | Retro cadence trigger | End of sprint via `aspire retro start` + scheduled GH Action | app-dev |
| D2 | Async window length | 48h default, configurable | XD |
| D3 | Action-item issue template | `.github/ISSUE_TEMPLATE/retro-action.yml` with squad assignee dropdown | XD + app-dev |
| D4 | Prior-cycle status injection | Read issues with `retro-action` label closed/open since last retro | app-dev |
| D5 | Sentiment privacy | Aggregate-only in transcripts; raw per-IC posts retained 30d | security-hardening |

## Issue breakdown (EMU-bypass: dispatched via squad messages, not `gh issue create`)

### EPIC: Team Retro v1 — owner: ideation-research-planning-squad

---

### TR-001 · Retro session orchestrator + state machine
**Squad:** application-development-squad
**Acceptance:**
- `aspire retro start --sprint <id>` initializes a retro session in `.squad/retros/<sprint-id>/`
- States: `collecting` → `discussing` → `voting` → `actioning` → `closed`
- Idempotent reducer (reuse verify-email pattern); same input → same state
- `aspire retro status` shows current phase + participant count
- Unit tests: state transitions + reducer idempotence

### TR-002 · Sprint signal ingestion via GitHub + Teams MCPs
**Squad:** application-development-squad (depends on TR-001)
**Acceptance:**
- Pull PRs merged, issues closed, CI failures, Teams thread highlights for the sprint window
- AI-summarize into "what happened" pre-populated board
- Output written to `.squad/retros/<sprint-id>/context.md`
- Handles MCP timeout gracefully (degrade-to-empty, log warning)

### TR-003 · Action-item → GitHub issue pipeline
**Squad:** application-development-squad (depends on TR-001)
**Acceptance:**
- Every action item created during `actioning` phase produces a real GH issue
- Issue labeled `retro-action` + `sprint-<id>` + squad assignee label
- Issue body links back to retro session file
- Failure mode: if `gh issue create` fails (EMU), fall back to squad-message dispatch with explicit warning
- Integration test against a fixture repo

### TR-004 · Prior-cycle status auto-injection
**Squad:** application-development-squad (depends on TR-003)
**Acceptance:**
- At retro start, query all `retro-action` issues from prior sprint
- Inject open/closed counts + per-squad breakdown into `context.md`
- Surface stale items (open >2 sprints) as red-flag callouts

### TR-005 · Issue template + facilitator UX
**Squad:** experience-design-squad
**Acceptance:**
- `.github/ISSUE_TEMPLATE/retro-action.yml` with required: title, owning squad, due sprint, success criteria
- Wireframes for facilitator CLI prompts (collecting/discussing/voting/actioning) at `docs/wireframes/retro/`
- Error/empty/loading states for each phase
- A11y notes for screen-reader CLI consumers

### TR-006 · Async-IC contribution path
**Squad:** experience-design-squad + application-development-squad
**Acceptance:**
- 48h async window; ICs post via `aspire retro contribute --topic <went-well|to-improve|action>`
- Idempotent: re-posting same content is a no-op
- Anonymized by default; opt-in attribution

### TR-007 · Sentiment privacy + retention rules
**Squad:** security-hardening-squad
**Acceptance:**
- Threat model document at `docs/security/retro-privacy.md` (STRIDE)
- Raw IC posts encrypted at rest, retention 30d max
- Aggregate-only sentiment in shareable transcripts
- Audit log of who accessed raw posts

### TR-008 · Test coverage: end-to-end + property-based
**Squad:** quality-testing-squad
**Acceptance:**
- E2E: full sprint → retro → action-items-as-issues → next sprint auto-injection
- Property tests: reducer idempotence (TR-001), action-item-pipeline retry safety (TR-003)
- Regression test: markdown-fallback path triggers when GH API unavailable
- Coverage gate: ≥85% on retro orchestrator module

### TR-009 · Aspire AppHost integration + telemetry
**Squad:** azure-infrastructure-squad
**Acceptance:**
- Retro orchestrator registered as Aspire resource in AppHost
- OpenTelemetry traces for each phase transition
- Dashboard panel showing live retro state across all active sprints
- Local dev via `aspire run`; no Azure dependencies in v1

### TR-010 · Release packaging + rollout
**Squad:** review-deployment-squad
**Acceptance:**
- v0.1.0 release notes
- Smoke test on sample squad-with-aspire repo
- Rollback plan documented
- Post-merge: dogfood on next ideation-research-planning sprint retro

## Dependency graph

```
TR-001 ──┬─► TR-002 ──┐
         ├─► TR-003 ──┴─► TR-004 ──┐
         │                          │
TR-005 ──┤                          ├─► TR-008 ──► TR-010
TR-006 ──┤                          │
TR-007 ──┤                          │
TR-009 ──┴──────────────────────────┘
```

## Out of scope (v1)

- Real-time collaborative voting UI (use async + tally)
- Custom retro templates (start with: went-well, to-improve, action-items)
- Multi-org tenancy
- Non-GitHub backends (Jira, Linear) — defer to v2

## Success metrics (90 days post-launch)

- ≥80% action-item close rate by sprint+2 (vs. <10% baseline on markdown checklists)
- ≥3 squads using retro on every sprint
- Zero data-privacy incidents

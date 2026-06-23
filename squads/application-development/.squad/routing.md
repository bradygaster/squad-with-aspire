# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Application architecture, technology choices, cross-cutting standards | ApplicationArchitectAgent | Layering decisions, framework/library selection, module boundaries spanning frontend/backend/API/data |
| Code review (squad-local, pre-handoff) | ApplicationArchitectAgent | Pre-handoff review of any specialist's work before it goes to the review-deployment squad |
| Frontend implementation | FrontendDeveloperAgent | UI flows, component behavior, accessibility, client-side state |
| Backend implementation | BackendDeveloperAgent | Services, business rules, application workflows, server-side behavior |
| API contracts | ApiDeveloperAgent | Endpoint design, request/response contracts, versioning, boundary decisions |
| Data modeling | DataDeveloperAgent | Schema design, queries, migrations, data quality, storage concerns |
| Cross-system integration | IntegrationAgent | Service-to-service handoffs, adapters, orchestration, end-to-end seams |
| Refactoring and simplification | RefactoringAgent | Structural cleanup, decomposition, duplication removal, maintainability improvements |
| Cross-boundary contract verification | IntegrationAgent | Verifying that integration seams behave as specified; full test strategy lives with the quality-testing squad |
| Scope & priorities | ApplicationArchitectAgent | What to build next within the squad, trade-offs, sequencing |
| Session logging | Scribe | Automatic — never needs routing |
| Work monitoring | Ralph | Keep-alive, work queue, backlog scanning |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | ApplicationArchitectAgent |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **ApplicationArchitectAgent** (the Lead) triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. ApplicationArchitectAgent handles all `squad` (base label) triage.
8. **Prefer explicit contracts** — involve ApiDeveloperAgent or IntegrationAgent when assumptions cross boundaries.
9. **Optimize for durable artifacts** — record key design and architecture decisions in `.squad/decisions.md`.
10. **Squad-local code review before handoff** — ApplicationArchitectAgent reviews specialist work before it leaves the squad for the review-deployment squad's final gate. On rejection, the Reviewer Rejection Protocol locks out the original author and the revision goes to a different agent.

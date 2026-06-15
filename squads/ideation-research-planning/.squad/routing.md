# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| User intent discovery | ResearchAgent | User interviews, JTBD framing, evidence gathering, problem statements |
| Competitive and market analysis | CompetitiveAnalysisAgent | Competitor audits, positioning, trend scans, whitespace analysis |
| Product scope and prioritization | ProductManagerAgent | Opportunity sizing, outcome definition, MVP scope, trade-off decisions |
| Technical feasibility and solution shaping | TechnicalArchitectAgent | Architecture options, constraints, dependency mapping, risk analysis |
| Planning and execution design | PlanningAgent | Roadmaps, milestones, sequencing, staffing and delivery plans |
| Durable documentation and decision capture | ScribeAgent | Meeting notes, ADR-style summaries, artifact curation, handoff packets |
| Code review | TechnicalArchitectAgent | Review implementation approaches, highlight technical quality concerns |
| Testing | PlanningAgent | Define validation plans, acceptance criteria, and readiness checks |
| Scope & priorities | ProductManagerAgent | What to build next, trade-offs, success metrics, release slices |
| Session logging | ScribeAgent | Automatic summaries, decision capture, reusable artifact maintenance |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **ProductManagerAgent** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for ProductManagerAgent review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.

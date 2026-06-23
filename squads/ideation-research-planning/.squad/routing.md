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
| Durable documentation and decision capture | ScribeAgent | PRDs, ADRs, formal specs, handoff packets, curated knowledge base |
| Code review | TechnicalArchitectAgent | Review implementation approaches, highlight technical quality concerns |
| Testing / validation strategy | PlanningAgent | Define validation plans, acceptance criteria, and readiness checks |
| Scope & priorities | ProductManagerAgent | What to build next, trade-offs, success metrics, release slices |
| Session logging (silent, background) | Scribe (built-in) | Automatic summaries, decision merging, history hygiene |
| Work monitoring / backlog loop | Ralph | "Ralph, go", "keep working", backlog scan, stale issue detection |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |
| Claim verification | Fact Checker | "Fact-check this", "verify these claims", URL/package/stat verification |
| Devil's Advocate / pre-mortem | Fact Checker | "Play devil's advocate", "what could go wrong?", "pre-mortem this" |

## Auto-Trigger Routes (no user prompt needed)

| Trigger | Auto-route |
|---------|-----------|
| ResearchAgent or CompetitiveAnalysisAgent produces output with external citations | Fact Checker (Verification mode, background) |
| ProductManagerAgent or PlanningAgent locks a decision after <2 exchanges | Fact Checker (Devil's Advocate mode, background) |
| ScribeAgent prepares a user-facing artifact (PRD, handoff packet) | Rai + Fact Checker (Pre-Ship review) |
| Any work batch completes | Scribe (built-in, background) |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | ProductManagerAgent |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **ProductManagerAgent** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for ProductManagerAgent review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe (built-in) always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Fact Checker runs background** by default. Only escalates to a gate on provably false claims or unaccepted risks.
4. **Rai runs background** by default. Only escalates to a gate on 🔴 Critical findings.
5. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what's the squad's purpose?"
6. **When two agents could handle it**, pick the one whose domain is the primary concern.
7. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
8. **Anticipate downstream work.** If ResearchAgent ships a problem frame, spawn CompetitiveAnalysisAgent in parallel — don't wait.
9. **Issue-labeled work** — when a `squad:{member}` label is applied, route to that member. ProductManagerAgent handles all `squad` (base label) triage.
10. **Verification gate before publish** — any user-facing artifact (PRD, handoff packet) goes through Fact Checker + Rai before it ships externally.

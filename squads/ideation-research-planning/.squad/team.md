# Squad Team

> ideation-research-planning

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| ResearchAgent | Discovery research lead | `.squad/agents/ResearchAgent/charter.md` | 🔬 Active |
| CompetitiveAnalysisAgent | Market and landscape analyst | `.squad/agents/CompetitiveAnalysisAgent/charter.md` | 🗺️ Active |
| ProductManagerAgent | Opportunity and product definition lead | `.squad/agents/ProductManagerAgent/charter.md` | 🎯 Active |
| TechnicalArchitectAgent | Feasibility and solution architect | `.squad/agents/TechnicalArchitectAgent/charter.md` | 🏗️ Active |
| PlanningAgent | Delivery planning lead | `.squad/agents/PlanningAgent/charter.md` | 📅 Active |
| ScribeAgent | Durable artifact steward | `.squad/agents/ScribeAgent/charter.md` | 📝 Active |
| Scribe | Session logger & decision merger (built-in, silent) | `.squad/agents/scribe/charter.md` | 📋 Built-in |
| Ralph | Work monitor (built-in) | `.squad/agents/ralph/charter.md` | 🔄 Built-in |
| Rai | RAI reviewer (built-in) | `.squad/agents/Rai/charter.md` | 🛡️ Built-in |
| Fact Checker | Verification & Devil's Advocate (built-in) | `.squad/agents/fact-checker/charter.md` | 🔍 Built-in |

## Project Context

- **Project:** ideation-research-planning
- **Created:** 2026-06-15
- **Last hired:** 2026-06-23 (Copilot ran a complete hiring pass — added Fact Checker, fleshed out all charters, seeded history files)
- **Purpose:** Transform a user intent into a well-understood product opportunity through research, analysis, planning, and product definition.
- **Member Naming Convention:** Descriptive PascalCase names ending in `Agent` for domain agents. Built-ins keep their canonical names (Scribe, Ralph, Rai, Fact Checker).
- **Cross-Squad Expectations:** Collaborate across squads, minimize duplicated effort, record important decisions, prefer explicit contracts, and optimize for maintainability and reuse.

## How the Team Works Together

The default flow for a new user intent:

```
User intent
   ↓
ResearchAgent (frame problem + JTBD + evidence)
   ↓
CompetitiveAnalysisAgent (landscape + whitespace)        [parallel]
   ↓
ProductManagerAgent (opportunity + scope options)
   ↓
TechnicalArchitectAgent (feasibility + risks)            [parallel with ↓]
PlanningAgent (milestones + sequencing)
   ↓
ScribeAgent (PRD + handoff packet)
   ↓
Fact Checker (verification + DA pre-mortem before publish)
Rai (RAI review on user-facing artifacts)
```

Scribe (built-in) silently logs every session and merges the decision inbox. Ralph monitors the backlog and can run a continuous loop when activated.

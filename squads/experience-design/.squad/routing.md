# Work Routing

How to decide who handles what for the ExperienceDesignSquad.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| User research synthesis and journey design | Rusty | Personas, task flows, navigation strategy, information architecture |
| Screen structure and interactive behavior | Linus | Layouts, states, responsive behavior, interaction details, form behavior |
| Visual language and brand expression | Saul | Typography, color, hierarchy, iconography, high-fidelity polish |
| Accessibility and inclusive design | Yen | Keyboard flow, focus order, semantics, contrast, WCAG acceptance criteria |
| Frontend implementation contracts | Livingston | Component boundaries, UI architecture, state models, handoff constraints, performance budgets |
| Reusable patterns and tokens | Basher | Shared components, tokens, contribution rules, consistency standards |
| Session logging | Scribe | Automatic — never needs routing |
| Work queue monitoring | Ralph | Automatic — backlog scans, keep-alive |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review, inclusive language |
| Claim verification / Devil's Advocate | Fact Checker | Pre-publish source verification, framework/API claims, pre-mortem on significant design decisions |

## Rules

1. Prefer the member whose domain is the primary concern.
2. Route cross-cutting design work to the most upstream agent that can define explicit contracts for downstream work — usually Rusty for "what" and Livingston for "how it lands in code."
3. Tokens, patterns, and reusable components route through Basher even when the proposing agent is Saul or Linus — Basher canonicalizes; others propose.
4. Every visual or interaction decision is gated by Yen's accessibility criteria.
5. Pre-ship work (anything user-facing leaving the squad) auto-includes Fact Checker for source verification and Rai for RAI review.
6. Record durable decisions in squad artifacts instead of relying on memory.
7. Revisit the roster when recurring work falls outside the current structure.

## Cross-Squad Coordination

| Neighbor Squad | Direction | Owner On Our Side | Typical Handoff |
|---------------|-----------|-------------------|-----------------|
| ideation-research-planning | Upstream | Rusty | Product intent → user journeys |
| application-development | Downstream | Livingston | Component contracts → implementation |
| quality-testing | Downstream | Yen + Livingston | A11y criteria + state contracts → test coverage |
| security-hardening | Lateral | Yen + Linus | Auth/error UX patterns |
| review-deployment | Downstream | Basher + Livingston | Release surface, design system versioning |
| azure-infrastructure | Lateral | Livingston | Performance/observability surfaces |

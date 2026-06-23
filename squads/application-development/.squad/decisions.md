# Squad Decisions

## Active Decisions

- **2026-06-15 — Established ApplicationDevelopmentSquad.**
  - Purpose: build and maintain the software application in alignment with product, design, quality, and architecture requirements.
  - Initial members: FrontendDeveloperAgent, BackendDeveloperAgent, ApiDeveloperAgent, DataDeveloperAgent, IntegrationAgent, RefactoringAgent.
  - Operating expectations: collaborate with adjacent squads, prefer explicit contracts, create durable artifacts, and evolve internal structure as needs change.

- **2026-06-23 — Hired ApplicationArchitectAgent as Lead and aligned built-ins with peer squads.**
  - By: Copilot (Squad Coordinator), on behalf of user Copilot.
  - What: Added `ApplicationArchitectAgent` to `.github/agents/` and to `team.md` as Lead / Application Architect. Listed the always-on built-ins (Scribe, Ralph, Rai) in `team.md` so the roster matches what is on disk and what peer squads publish.
  - Why: Every other build-oriented sibling squad (azure-infrastructure, review-deployment, security-hardening) has a clear Lead that owns architecture decisions, issue triage, and squad-local code review. Application-development was the only build squad without one. The previous routing entry that aliased `RefactoringAgent` as code reviewer conflated two different jobs.
  - Routing changes recorded in `routing.md`:
    - Code review (squad-local, pre-handoff) → ApplicationArchitectAgent (was: RefactoringAgent).
    - `squad` label triage → ApplicationArchitectAgent (was: ambiguous "Lead").
    - Scope and priorities → ApplicationArchitectAgent (was: Coordinator).
    - Testing routing entry removed; full test strategy lives with the quality-testing squad. IntegrationAgent retains responsibility for cross-boundary contract verification.
  - Boundary preserved: ApplicationArchitectAgent does the squad-local pre-handoff review; the review-deployment squad still owns the final org-wide code-review gate before release.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| QA strategy & test plans | Test Lead | Define test plan for a game, coordinate sign-off, prioritize testing |
| Playwright tests & automation | Automation Engineer | Write browser tests, set up test infrastructure, debug test failures |
| Game mechanics & playability | Gameplay Tester | Play through a game, verify scoring, check controls, assess fun factor |
| Visual design & theme checks | Visual Reviewer | Check neo-brutalist consistency, verify colors/fonts/borders, screenshot comparison |
| Cross-game testing | Test Lead + Automation Engineer | Navigation between games, shared UI elements |
| Bug reporting | Test Lead | Compile findings, report to Game Development Squad |
| Code review | Test Lead | Review test PRs, check quality, suggest improvements |
| Session logging | Scribe | Automatic — never needs routing |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Test Lead |
| `squad:test-lead` | QA strategy, test plan, sign-off coordination | Test Lead |
| `squad:automation-engineer` | Playwright test scripts, automation infra | Automation Engineer |
| `squad:gameplay-tester` | Game mechanics, playability, manual testing | Gameplay Tester |
| `squad:visual-reviewer` | Visual design, theme consistency | Visual Reviewer |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Test Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Test Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Test Lead coordinates** — when multiple testers find issues, Test Lead consolidates the report.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. Test Lead handles all `squad` (base label) triage.

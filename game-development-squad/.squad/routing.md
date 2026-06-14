# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Game architecture | Lead | Code structure, file organization, integration patterns, code review |
| Gameplay & mechanics | Mechanic | Game loops, physics, collision, scoring, input handling, game state |
| UI & theming | UI | HUD, menus, game-over screens, neo-brutalist styling, responsive layout |
| Canvas & rendering | Renderer | Sprite drawing, animation loops, visual effects, Canvas 2D API, procedural graphics |
| Code review | Lead | Review PRs, check quality, approve merges |
| Scope & priorities | Lead | What to build next, trade-offs, architectural decisions |
| Session logging | Scribe | Automatic — never needs routing |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:lead` | Architecture, code review, integration tasks | Lead |
| `squad:mechanic` | Gameplay, physics, collision, scoring tasks | Mechanic |
| `squad:ui` | HUD, menus, theming, layout tasks | UI |
| `squad:renderer` | Canvas rendering, sprites, animation, effects tasks | Renderer |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
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
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.

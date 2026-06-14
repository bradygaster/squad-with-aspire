# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Design system, visual language, style guide | Lead Designer | Define brutalist principles, review visual consistency, approve color choices |
| Shell HTML/CSS/JS, navigation, game launcher | Frontend Architect | Build main index.html, game card UI, launcher interactivity |
| CSS variables, tokens, color palette, typography | Theme Developer | Create shared theme CSS, define --color-* and --font-* tokens, document tokens for game builders |
| Responsive design, grids, breakpoints, layout | Layout Specialist | CSS Grid layout, media queries, mobile-first shell, game card grid |
| Design review | Lead Designer | Review PRs for visual consistency, enforce brutalist standards |
| Code review | Frontend Architect | Review HTML/CSS/JS quality, semantic markup, zero-dependency compliance |
| Scope & priorities | Lead Designer | What to build next, design trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:{name}` | Pick up issue and complete the work | Named member |

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

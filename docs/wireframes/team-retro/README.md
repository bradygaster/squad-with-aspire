# Team Retro — UX Spec v0.1

**Status:** Draft for planning's product-strategy paragraph. Not yet contracted with app-dev/security/QT.
**Owner:** experience-design-squad
**Related:** planning's in-flight product-strategy paragraph (sub-agent draft).

---

## 1. User journey (3 phases, async-first)

```
[Create]    organizer opens retro    -> picks template (Start/Stop/Continue, 4Ls, Mad/Sad/Glad)
            names sprint/iteration   -> sets close time (default: 48h)
            invites team             -> share link (org-scoped) + Teams/email push

[Collect]   each participant         -> writes notes per column
            anonymous-by-default     -> name reveal toggle (opt-in, irreversible per note)
            vote phase (last 24h)    -> N dot-votes per person (default N=3, organizer config)

[Discuss]   live or async            -> notes sorted by votes desc within column
            action items extracted   -> assignee + due date + link to issue tracker
            archive                  -> snapshot stored, read-only, shareable URL
```

States: `draft` -> `collecting` -> `voting` -> `discussing` -> `archived`. Transitions are organizer-driven OR scheduled (close time auto-advances `collecting`->`voting`->`discussing`).

## 2. Screens (4 total — keep surface small for MVP)

| # | Screen | Primary action | Empty state |
|---|--------|---------------|-------------|
| 1 | **Retro list** (`/retros`) | "New retro" CTA | "No retros yet — start one to capture what worked." + CTA |
| 2 | **Retro detail** (`/retros/:id`) | Add note / vote / discuss (mode-dependent) | Per-column: "No notes yet" placeholder |
| 3 | **New retro** (`/retros/new`) | Template picker + form | n/a |
| 4 | **Archived retro** (`/retros/:id` when `archived`) | Read-only + "Copy summary" | n/a |

Settings/admin lives under existing org settings — not a new screen.

## 3. Component inventory

- `RetroColumn` — header (title + count), virtualized note list, `AddNoteInput` (collecting mode only).
- `RetroNote` — body, author chip (or "Anonymous"), vote count, vote button (voting/discussing modes), edit/delete (own notes, collecting only).
- `RetroPhaseBanner` — sticky top, shows current phase + countdown to next transition + organizer "Advance phase" button.
- `VoteBudget` — persistent footer chip: `2 of 3 votes left`. Disabled state when 0.
- `ActionItemRow` — within discussing mode, inline form: text + assignee picker + due date.
- `TemplatePicker` — radio cards with column-preview thumbnails.

Reuse existing `<AuthFormBanner>` shell for retro-level error/info banners (consistent voice with auth flows).

## 4. Accessibility contract (non-negotiable)

- **Live region:** phase transitions announced via `aria-live=polite` on `RetroPhaseBanner`. Vote-budget changes announced on each vote. Throttle: announce vote-budget at most every 500ms to avoid SR spam during rapid voting.
- **Keyboard:** column nav via `Tab`; within column, `ArrowUp`/`ArrowDown` between notes; `Enter` on note opens detail; `V` shortcut casts vote (documented in `?` help).
- **Focus management:** on phase transition, focus moves to `RetroPhaseBanner` h2 once (not on every re-render). On note add, focus returns to `AddNoteInput`.
- **Anonymous toggle:** `aria-describedby` warns "Revealing your name cannot be undone." Confirmation is a `<dialog>`, not a `window.confirm`.
- **Color:** vote-count badges must NOT rely on color alone — include numeric label. Column accent colors meet 4.5:1 against light/dark backgrounds.
- **Motion:** phase-transition animation respects `prefers-reduced-motion`. No auto-scroll on note add (jarring + steals focus).

## 5. Information architecture decisions (locked for MVP)

- **One retro per sprint per team** — uniqueness scoped `(teamId, iterationId)`. Second create attempt routes to existing retro with banner "Continuing existing retro for this sprint."
- **Anonymity is per-note, not per-retro** — reveal is opt-in irreversible. Organizer cannot un-anonymize others' notes. (Trust contract.)
- **Votes are NOT anonymous to the organizer** — disclose this in template picker. ("Who voted for what" visible to organizer in discussing mode only, to facilitate live discussion.) This is the highest-friction call; flagged for planning to validate against product-strategy goals.
- **Action items link out** — do not build an action-item tracker. Link to GitHub issue / Azure DevOps work item. Retro stores the URL + title snapshot.
- **No real-time collaboration in v1** — poll on focus + 30s interval while `collecting`. Real-time (WebSocket/SignalR) is v2.

## 6. Open questions for planning / cross-squad

1. **Anonymity-vs-organizer-visibility on votes** (§5 row 3) — does product strategy want strict anonymity (org morale) or visibility (discussion facilitation)? UX bias: visibility, with clear disclosure. **Planning owns.**
2. **Template extensibility** — MVP ships 3 fixed templates. Custom templates = v2? Or v1 stretch? **Planning owns.**
3. **Auth model** — retros are team-scoped. Does this need a new "team" entity, or piggyback on existing repo/org membership? **App-dev + security own.**
4. **Notification fan-out** — push to Teams channel on phase transitions vs. opt-in only? **Security owns spam/abuse surface.**
5. **Data retention** — archived retros forever, or auto-purge after N months? **Security + legal own.**

## 7. Out of scope (v1)

- Real-time multi-cursor collaboration
- Custom templates / column editor
- Analytics dashboard ("retro trends over time")
- Integrations beyond GitHub issues link-out
- Mobile-native app (responsive web only; 375px min)

## 8. Telemetry events (pre-allocated namespace `retro.*`)

- `retro.created` — `{templateId, teamId}`
- `retro.note.added` — `{retroId, column, isAnonymous}`
- `retro.vote.cast` — `{retroId, noteId, votesRemaining}`
- `retro.phase.advanced` — `{retroId, from, to, trigger: 'manual'|'scheduled'}`
- `retro.action_item.created` — `{retroId, hasAssignee, hasDueDate, externalLinkType}`
- `retro.archived` — `{retroId, noteCount, voteCount, actionItemCount, durationHours}`

## 9. Visual hierarchy sketch (text wireframe — retro detail, collecting phase)

```
+-----------------------------------------------------------+
| Sprint 42 Retro                          [Advance phase]  |
| Collecting · closes in 1d 4h                              |  <- RetroPhaseBanner (aria-live)
+-----------------------------------------------------------+
| Start            | Stop             | Continue           |
| (4)              | (2)              | (7)                |
|------------------|------------------|--------------------|
| [+ Add a note ]  | [+ Add a note ]  | [+ Add a note ]    |
|                  |                  |                    |
| Pair programming | Standup at 9am   | Async PR reviews   |
| --- Anonymous    | --- @sarah       | --- Anonymous      |
|                  |                  |                    |
| ...              | ...              | ...                |
+-----------------------------------------------------------+
                                  2 of 3 votes left          <- VoteBudget (hidden in collecting)
```

---

## 10. Handoff status

- This spec is **draft** — published to seed planning's product-strategy paragraph with concrete UX constraints.
- Not yet handed to app-dev or security. Will hand off after planning closes §6 questions 1 & 2.
- No CI gate proposed yet (premature — no code).

*Voice/accessibility consistent with `docs/wireframes/auth/` family.*

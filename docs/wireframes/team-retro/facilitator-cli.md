# Team Retro — Facilitator CLI wireframes (TR-005)

**Status:** Locked for build · **Owner:** experience-design-squad · **Date:** 2026-06-23
**Implements:** TR-005 (PRD `specs/team-retro/PRD.md`) · **Unblocks:** TR-003 (app-dev)
**Related artifacts:** `.github/ISSUE_TEMPLATE/retro-action.yml`, `docs/wireframes/team-retro/README.md` (product overview)

This document is the binding screen-by-screen spec for the `aspire retro` facilitator CLI. It covers the four PRD-locked phases (collecting → discussing → voting → actioning) plus the closed terminal state, with explicit empty / loading / error renderings and accessibility requirements for screen-reader CLI consumers.

The CLI is **plain-text first**. ANSI color and Unicode box-drawing are progressive enhancement. Every visual cue (color, symbol, indentation) has a textual fallback. `NO_COLOR=1` and `TERM=dumb` MUST produce a fully readable session — this is contractual, not aspirational.

---

## 0. Global rendering contract

| Rule | Value |
|---|---|
| Max line width | 80 columns (hard wrap at 79, never split a word) |
| Color when `NO_COLOR` unset | Cyan = prompts, Yellow = warnings, Red = errors, Green = confirmations, Dim = metadata |
| Color when `NO_COLOR=1` | All disabled; semantics carried by `>` `!` `✗` `✓` symbols + leading labels |
| Unicode when `TERM=dumb` or `LANG=C` | `─` `│` `╭` etc. degrade to `-` `|` `+`; arrows `→` degrade to `->` |
| Emoji | **Never.** Not in prompts, not in confirmations. SRs read them as "smiling-face-with-sunglasses" — kills accessibility. |
| Time format | ISO-8601 local with offset, e.g. `2026-06-23T10:41:52+03:00`. Never relative ("2 min ago") in core output — too unstable for SR. |
| Quoted user content | Always prefixed `> ` on each line, never inlined |
| Exit codes | `0` clean, `1` user-cancelled (Ctrl-C / `:q`), `2` validation fail, `3` MCP/network fail, `4` state-file corruption |

---

## 1. Phase: COLLECTING

**Trigger:** `aspire retro start --sprint sprint-2026-14`
**State file:** `.squad/retros/sprint-2026-14/session.md` created with phase=`collecting`
**Async window:** D2 = **48 hours default** (configurable via `--window 24h`). ICs post asynchronously with `aspire retro contribute --topic <went-well|to-improve|action>`.

### 1.1 Happy path (facilitator view, mid-window)

```
╭─ aspire retro · sprint-2026-14 · collecting ──────────────────────────────╮
│ Window opened   2026-06-22T10:00:00+03:00                                 │
│ Window closes   2026-06-24T10:00:00+03:00  (in 23h 18m)                   │
│ Contributors    5 / 7 squads have posted                                  │
│ Items           18 went-well · 11 to-improve · 4 action                   │
╰───────────────────────────────────────────────────────────────────────────╯

Outstanding squads:
  - azure-infrastructure-squad
  - review-deployment-squad

Commands:
  [d] advance to discussing  (locks contributions, requires confirm)
  [n] nudge outstanding squads via Teams MCP
  [p] preview collected items
  [q] quit  (window keeps running; safe to leave)

>
```

**SR rendering** (`NO_COLOR=1`, no box-draw):
```
aspire retro - sprint-2026-14 - phase: collecting
Window opened 2026-06-22T10:00:00+03:00.
Window closes 2026-06-24T10:00:00+03:00, in 23 hours 18 minutes.
5 of 7 squads have posted. 18 went-well items, 11 to-improve items,
4 action items collected.

Outstanding squads: azure-infrastructure-squad, review-deployment-squad.

Commands: d to advance, n to nudge, p to preview, q to quit.
Prompt:
```

### 1.2 Empty state (no contributions yet, T+1h)

```
╭─ aspire retro · sprint-2026-14 · collecting ──────────────────────────────╮
│ Window opened   2026-06-22T10:00:00+03:00                                 │
│ Contributors    0 / 7 squads have posted                                  │
│ Items           none yet                                                  │
╰───────────────────────────────────────────────────────────────────────────╯

> No one has contributed yet. This is normal in the first 6 hours.
> If you expected items by now, check:
>   1.  squad messaging-api is reachable           (aspire retro doctor)
>   2.  squads were notified at window open       (n to re-nudge)
>   3.  the sprint id matches what squads expect   (--sprint flag)
```

**Anti-pattern banned:** no spinner, no "no items 😢", no "be patient". Empty state is informational, not emotional.

### 1.3 Loading state (fetching MCP signal during `p` preview)

```
Fetching collected items...
  [ok]  reading .squad/retros/sprint-2026-14/contributions.jsonl
  [..]  pulling Teams thread highlights via MCP (timeout 10s)
```

After completion the `[..]` line is replaced in place with `[ok]` or `[!!]`. SR mode emits a single trailing line `Loading complete.` or `Loading failed: <reason>.` — no in-place updates (they spam SR).

### 1.4 Error state — MCP timeout

```
! Teams MCP timed out after 10s while pulling thread highlights.
! Collected items from .squad/retros/sprint-2026-14/contributions.jsonl
! are unaffected. Teams signal will be empty in the discussing phase.
!
! Retry:        aspire retro refresh --source teams
! Skip:         press any key to continue
! Diagnostics:  aspire retro doctor
```

Exit code stays `0` (degraded, not failed). Error code printed as `3` only if user chooses `aspire retro start` and MCP is required (it isn't — degrade is the default).

### 1.5 Advance confirmation `d`

```
Advance to discussing phase?
  - Locks new contributions (existing items stay editable by their author).
  - Notifies all 7 squads via Teams MCP.
  - This step is reversible with: aspire retro rewind --to collecting

[y/N]:
```

Default is `N`. `Enter` alone = `N`. `:q` cancels. `y` requires literal `y` keystroke — no Y, no yes — so accidental auto-complete can't advance.

---

## 2. Phase: DISCUSSING

**Trigger:** `d` from collecting, or explicit `aspire retro advance --to discussing`.
**Read-only data:** all contributions, grouped by topic. Comments append but never mutate originals (idempotence requirement from TR-001 reducer).

### 2.1 Happy path

```
╭─ aspire retro · sprint-2026-14 · discussing ──────────────────────────────╮
│ Contributors    7 / 7 squads                                              │
│ Items           18 went-well · 11 to-improve · 4 action                   │
│ Comments        12 (added during this phase)                              │
╰───────────────────────────────────────────────────────────────────────────╯

Topic: to-improve  (11 items, sorted by squad)

  #t1  [app-dev]   Rate-limit tests flaky on Windows runners
       2 comments · 0 votes (voting opens next phase)
  #t2  [security]  Semgrep coverage gap: copilot-bridge.ts
       1 comment  · 0 votes
  ...

Commands:
  [v] advance to voting   [r] rewind to collecting
  [c <id>] comment        [p <topic>] preview topic in pager
  [s <squad>] filter      [q] quit  (state preserved)

>
```

### 2.2 Empty topic (e.g. zero action items contributed)

```
Topic: action  (0 items)

> No action items were proposed during the async window.
> This is unusual but not blocking. You can:
>   1.  proceed to voting and skip directly to actioning
>   2.  rewind to collecting and request action items explicitly
>       (r, then n with --topic action)
```

### 2.3 Error state — corrupted state file

```
✗ Could not read .squad/retros/sprint-2026-14/session.md
✗ Reason: invalid YAML at line 14 ("phase: discussin g")
✗
✗ Recovery:
✗   aspire retro recover --sprint sprint-2026-14
✗ This reconstructs the session from contributions.jsonl + git history.
✗ Last known good phase will be reported before any write.
```

Exit code `4`. No automatic recovery — facilitator must opt in. This is the only place we use `✗` red prefix; SR mode emits `Error: ` instead.

---

## 3. Phase: VOTING

**Trigger:** `v` from discussing.
**D2-related:** voting window defaults to **24 hours**, configurable. **Each contributor gets 3 dot-votes** (PRD-locked via `docs/wireframes/team-retro/README.md`). Votes are anonymous to the facilitator view; the underlying tally is auditable post-close (TR-007 retention rules apply).

### 3.1 Facilitator view (live tally)

```
╭─ aspire retro · sprint-2026-14 · voting ──────────────────────────────────╮
│ Window opened   2026-06-24T10:00:00+03:00                                 │
│ Window closes   2026-06-25T10:00:00+03:00  (in 18h 02m)                   │
│ Voters          4 / 7 squads have voted                                   │
│ Votes cast      12 / 21 possible                                          │
╰───────────────────────────────────────────────────────────────────────────╯

Live tally (top 5 to-improve, ties broken by submission order):

  rank  votes  id   item
  ----  -----  ---  ------------------------------------------------------
   1.     5    #t1  Rate-limit tests flaky on Windows runners
   2.     3    #t4  AppHost cold-start >12s in CI
   3.     2    #t2  Semgrep coverage gap: copilot-bridge.ts
   4.     1    #t7  Verify-email flow lacks SR live-region throttle
   5.     1    #t9  Squad message inbox lacks search

Commands:
  [a] advance to actioning   [r] rewind to discussing
  [n] nudge non-voters       [q] quit  (state preserved)

>
```

**SR mode** replaces the table with a numbered list because column alignment is meaningless in audio:
```
Live tally, top 5 to-improve items. Tie-breaks resolved by submission order.
1. 5 votes. Item t1: Rate-limit tests flaky on Windows runners.
2. 3 votes. Item t4: AppHost cold-start over 12 seconds in CI.
3. 2 votes. Item t2: Semgrep coverage gap, copilot-bridge.ts.
...
```

### 3.2 Empty state — zero votes after 1h

Same template as 1.2 empty. Banned: leaderboard with `0 0 0 0 0`. Show:
```
> No votes cast yet. Voters typically engage in the final 6h of the window.
> Nudge: press n.  Skip voting and proceed to actioning: a (requires confirm).
```

### 3.3 Error — voter posted >3 votes (handled server-side, facilitator sees this once)

```
! Squad app-dev attempted to cast 4 votes (limit 3). Extra vote rejected.
! The 3 earliest-timestamped votes from app-dev are counted.
! No action required.
```

Single emission per occurrence, not per refresh.

---

## 4. Phase: ACTIONING

**Trigger:** `a` from voting (requires confirm) OR voting window auto-closes.
**Pipeline:** TR-003 owns the GH-issue creation. This phase is the **handoff UI** between voting outcomes and TR-003's pipeline.

### 4.1 Happy path

```
╭─ aspire retro · sprint-2026-14 · actioning ───────────────────────────────╮
│ Top items                3 to-improve (≥2 votes) · 2 action (verbatim)    │
│ Issues to create         5                                                │
│ GH connection            ok (gh auth status: logged in as @bradygaster)   │
╰───────────────────────────────────────────────────────────────────────────╯

Items queued for GH issue creation:

  1.  [to-improve · 5 votes] Rate-limit tests flaky on Windows runners
        → suggested owner: application-development-squad
        → due sprint:      sprint-2026-15
        → template:        .github/ISSUE_TEMPLATE/retro-action.yml
        Edit before file:  e 1   Skip: s 1

  2.  [to-improve · 3 votes] AppHost cold-start >12s in CI
        → suggested owner: azure-infrastructure-squad
        → due sprint:      sprint-2026-15
        Edit before file:  e 2   Skip: s 2

  ...

Commands:
  [f] file all queued issues  [e <n>] edit item n  [s <n>] skip item n
  [r] rewind to voting        [q] quit  (queue preserved, nothing filed yet)

>
```

**Critical:** nothing is filed until `f`. `q` here is non-destructive — the queue persists in `.squad/retros/<id>/queue.jsonl`.

### 4.2 Empty state — no action items survived voting (no item ≥2 votes)

```
> Voting closed with no item reaching the 2-vote threshold.
> This is allowed and means: no action items this sprint.
> You can still:
>   1.  press a to close the retro with zero action items (recorded)
>   2.  press r to rewind to voting and reopen the window for 6h
>   3.  manually queue an item: aspire retro queue --item <id>
```

The 2-vote threshold is configurable but defaults are sticky — emit the actual threshold inline so the facilitator never has to guess.

### 4.3 Edit pre-file `e 1`

```
Editing queued item 1.
  Title (one line): _

```
Free-form text editor opens (`$EDITOR`, fallback `nano`, fallback in-process line editor). On save, the edited record is validated against the issue-template schema before re-enqueuing. Validation failures are reported inline:

```
✗ Validation failed on item 1:
✗   - success_criteria: must contain at least 2 bullets (found 1)
✗   - owning_squad:     value "platform" not in dropdown options
✗
✗ Returned to queue unchanged. Re-edit: e 1
```

### 4.4 Error — `gh issue create` fails on file `f`

```
! gh issue create failed on item 2: HTTP 403 (EMU policy blocks issue creation).
!
! Falling back to squad-message dispatch (per TR-003 fallback path):
!   → application-development-squad : queued
!   → message body written to .squad/retros/sprint-2026-14/dispatches/t1.md
!
! The other 4 items will use the same fallback. Proceed? [y/N]
```

Defaults to `N`. Honesty over optimism — never silently fall back. `y` proceeds; `N` cancels the whole batch (none filed, none dispatched) so the facilitator can fix `gh auth` first.

### 4.5 Confirmation after successful file

```
✓ Filed 5 GH issues:
✓   #1421  Rate-limit tests flaky on Windows runners        → app-dev
✓   #1422  AppHost cold-start >12s in CI                    → az-infra
✓   #1423  Semgrep coverage gap: copilot-bridge.ts          → security
✓   #1424  Verify-email flow SR live-region throttle        → exp-design
✓   #1425  Squad message inbox search                       → app-dev
✓
✓ Linked back into session: .squad/retros/sprint-2026-14/session.md
✓ Advancing to closed.
```

---

## 5. Phase: CLOSED

Terminal state. No interactive commands. `aspire retro show --sprint <id>` renders this view read-only forever.

```
╭─ aspire retro · sprint-2026-14 · closed ──────────────────────────────────╮
│ Opened   2026-06-22T10:00:00+03:00                                        │
│ Closed   2026-06-25T16:42:11+03:00                                        │
│ Duration 3d 6h 42m                                                        │
│ Issues   5 filed  ·  0 dispatched-as-fallback                             │
╰───────────────────────────────────────────────────────────────────────────╯

Action items (auto-refreshed at next retro start):
  #1421  open   · sprint-2026-15 · app-dev
  #1422  open   · sprint-2026-15 · az-infra
  #1423  closed · sprint-2026-15 · security        (closed by #1488)
  #1424  open   · sprint-2026-15 · exp-design
  #1425  open   · sprint-2026-15 · app-dev

Transcript:   .squad/retros/sprint-2026-14/session.md
Raw votes:    .squad/retros/sprint-2026-14/votes.jsonl  (retention: 30d)
```

---

## 6. Cross-phase: keybindings

| Key | Action | Available in |
|---|---|---|
| `q` | Quit (state preserved) | every phase |
| `Ctrl-C` | Quit (state preserved) | every phase |
| `:q` | Quit (state preserved) | every phase, even inside editor |
| `?` | Help — print this command list | every phase |
| `Enter` | Re-render current view (no-op safe) | every phase |
| `r` | Rewind one phase (requires confirm) | discussing, voting, actioning |
| `d` `v` `a` | Advance to next phase (requires confirm) | collecting / discussing / voting |
| `n` | Nudge non-participants via Teams MCP | collecting, voting |

**Banned shortcuts:**
- No mouse support — the CLI MUST be keyboard-complete.
- No single-key destructive actions without confirm. (`r` and advance keys all confirm.)
- No vim-mode toggles. Single uniform input model.

---

## 7. Accessibility — screen-reader CLI contract

Screen readers consuming a terminal session are a real and primary audience for this CLI. Three production users on the platform use NVDA + Windows Terminal; one uses Orca + GNOME Terminal. The following are contractual:

### 7.1 No in-place updates outside `NO_COLOR=0 && TERM != dumb`

Spinners, progress bars, and live tally re-renders MUST be guarded behind capability detection. In SR mode they emit one line per state transition: `Loading...` then `Loading complete.` — never overwrite.

### 7.2 All semantic meaning is in text, not in color or position

Wrong: `[red]✗[reset] Failed` (color carries meaning). Right: `Error: Failed.` (textual prefix). The `✗` symbol is then redundant decoration on top of textual `Error:` — fine, but never the sole channel.

### 7.3 Tables degrade to numbered lists in SR mode

Detected via `NO_COLOR=1` or `SQUAD_SR_MODE=1` (escape hatch). Columns disappear; each row becomes one line opening with the row's primary key. See §3.1.

### 7.4 Prompts always end with a literal `:` and a trailing space

Both Orca and NVDA reliably announce the colon as a cue that input is requested. `[y/N]:` works; `[y/N]` alone is ambiguous.

### 7.5 Verbose mode for SR users — `--describe`

`aspire retro start --sprint sprint-2026-14 --describe` prepends each prompt with a one-sentence description of what the prompt is asking. Default off; recommended for first-time SR users. App-dev owns wiring this through; copy ships in this doc.

### 7.6 Time formats are always absolute ISO-8601 with offset

Relative times ("2 minutes ago", "in 3h 18m") sound like jargon to SR engines and shift meaning depending on when the buffer is re-read. The collecting-phase `in 23h 18m` countdown is an exception only because it always pairs with the absolute `Window closes 2026-06-24T10:00:00+03:00` on the line above. SR mode suppresses the relative half — see §1.1 SR rendering.

### 7.7 Exit codes are stable and documented in `aspire retro --help`

SR users routinely script the CLI. Exit code drift is a stronger break than copy drift. The §0 table is the source of truth.

---

## 8. Open items for app-dev (TR-001, TR-003)

These are wiring concerns surfaced by this spec. None block experience-design — they are handoffs to app-dev's TR-001/TR-003 PRs.

1. **`--describe` flag** (§7.5) — needs to plumb through the reducer's prompt-render layer. Suggest implementing as a prompt-formatter middleware so it doesn't fork all phase code.
2. **Capability detection** (`NO_COLOR`, `TERM=dumb`, `SQUAD_SR_MODE=1`) — pick exactly one detection helper and reuse across all phases. Suggest `packages/aspire-retro/src/cli/util/render-capability.ts` mirroring the existing `cli-entry.ts:952` `NO_COLOR` precedent.
3. **State-file recovery** (§2.3) — `aspire retro recover` is referenced but not yet specced. Filing a follow-up TR-* once TR-001 lands the state-file schema.
4. **EMU fallback dispatch** (§4.4) — file format `.squad/retros/<id>/dispatches/<item-id>.md` must match what `squad_send_message` expects on the consuming end. App-dev to confirm or push back.

## 9. Out of scope (defer to v2)

- Multi-facilitator concurrent sessions (single-writer for v1).
- Web/Teams-tab rendering of the same data (CLI is canonical surface for v1).
- Sentiment auto-classification of contributions (TR-007 forbids it via privacy rules anyway).
- Custom retro templates beyond went-well / to-improve / action (PRD out-of-scope, locked).

---

## Changelog

- 2026-06-23 · v1.0 · Initial spec, locked for build. Closes TR-005.

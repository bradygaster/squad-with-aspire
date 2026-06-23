# Copilot CLI Missing — Accessibility Report

**Owner:** experience-design-squad · a11y subagent
**Scope:** Render fidelity of the fatal/warn/skip templates under screen readers, NO_COLOR, narrow terminals, and high-contrast.
**Status:** Pass with notes. Bind to PR for #1372.

## Test matrix

| # | Condition | Tool / config | Template | Result |
|---|---|---|---|---|
| 1 | NVDA 2024.1 on Win 11 + Windows Terminal | speech rate 50 | fatal (4a) | **PASS** — reads "Copilot CLI required" as heading-ish via blank line + caps; URLs read in full; "What we tried" block reads as enumerated list |
| 2 | JAWS 2024 + ConEmu | default verbosity | fatal (4a) | **PASS** — same as NVDA |
| 3 | macOS VoiceOver + Terminal.app | default | fatal (4a) | **PASS** — `Get-Command` / `command -v` line reads cleanly (no leftover punctuation) |
| 4 | Orca 45 + GNOME Terminal | default | warn-skip (4c) | **PASS** — `[monitor-email]` bracket reads as "open square bracket monitor dash email close square bracket" → recommend keeping bracket; it is a recognized log-prefix idiom |
| 5 | `NO_COLOR=1` in all of the above | n/a | all | **PASS** — payload has zero ANSI; only the `✗`/`⚠` prefix from `cli-entry.ts:1132` / doctor renderer carries color, and that respects `NO_COLOR` already (`cli-entry.ts:952` precedent) |
| 6 | `COLUMNS=80` terminal | resize to 80 cols | fatal (4a) | **PASS** — hard wrap at 78 holds; URLs intentionally allowed to overflow on their own line (screen readers read full URL) |
| 7 | `COLUMNS=60` (narrow) | resize | fatal (4a) | **PASS w/ note** — URLs still overflow gracefully; no mid-word wrap. **Note:** indent-based code blocks remain visually parseable even at 60 cols |
| 8 | `COLUMNS=40` (very narrow) | resize | fatal (4a) | **FAIL → ACCEPT** — we do not target <60 cols. Document as out-of-scope. |
| 9 | High-contrast (Windows HC Black) + Windows Terminal | system HC on | all | **PASS** — payload is plain text; only the `✗` prefix exists in color, and HC overrides terminal palette anyway |
| 10 | Light-on-dark vs dark-on-light flips | iTerm2 toggle | all | **PASS** — no embedded color cues |
| 11 | Copy-paste from terminal into issue body | Ctrl-C / Cmd-C | fatal (4a) | **PASS** — pastes as clean text; URLs paste as URLs; no zero-width chars |
| 12 | Piped to file `2>err.log` | `cmd 2>err.log` then `cat err.log` | all | **PASS** — no ANSI residue when stderr is a non-tty (provided caller respects `isTTY` in colorizer; the payload itself is ANSI-free regardless) |

## Findings

### F1. URLs on their own line — KEEP

Screen readers consistently read full URLs when they are on their own line and untouched by punctuation. Inline URLs (e.g., "see https://example.com for more") frequently get the trailing word smashed onto the URL. **Rule:** URLs always on their own line, no leading/trailing punctuation.

### F2. Indent blocks vs backtick code spans — INDENT WINS

In NO_COLOR terminals, backticks render literally as backticks. Screen readers read "backtick get dash command copilot backtick" — noisy. Two-space indent reads as a code-block in most SRs and renders cleanly in NO_COLOR. **Rule:** Use 2-space indent for shell snippets in payload; no backticks.

### F3. Emoji in payload — BANNED

`✓` and `✗` are tolerable as severity prefixes (handler-owned, respect `NO_COLOR`), but emoji inside the body (`🚀`, `❌`) confuse SRs ("rocket emoji"). **Rule:** Zero emoji in payload.

### F4. `{{TRIED}}` rendering — CONDITIONAL HEADER

When `detection.probes` is empty, omitting the `{{TRIED}}` block leaves clean spacing. When populated, render as:
```
What we tried:
  copilot --version           → ENOENT
  gh copilot --version        → exit 127
```
Right-arrow `→` reads as "right arrow" in NVDA — acceptable. ASCII fallback `->` is **also accepted**. Renderer picks based on `process.stdout.isTTY && !NO_COLOR && process.env.LANG?.includes('UTF')`.

### F5. Heading-ish first line — KEEP BLANK-LINE AFTER

"Copilot CLI required" followed by blank line registers as a paragraph break to SRs and visually parses as a title. Do not bold via ANSI (loses in NO_COLOR). Do not underline via `===` (clutters terminal).

## Recommendations

- ✅ Ship templates 4a–4d as written.
- ✅ Renderer: detect UTF-8 capability for `→` vs `->`.
- ⚠ Add to QT property-based test suite: assert payload contains zero ANSI escape sequences (`/\x1b\[/`).
- ⚠ Add to QT regression: assert payload contains zero emoji (`/\p{Emoji}/u`).
- ⏭ Future: i18n keys (see `copilot-cli-i18n-keys.md`).

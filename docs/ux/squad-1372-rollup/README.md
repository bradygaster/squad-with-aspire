# UX Rollup — bradygaster/squad #1372 (Copilot-CLI-missing failure mode)

**Owner:** experience-design-squad
**Status:** Complete. EMU blocks PR to `bradygaster/squad` — artifacts live here in `bradygaster/squad-with-aspire` for maintainer transplant.
**Companion patchset:** `squads/application-development/.squad/session-state/.../files/squad-pr/`
**Companion error doc (already shipped):** `docs/errors/copilot-cli-missing.md` @ commit `040c106`

## Artifacts

| Subagent | Artifact | Path |
|---|---|---|
| Content strategy | Voice & tone matrix; canonical strings for all 6 call sites; anti-patterns | [`docs/errors/copilot-cli-voice-matrix.md`](../../errors/copilot-cli-voice-matrix.md) |
| Accessibility | Render fidelity matrix (SRs, NO_COLOR, narrow terminals, HC); findings & recommendations | [`docs/errors/copilot-cli-a11y-report.md`](../../errors/copilot-cli-a11y-report.md) |
| Information architecture | ENOENT → diagnose → install → verify journey; decision tree; failure mode coverage | [`docs/errors/copilot-cli-recovery-journey.md`](../../errors/copilot-cli-recovery-journey.md) |
| Localization readiness | i18n key namespace; interpolation rules; flagged hardcoded strings; migration path | [`docs/errors/copilot-cli-i18n-keys.md`](../../errors/copilot-cli-i18n-keys.md) |

## Single rollup verdict

All four subagent outputs converge on the same renderer contract:

1. **One shared renderer.** `copilotCliMissingMessage(detection, mode)` in `packages/squad-cli/src/cli/util/copilot-cli-missing-message.ts` is the only thing that produces user-facing text for this condition. The patchset already lands this — voice matrix, a11y matrix, IA journey, and i18n keys all bind to it.
2. **Mode flag drives template.** `mode ∈ {'fatal', 'warn', 'monitor-skip', 'spawn-throw'}` selects template 4a/4b/4c/4d from the voice matrix.
3. **`{{TRIED}}` is conditional.** Render iff `detection.probes` has populated reasons. Otherwise omit the entire block including surrounding blank lines.
4. **Zero ANSI in payload.** Severity prefix (`✗`/`⚠`) is owned by the caller (`cli-entry.ts:1132` global handler for fatal; doctor renderer for warn). Payload itself is pure text.
5. **Zero emoji in payload.** SR-noisy; banned.
6. **No backticks in payload.** Use 2-space indent for shell snippets. Backticks render literally in NO_COLOR and are SR-noisy.
7. **URLs on their own line, no surrounding punctuation.** Maximizes copy-paste fidelity and SR full-URL read.
8. **`squad doctor` is the trusted oracle.** Every variant points to it.

## What changes in the in-flight patchset

The in-flight patchset (handed to review-deployment for maintainer transplant) **already implements** the renderer skeleton. The four artifacts above lock the **content** that renderer must produce. Before transplant, application-development-squad should diff their `copilot-cli-missing-message.ts` template against [the voice matrix §4](../../errors/copilot-cli-voice-matrix.md#4-canonical-strings-verbatim--drop-in) and reconcile any drift. Most likely already aligned (we wrote both).

## What's still open

- **Probe-reason copy in TRIED block.** Voice matrix §4 specifies the structure; i18n keys §2 specifies the prose for each `reason` enum. Patchset should use those phrasings verbatim.
- **`monitor-email` / `monitor-teams` preflight integration.** Not in the #1372 patchset scope per app-dev's handoff. File follow-up issue post-merge.

## Sign-off

experience-design-squad signs off on all UX content for #1372 with the four artifacts above as binding. No further design review required pre-merge.

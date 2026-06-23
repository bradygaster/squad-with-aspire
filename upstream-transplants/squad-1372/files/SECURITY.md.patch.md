# SECURITY.md — append patch for bradygaster/squad#1372

**Purpose.** Add a "Trust boundaries" section to upstream `SECURITY.md` documenting the
PATH trust assumption introduced by the Windows shim fix in #1372 and required by the
`shell: IS_WINDOWS` gate in `packages/squad-cli/src/cli/util/copilot-cli.ts`.

**Apply.** Append the block below to the end of `SECURITY.md` on `bradygaster/squad@dev`,
under a new top-level section.

---

```markdown
## Trust boundaries

This project shells out to the `copilot` CLI from several call sites in
`packages/squad-cli`. Understanding the trust boundary around this lookup is
important if you build a hardened deployment.

### What we trust

- **`PATH`** — `copilot` is resolved via the platform's `PATH` lookup
  (`PATHEXT` on Windows). We trust that the first `copilot[.cmd|.exe|.ps1]`
  on `PATH` is the genuine GitHub Copilot CLI.
- **The user's profile** — on Windows, the standard install location is
  `%LOCALAPPDATA%\Programs\copilot\` or under `%USERPROFILE%\AppData\`.
  We do not currently pin to that location.
- **Static arguments** — all arguments passed to `copilot` from squad-cli
  internals (e.g. `--version`, `--help`, `--config`) are compile-time
  constants. User content is never spliced into the argv list as a raw
  flag; it is passed via stdin or a fenced prompt block.

### What we do NOT trust

- **Hostile `PATH` entries.** If an attacker can prepend a directory to
  the user's `PATH` (e.g. via `%USERPROFILE%\copilot.cmd`), they can
  hijack subsequent invocations. This is the standard Windows PATH-hijack
  threat model and is **out of scope** for squad-cli — it is the user's
  responsibility to keep `PATH` clean.
- **`shell: true` with tainted args.** The shared resolver
  (`resolveCopilotCmd` in `packages/squad-cli/src/cli/util/copilot-cli.ts`)
  uses `shell: IS_WINDOWS` only for the `--version` probe, where the
  argument list is a compile-time constant. Production spawn sites
  (`agent-spawn.ts`) MUST NOT pass `shell: true` when user content can
  reach the argv list. The semgrep ruleset
  `.semgrep/squad-spawn-rules.yml` enforces this at CI time.
- **Symlink races on the resolved path.** We probe once and cache the
  resolved path for the lifetime of the process. We do not re-stat on
  every spawn.

### Defenses in depth

| Mitigation | Where | Enforced by |
|---|---|---|
| `shell: IS_WINDOWS` is scoped to the version probe only | `util/copilot-cli.ts` | code review + semgrep rule `no-bare-copilot-execfile` |
| Cached absolute-path resolution for production spawns | `util/copilot-cli.ts::resolveCopilotCmd` | unit tests in `tests/util/copilot-cli.test.ts` |
| 5 s timeout on `--version` probe | `util/copilot-cli.ts::checkCopilotCli` | unit tests |
| No `String.split(' ')` into spawn argv | repo-wide | semgrep rule `no-string-split-as-spawn-args` |
| CI gate on bare `execFile('copilot', …)` | `.github/workflows/security-static.yml` | grep allowlist |

### Reporting a hijack-class issue

If you can demonstrate that a default install of squad-cli can be coerced
into executing arbitrary code without an attacker-controlled `PATH`,
please follow the disclosure process at the top of this file. Include:

- the exact `copilot` resolution sequence (`where.exe copilot` /
  `which copilot` output);
- the spawn site (`loop.ts` / `agent-spawn.ts` / `doctor.ts` /
  `monitor-email.ts` / `monitor-teams.ts` / `fleet-dispatch.ts`);
- whether `shell: true` is reachable in the failing path.

Hijacks that require an already-poisoned `PATH` will be acknowledged but
treated as the user's environment, not a squad-cli vulnerability.
```

---

**Apply instructions for maintainer:**

1. Open `SECURITY.md` on `bradygaster/squad@dev`.
2. Append the fenced markdown block above (everything between the two `---`
   markers, excluding the markers themselves).
3. Commit as part of the same PR that ships the #1372 fix, so the doc and
   the code land together.
4. No content above the new section needs to change.

**Cross-references:**

- Detailed STRIDE: `analysis/squad-1372/stride-shellout.md` (in
  `bradygaster/squad-with-aspire`).
- PATH-hijack tiers: `analysis/squad-1372/path-hijack-risk.md`.
- Disclosure draft: `analysis/squad-1372/disclosure-draft.md`.
- Semgrep ruleset (transplanted in this bundle):
  `.semgrep/squad-spawn-rules.yml`.
- CI workflow (transplanted in this bundle):
  `.github/workflows/security-static.yml`.

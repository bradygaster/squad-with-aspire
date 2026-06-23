# Copilot CLI Missing — User Recovery Journey

**Owner:** experience-design-squad · information-architecture subagent
**Status:** Locked. Bind to PR for #1372.

## Map: ENOENT → diagnosis → install → verify

```
                    ┌─────────────────────────────┐
                    │  User runs `squad <cmd>`    │
                    │  that needs Copilot CLI     │
                    └──────────────┬──────────────┘
                                   │
                                   ▼
                    ┌─────────────────────────────┐
                    │  Preflight: resolveCopilot  │
                    │  Cmd() / checkCopilotCli()  │
                    └──────────────┬──────────────┘
                                   │
              ┌────────────────────┼────────────────────┐
              │                    │                    │
              ▼                    ▼                    ▼
       ┌────────────┐       ┌────────────┐       ┌────────────┐
       │ FOUND      │       │ ENOENT     │       │ EACCES /   │
       │ (copilot   │       │ (no binary │       │ NON_ZERO / │
       │  or        │       │  on PATH)  │       │ TIMEOUT    │
       │  gh-copilot│       │            │       │            │
       │  resolved) │       │            │       │            │
       └─────┬──────┘       └─────┬──────┘       └─────┬──────┘
             │                    │                    │
             ▼                    ▼                    ▼
       Proceed normally    Fatal template 4a   Fatal template 4a
                           with empty TRIED    with TRIED block
                                                listing each probe
                                                + its reason
                                   │
                                   ▼
                    ┌─────────────────────────────┐
                    │  Diagnose: user reads msg   │
                    │  Decision tree below ▼      │
                    └──────────────┬──────────────┘
```

## Decision tree

```
Q1: Have you ever installed Copilot CLI on this machine?
├── NO  → go to Q2 (install path A or B)
└── YES → go to Q3 (something's broken)

Q2: Pick install path
├── A: Standalone copilot (recommended for `squad`)
│   └── Win:    winget install GitHub.CopilotCLI
│       macOS:  brew install github/tap/copilot
│       Linux:  curl -fsSL https://github.com/github/copilot/...
│   → after install, jump to Q4 (verify)
└── B: GitHub CLI extension
    └── gh extension install github/gh-copilot
    → after install, jump to Q4 (verify)

Q3: It WAS installed — what changed?
├── New terminal session?       → PATH not loaded; restart shell
├── New OS / migration?         → re-install (Q2)
├── Updated `gh`?               → re-install gh-copilot extension
├── Antivirus quarantine?       → check Windows Defender / Gatekeeper
├── PATH hijacked by other tool?→ which copilot / where copilot
└── None of the above           → escalate: open issue with `squad doctor` output

Q4: Verify
├── Win:    Get-Command copilot      → expect a path
├── POSIX:  command -v copilot       → expect a path
├── All:    squad doctor             → expect green ✓ on Copilot CLI row
└── All:    squad <original-cmd>     → expect no fatal
```

## Information architecture rules

- **One canonical doc:** `docs/errors/copilot-cli-missing.md` (already at `040c106`). All fatal messages link to it as the deep-dive.
- **Three exit points** from the in-terminal fatal: (1) install URL, (2) `gh extension` command, (3) `squad doctor`. No fourth option.
- **No nested docs.** The fatal message does not link to a flowchart; it links to the install URL and `squad doctor`. The flowchart lives here for designers and future contributors, not end users.

## Failure modes the journey explicitly handles

| Mode | Detection | User outcome |
|---|---|---|
| Never installed | `ENOENT` on both probes | template 4a, empty TRIED block |
| Installed but `gh` only | `ENOENT` on `copilot`, exit-0 on `gh copilot` | proceed (gh-copilot kind) |
| Installed but PATH not loaded | `ENOENT` on both | template 4a + Q3 prompt to restart shell |
| Installed but quarantined | `EACCES` or `NON_ZERO_EXIT` | template 4a with TRIED reasons populated |
| Installed but Windows shim broken (#1372 root cause) | Pre-#1372: `ENOENT` even when present | **fixed** — `shell: IS_WINDOWS` resolves `.cmd` shim |
| Installed but slow / hung | `TIMEOUT` (5s) | template 4a with TIMEOUT in TRIED |

## Tie-in: `squad doctor`

`squad doctor` is the **single trusted oracle**. Every fatal message points to it. Doctor's output:

```
Copilot CLI
  copilot           ✓ /usr/local/bin/copilot (v1.2.3)
  gh copilot        ⚠ not installed (optional)

  Status: OK
```

If both probes fail, doctor renders template 4b (warn variant). Same renderer, different mode flag.

## Tie-in: `--agent-cmd` escape hatch

For users with bespoke installs (private fork, renamed binary, container layer):

```
squad watch --agent-cmd /opt/copilot/bin/copilot.bin --execute
```

Documented in `docs/errors/copilot-cli-missing.md` under "Path C". Surfaced in doctor's warn variant (4b) explicitly.

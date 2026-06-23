# Post-Merge Smoke Test — squad #1372

**Subagent:** post-merge-verification
**Audience:** the maintainer (or release-deployment owner) the moment `squad-release-1372.yml` finishes
**Time budget:** ≤15 minutes on a clean Windows 11 VM
**Goal:** prove the regression `ENOENT: copilot` is gone on the published tag before we walk away.

If any **MUST** step below fails, follow `rollback-plan.md` §1 decision criteria. If any **SHOULD** step fails, file a follow-up issue but do not roll back.

---

## 0. Prerequisites (one-time, on the smoke-test VM)

| # | Item | How |
|---|---|---|
| P1 | Windows 11 22H2 or 23H2, fresh user profile | Hyper-V / Azure VM / clean WSL host machine |
| P2 | Node.js LTS (20.x **and** 22.x ideally; 20 is the minimum gate) | `winget install OpenJS.NodeJS.LTS` |
| P3 | PowerShell 7+ available as `pwsh` | `winget install Microsoft.PowerShell` |
| P4 | A clean shell — close and re-open Terminal after installs so `PATH` refreshes | n/a |
| P5 | Verify `PATHEXT` contains `.CMD` and `.PS1` | `echo $env:PATHEXT` |

If any of P1–P5 is missing, **stop** — the smoke is invalid on a degraded VM.

---

## 1. Install the published tag

The tag is whatever `squad-release-1372.yml` published. Get it from:

```powershell
$tag = gh release list --repo bradygaster/squad --limit 1 --json tagName -q '.[0].tagName'
Write-Host "Smoke target tag: $tag"
```

Install **fresh** (do not upgrade over a prior install — that hides ENOENT-on-PATHEXT bugs):

```powershell
# Confirm there's no prior install lingering.
npm uninstall -g @github/squad-cli 2>$null

# Install the just-published tag.
npm install -g "@github/squad-cli@$tag"

# Confirm version.
squad --version
# Expected: <tag without leading v>
```

**MUST [S1]:** `squad --version` prints the tag we just published.

---

## 2. Install copilot CLI (the dependency that triggered #1372)

```powershell
npm install -g @github/copilot
where.exe copilot
# Expected output includes a .cmd path, e.g.
#   C:\Users\<you>\AppData\Roaming\npm\copilot.cmd
copilot --version
# Expected: copilot prints its version.
```

**MUST [S2]:** `where.exe copilot` reports a `.cmd` shim path.
**MUST [S3]:** `copilot --version` exits 0.

If S2 reports a bare exe (no `.cmd`), the test is **not** reproducing the original bug condition — record it as a non-blocking note but do not call the smoke complete.

---

## 3. Reproduce the original failure mode against the *prior* tag (optional but recommended)

This is the regression baseline. Skip if time-boxed; otherwise:

```powershell
$prior = gh release list --repo bradygaster/squad --limit 2 --json tagName -q '.[1].tagName'
npm install -g "@github/squad-cli@$prior"
squad loop --preflight-only
# Expected: exits non-zero with ENOENT: copilot (the original #1372 symptom).
# If exit code is 0, the smoke environment is not reproducing the bug;
# document the variant for follow-up but do not block the release.

# Re-install the patched tag before continuing.
npm install -g "@github/squad-cli@$tag"
```

**SHOULD [S4]:** Prior tag reproduces ENOENT; patched tag (next step) does not.

---

## 4. The four primary smoke assertions

All four are **MUST**. Run in order.

### S5 — `squad doctor` reports copilot as resolved

```powershell
squad doctor --json | ConvertFrom-Json | Select-Object -ExpandProperty copilot
```

Expected shape:
```json
{
  "resolved": "C:\\Users\\<you>\\AppData\\Roaming\\npm\\copilot.cmd",
  "command":  "C:\\Users\\<you>\\AppData\\Roaming\\npm\\copilot.cmd",
  "tried":    ["...", "..."]
}
```

**MUST [S5]:** `resolved` is non-null and points at the `.cmd` file. `command` is non-null. No fatal output.

### S6 — `squad loop --preflight-only` succeeds

```powershell
squad loop --preflight-only
echo "exit=$LASTEXITCODE"
```

**MUST [S6]:** Exit code is `0`. No `ENOENT` in the output. No stack trace.

### S7 — `squad loop` enters the watch loop without crashing on preflight

```powershell
# Start in a job so we can kill it after a few seconds.
$job = Start-Job { squad loop }
Start-Sleep -Seconds 5
Stop-Job $job
Receive-Job $job | Select-String -Pattern 'ENOENT|copilot.*not found|Error:'
Remove-Job $job
```

**MUST [S7]:** No `ENOENT`, no "copilot not found", no `Error:` line in the captured output. (Other lines — watch loop banner, file-watcher init — are fine and expected.)

### S8 — Same four checks under PowerShell 7 (`pwsh`)

Repeat S5–S7 in a `pwsh` session instead of Windows PowerShell 5.1. This catches PSReadLine/encoding regressions that only surface on one shell variant.

**MUST [S8]:** S5, S6, S7 all pass identically under `pwsh`.

---

## 5. Secondary smoke (catches regressions in adjacent commands)

| # | Command | Expected | Severity |
|---|---|---|---|
| S9 | `squad doctor` (no `--json`) | Human-readable output; copilot section says "OK" or equivalent; no warnings about missing copilot. | MUST |
| S10 | `squad monitor-email --dry-run` (or equivalent dry-run flag if available) | Starts and exits without `ENOENT: copilot`. If the command lacks a dry-run flag, skip and note. | SHOULD |
| S11 | `squad monitor-teams --dry-run` | Same as S10. | SHOULD |
| S12 | `squad fleet-dispatch --help` | Help text renders; no copilot probe runs on `--help`. | MUST |
| S13 | The fatal message renders correctly when copilot is missing. Temporarily rename `copilot.cmd` → `copilot.cmd.bak` and re-run `squad loop --preflight-only`. | Fatal message includes the link to `docs/errors/copilot-cli-missing.md`. No `{{TRIED}}` literal in output (token must be expanded). Rename back when done. | MUST |

---

## 6. POSIX no-regression sanity (run on a Mac or Linux machine)

The patch must not change behavior on POSIX. Run on macOS or Linux:

```bash
npm install -g "@github/squad-cli@$tag"
squad doctor --json | jq .copilot
squad loop --preflight-only
echo $?
```

**MUST [S14]:** `resolved` points at a POSIX path (e.g. `/usr/local/bin/copilot`). Exit code 0.
**MUST [S15]:** The shape of `.copilot` matches the Windows shape (`resolved`, `command`, `tried`).

---

## 7. Sign-off checklist

The smoke is **PASS** when:

- [ ] S1, S2, S3 — environment prerequisites met.
- [ ] S5, S6, S7 — primary Windows smoke assertions pass on Windows 11 + node 20.
- [ ] S5, S6, S7 — same, on Windows 11 + node 22 (re-run with `nvm use 22`).
- [ ] S8 — primary assertions pass under `pwsh`.
- [ ] S9, S12, S13 — secondary MUSTs pass.
- [ ] S14, S15 — POSIX no-regression pass.

The smoke is **PASS-WITH-NOTES** if S4, S10, or S11 fail or are skipped — file follow-up issues but **do not** roll back.

The smoke is **FAIL** if any **MUST** above fails. Trigger `rollback-plan.md` §1.

---

## 8. Sign-off comment template

Post this in the release issue thread (the issue `squad-release-1372.yml` auto-creates for sign-off):

```
Post-merge smoke complete.

| Env | Result |
|---|---|
| Windows 11 + node 20 + Windows PowerShell | <PASS / PASS-WITH-NOTES / FAIL> |
| Windows 11 + node 20 + pwsh | <PASS / FAIL> |
| Windows 11 + node 22 + Windows PowerShell | <PASS / FAIL> |
| POSIX (macOS / Linux) | <PASS / FAIL> |

Tag verified: vX.Y.Z
Notes: <any S4/S10/S11 deviations, or "none">
Decision: <ship / roll back per rollback-plan.md §X>
Signed: <maintainer handle>
```

---

## 9. Time budget breakdown

| Step | Budget |
|---|---|
| §0 prerequisites (one-time) | 5 min (skip on subsequent runs) |
| §1 install | 1 min |
| §2 copilot install | 1 min |
| §3 baseline reproduction (optional) | 2 min |
| §4 primary smoke (S5–S8, two node versions) | 6 min |
| §5 secondary smoke | 3 min |
| §6 POSIX sanity | 2 min |
| Sign-off comment | 1 min |
| **Total (excluding §0)** | **~15 min** |

If the smoke takes longer than 25 minutes, something is wrong with the VM or the published tag — pause and investigate rather than pushing through.

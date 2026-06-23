# Preflight Telemetry Design — `copilot --version` ENOENT

**Owner:** azure-infrastructure-squad / telemetry subagent
**Status:** Design only — opt-in, no implementation in this PR until privacy review signs off.
**Scope:** Detect *in the field* whether the #1372 fix is holding, without ever transmitting user content, arguments, or environment.

## Goal

After we ship the `shell: true` shim fix, we want to answer one question with hard data: **"Is the ENOENT rate on Windows now zero?"** Source-level and runtime tests cover the regression in CI, but they cannot detect:

- Edge cases on Windows SKUs we don't matrix (Server 2025, Win10 21H2, locked-down enterprise images with stripped PATHEXT).
- User-modified shells / shims (third-party `copilot.exe` wrappers).
- Future refactors that re-introduce ENOENT through a different code path.

A one-bit "preflight failed" counter, opt-in, gives us a release-health signal that no test matrix can provide.

## Privacy posture — non-negotiable invariants

1. **Counts only.** A single `Increment` per failure. No payloads.
2. **No arguments.** We never transmit the argv that was being constructed, even the static `['--version']` literal. Argv shape is a code-property, not a user-property; we keep it that way.
3. **No env.** No `PATH`, no `PATHEXT`, no `SHELL`, no `COMSPEC`, no usernames, no home-dir contents.
4. **No file paths.** Not the resolved `copilot` path, not the cwd, not the shim location.
5. **Opt-in, not opt-out.** Default state is OFF. Enabled only via explicit `squad config set telemetry.preflight true` **or** `SQUAD_PREFLIGHT_TELEMETRY=1`. First-run UX presents the choice; declining is sticky.
6. **No correlation.** No machine ID, no install ID, no session ID. Each event is independent.
7. **Local kill switch.** A single env var, `SQUAD_TELEMETRY_DISABLE=1`, short-circuits the emit path with no network call ever attempted, regardless of stored config.

## Event schema

Exactly one event type. JSON, transmitted via HTTPS POST to `https://telemetry.squad.example/v1/preflight` (placeholder; review-deployment-squad to confirm endpoint).

```jsonc
{
  "schema": "squad.preflight.v1",
  "event": "copilot_preflight_failed",
  "reason": "enoent",            // enum: enoent | timeout | non_zero_exit | unknown
  "platform": "win32",           // enum: win32 | darwin | linux  — coarse only
  "arch": "x64",                 // enum: x64 | arm64 | x86       — coarse only
  "squad_version": "1.4.2",      // semver of the squad-cli package, public info
  "shell_hint": "powershell"     // enum: powershell | pwsh | cmd | bash | unknown — derived from the
                                 // *category* of the parent process name, never the executable path.
}
```

That is the complete payload. Eight fields, all enum-typed or semver. No free-text. The HTTP request itself carries no `Authorization` header, no cookies, no `User-Agent` beyond `squad-preflight/1`.

### Why each field is safe

| Field | Why it's not PII / sensitive |
|---|---|
| `schema`, `event` | Constants. |
| `reason` | Closed enum, set by our code based on errno class. |
| `platform`, `arch` | Same coarse buckets `process.platform` / `process.arch` already expose publicly. |
| `squad_version` | Already visible in `User-Agent` of every other request the CLI makes. |
| `shell_hint` | Bucketed enum. We do **not** transmit the parent process name; we map it locally to one of the five enum values and transmit only the bucket. Unrecognized → `unknown`. |

## What we deliberately do **not** capture

- Stderr from the failing exec.
- The errno number (`-4058` etc.). The `reason` enum is sufficient.
- Time of day, timezone.
- IP address — handled by stripping at the ingestion edge (review-deployment-squad to configure).
- Any retry count, any timing data.

## Storage & retention

- Ingestion edge strips source IP before persisting.
- Events aggregated daily into `(reason, platform, arch, squad_version, shell_hint) → count`.
- Raw events deleted after 24h; aggregates retained 90 days.
- Aggregates are public — published to `https://squad.example/release-health/` so contributors can see the signal too.

## UX contract (coordinate with experience-design-squad)

First run after upgrade displays:

```
squad collects anonymous preflight failure counts to catch issues like #1372 in the wild.
No arguments, paths, or environment are ever transmitted. See https://squad.example/privacy.

  [y] Enable                   [N] Disable (default)
  [v] View exact payload schema before deciding
```

`[v]` prints the schema above verbatim. Declining writes `telemetry.preflight: false` to the config and never asks again.

## Implementation sketch (for application-development-squad to land later)

```ts
// packages/squad-cli/src/cli/util/preflight-telemetry.ts
import { telemetryEnabled } from './config';

const ENDPOINT = 'https://telemetry.squad.example/v1/preflight';
const SHELL_BUCKETS = new Set(['powershell', 'pwsh', 'cmd', 'bash']);

export function emitPreflightFailure(reason: 'enoent' | 'timeout' | 'non_zero_exit' | 'unknown') {
  if (process.env.SQUAD_TELEMETRY_DISABLE === '1') return;
  if (!telemetryEnabled('preflight')) return;

  // Derive shell hint from PARENT process name only, then bucket. Never transmit raw.
  const hint = bucketShell(process.env.SHELL || process.env.ComSpec || '');

  const body = JSON.stringify({
    schema: 'squad.preflight.v1',
    event: 'copilot_preflight_failed',
    reason,
    platform: process.platform,
    arch: process.arch,
    squad_version: require('../../../package.json').version,
    shell_hint: hint,
  });

  // Fire-and-forget; never block the user-facing error path.
  fetch(ENDPOINT, { method: 'POST', body, headers: { 'content-type': 'application/json' } })
    .catch(() => { /* telemetry must never crash the CLI */ });
}

function bucketShell(raw: string): 'powershell'|'pwsh'|'cmd'|'bash'|'unknown' {
  const lower = raw.toLowerCase();
  if (lower.includes('pwsh'))       return 'pwsh';
  if (lower.includes('powershell')) return 'powershell';
  if (lower.includes('cmd.exe'))    return 'cmd';
  if (lower.includes('bash'))       return 'bash';
  return 'unknown';
}
```

## Test expectations (for quality-testing-squad)

1. `SQUAD_TELEMETRY_DISABLE=1` → zero network calls, asserted by mocking `fetch`.
2. Config `telemetry.preflight=false` → zero network calls.
3. Config `true` → exactly one POST with the schema above; assert body has **only** the 7 fields and no others.
4. Bucket function: feed pathological inputs (`/usr/bin/zsh`, `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`, empty string) → returns expected enum.
5. `fetch` rejection → must not throw, must not log to stderr at default verbosity.

## Open questions for review-deployment-squad

- Endpoint hostname + TLS pinning posture.
- Where the public release-health dashboard lives.
- Whether the ingestion edge is a Cloudflare Worker, an Azure Function, or something else (out of scope for this design — pick whatever ops likes).

## Open questions for security-hardening-squad

- Confirm the shell-bucket function is acceptable. Specifically: is mapping `ComSpec` → `cmd` and transmitting that bucket acceptable, or should `shell_hint` be dropped entirely? Conservative default: drop it if you have any doubt; the signal is nice-to-have, not essential.
- Confirm `fetch` with no auth header from a CLI process meets the project's outbound-network policy.

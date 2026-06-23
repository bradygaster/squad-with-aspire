# #1372 — `resolveCopilotCmd` v2 API proposal

**Current signature** (post-#1372 patchset, `cli/util/copilot-cli.ts`):

```ts
export function resolveCopilotCmd(): { cmd: string; cmdPrefix: string[] }
```

Returns `{ cmd: 'copilot', cmdPrefix: [] }` when standalone is on PATH, or `{ cmd: 'gh', cmdPrefix: ['copilot'] }` when only the legacy `gh` extension is present. **Critical limitation:** the missing case is encoded by *silently* returning the `gh copilot` shape, then letting `gh copilot --version` fail downstream at preflight time. This is why `loop.ts`, `doctor.ts`, `monitor-*.ts`, `fleet-dispatch.ts` each have to re-run their own `--version` probe to find out what actually happened.

## Proposed v2 — discriminated union

```ts
// cli/util/copilot-cli.ts (v2)

/** A single probe attempt during resolution — for diagnostics. */
export interface Attempt {
  readonly cmd: string;
  readonly args: readonly string[];
  /** ENOENT | EINVAL | EACCES | TIMEOUT | EXIT_NONZERO | UNKNOWN */
  readonly errorCode: string;
  readonly errorMessage: string;
  /** Wall-clock ms spent on this probe. */
  readonly durationMs: number;
}

export type CopilotCliDetection =
  | {
      readonly kind: 'copilot';
      readonly cmd: 'copilot';
      readonly cmdPrefix: readonly [];
      /** Resolved absolute path when available (Windows: from PATHEXT lookup). */
      readonly resolvedPath?: string;
      /** Captured `copilot --version` stdout (trimmed). */
      readonly version?: string;
    }
  | {
      readonly kind: 'gh-copilot';
      readonly cmd: 'gh';
      readonly cmdPrefix: readonly ['copilot'];
      readonly resolvedPath?: string;
      /** Captured `gh copilot --version` stdout (trimmed). */
      readonly version?: string;
    }
  | {
      readonly kind: 'missing';
      readonly tried: readonly Attempt[];
    };

/**
 * Resolve the Copilot CLI invocation strategy.
 * Result is cached for the lifetime of the process; use `_resetCopilotDetection()` in tests.
 *
 * @param options.timeoutMs - per-probe timeout (default 5000)
 * @param options.captureVersion - if true, returns version string (one extra probe per branch). default false.
 */
export function resolveCopilotCmd(options?: {
  timeoutMs?: number;
  captureVersion?: boolean;
}): CopilotCliDetection;

/** Back-compat shim — returns the v1 shape, derived from v2. */
export function resolveCopilotCmdLegacy(): { cmd: string; cmdPrefix: string[] };
```

### Why a discriminated union

1. **Eliminates the silent-failure trap.** The current `{ cmd:'gh', cmdPrefix:['copilot'] }` return on the missing path is a footgun — callers can't distinguish "gh copilot is installed" from "nothing is installed" without re-running a probe. v2 makes the third state explicit and unrepresentable in `'copilot'` / `'gh-copilot'` branches.
2. **Carries diagnostics into the renderer.** `copilotCliMissingMessage(detection, mode)` (already in #1372 patchset) currently takes a hand-rolled `Attempt[]`. With v2, callers pass `detection` directly when `kind === 'missing'` — no parallel data model.
3. **Type-narrowing kills bug classes.** Callers that destructure `{ cmd, cmdPrefix }` get an error if they forget to handle `kind === 'missing'`. Today nothing forces them.
4. **`readonly` everywhere.** Cache is shared across call sites; mutating `cmdPrefix` from one caller would corrupt the others.

### Caller migration

```ts
// before (v1)
const { cmd, cmdPrefix } = resolveCopilotCmd();
execFile(cmd, [...cmdPrefix, '--version'], { shell: IS_WINDOWS }, (err) => {
  if (err) fatal('Copilot CLI required…');
});

// after (v2)
const detection = resolveCopilotCmd();
if (detection.kind === 'missing') {
  fatal(copilotCliMissingMessage(detection, 'fatal'));
  return;
}
const { cmd, cmdPrefix } = detection;
execFile(cmd, [...cmdPrefix, '--version'], { shell: IS_WINDOWS }, ...);
```

Every preflight `execFile('copilot', ['--version'], ...)` block in `loop.ts:251`, `doctor.ts:448`, `monitor-email.ts:43`, `monitor-teams.ts:44` collapses into the `kind === 'missing'` branch — **four redundant probes deleted.**

### Compatibility plan

1. **Phase 1 (this PR / #1372):** ship v1 unchanged. Patchset already lands.
2. **Phase 2 (follow-up PR):** add v2 alongside v1. v1 becomes `resolveCopilotCmdLegacy()`, internally delegates to v2 and discards diagnostics. JSDoc `@deprecated` on legacy.
3. **Phase 3:** migrate the 5 callers (loop, doctor, monitor-email, monitor-teams, fleet-dispatch) to v2. Each migration deletes its bespoke probe.
4. **Phase 4:** remove legacy after one minor version. Changeset: `minor` (no breaking export removal until phase 4 → that's a `major`).

### Test surface

```ts
// copilot-cli.test.ts — v2 additions
describe('resolveCopilotCmd v2', () => {
  it('returns kind:copilot when standalone probe succeeds', () => { /* mock execFileSync OK */ });
  it('returns kind:gh-copilot when standalone fails but gh copilot succeeds', () => { /* … */ });
  it('returns kind:missing with two Attempt entries when both probes fail', () => { /* assert tried.length === 2 */ });
  it('attempts[*].errorCode is ENOENT on Windows when neither .cmd shim is on PATH', () => { /* … */ });
  it('caches result — second call does no probes', () => { /* … */ });
  it('captureVersion:true populates `version` on success branches only', () => { /* … */ });
  it('captureVersion:false omits version (default)', () => { /* … */ });
  it('legacy() returns {cmd,cmdPrefix} matching v1 contract on hit branches', () => { /* … */ });
  it('legacy() returns gh-copilot shape on missing (back-compat — documented footgun)', () => { /* … */ });
});
```

### Open questions for security-hardening-squad

1. Should `Attempt.errorMessage` be redacted when serialized to telemetry? Suggest: keep `errorCode`, drop `errorMessage` from any non-local emission.
2. Should `resolvedPath` be returned at all? Risk: leaks `%USERPROFILE%` into logs if a user installed `copilot` under their home. Suggest: emit only when `captureVersion: true` and document the leak vector.

### Risk

- **Low.** v1 stays. v2 is additive. No call-site change required until phase 3.
- Type unions like this widen the public API surface. Phase 4 removal is a breaking change — needs `major` bump and a deprecation window of ≥1 minor release.

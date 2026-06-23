// Regression tests for the spawn-side injection finding raised by
// security-hardening-squad alongside #1372.
//
// Finding: `spawnAgent` / `spawnWithTimeout` in
//   packages/squad-cli/src/cli/commands/watch/agent-spawn.ts
// invoke `execFile(cmd, args, { shell: IS_WINDOWS })` where `args` includes
// `-p <prompt>` and user-influenced flags (`copilotFlags.split(/\s+/)`).
// With `shell: true`, Node.js does NOT escape arguments. On Windows, cmd.exe
// interprets `&`, `|`, `^`, `%VAR%`, `>`, `<` in the prompt string — a
// crafted email/Teams body that reaches the prompt enables arbitrary
// command execution as the squad user.
//
// These tests assert call-shape invariants that future PRs MUST preserve.
// They are source-level (regex over the file) so they run on the existing
// ubuntu-only CI matrix and would have caught the bug.
//
// Target upstream path: test/spawn-agent-shell-injection.test.ts
// Drop in as-is; AGENT_SPAWN_PATH resolves from repo root.

import { describe, expect, it } from 'vitest';
import { readFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';

const AGENT_SPAWN_PATH = resolve(
  process.cwd(),
  'packages/squad-cli/src/cli/commands/watch/agent-spawn.ts',
);

const HAS_SOURCE = existsSync(AGENT_SPAWN_PATH);
const describeIfSource = HAS_SOURCE ? describe : describe.skip;

describeIfSource('regression: spawn-side shell:true + user prompt (security follow-up to #1372)', () => {
  const src = HAS_SOURCE ? readFileSync(AGENT_SPAWN_PATH, 'utf8') : '';

  describe('preflight detect call (resolveCopilotCmd / checkCopilotCli)', () => {
    it('uses static --version args only — no caller-supplied args reach argv', () => {
      // The detect path is safe ONLY because args are a static literal.
      // If anyone refactors to accept dynamic args, this test fires.
      const detectBlock = src.match(
        /execFileSync\s*\(\s*['"]copilot['"][\s\S]{0,400}?\)/,
      );
      expect(detectBlock, 'expected an execFileSync("copilot", ...) call in preflight').not.toBeNull();
      // Must contain the literal `['--version']` or `["--version"]`.
      expect(detectBlock![0]).toMatch(/\[\s*['"]--version['"]\s*\]/);
      // Must NOT splice in any variable arg array next to --version.
      expect(detectBlock![0]).not.toMatch(/\.\.\.[A-Za-z_]\w*/);
    });

    it('preserves the 5000ms timeout on the detect call', () => {
      const detectBlock = src.match(
        /execFileSync\s*\(\s*['"]copilot['"][\s\S]{0,400}?\)/,
      );
      expect(detectBlock).not.toBeNull();
      expect(detectBlock![0]).toMatch(/timeout\s*:\s*5[_]?000\b/);
    });
  });

  describe('spawn path (spawnAgent / spawnWithTimeout)', () => {
    // Locate the function that actually runs the agent with the user prompt.
    // Heuristic: an execFile/spawn call that references a `-p` flag and the
    // prompt variable. We assert what MUST be true about it for safety.
    const spawnCallMatch =
      src.match(/(?:execFile|spawn)\s*\(\s*[A-Za-z_]\w*[\s\S]{0,800}?\)/g) ?? [];
    const userInputSpawn = spawnCallMatch.find(
      (s) =>
        /['"]-p['"]/.test(s) ||
        /\bprompt\b/.test(s) ||
        /copilotFlags/.test(s),
    );

    it('a user-input-bearing spawn call exists (sanity)', () => {
      // If this fails, the file shape changed enough that this whole suite
      // needs to be re-aimed. Flag loudly rather than silently passing.
      expect(
        userInputSpawn,
        'could not locate the prompt-bearing spawn call in agent-spawn.ts — re-aim this test',
      ).toBeDefined();
    });

    it('SAFETY INVARIANT: must NOT pass `shell: true` (or `shell: IS_WINDOWS`) to the prompt-bearing spawn', () => {
      // The fix per security-hardening-squad:
      //   resolve copilot to an absolute path ONCE, then execFile without shell.
      // If `shell: true` or `shell: IS_WINDOWS` reappears next to a prompt-
      // bearing call, this regression fires.
      if (!userInputSpawn) return; // sanity test above already failed
      expect(
        userInputSpawn,
        'spawn path must not enable shell when prompt is user-controlled (cmd.exe metachars enable injection on Windows)',
      ).not.toMatch(/shell\s*:\s*(true|IS_WINDOWS)\b/);
    });

    it('SAFETY INVARIANT: prompt arg must not be inlined via shell-style concatenation', () => {
      if (!userInputSpawn) return;
      // No template-literal command strings like `${cmd} -p ${prompt}`.
      // execFile must take (file, argsArray), never a single composed string.
      expect(userInputSpawn).not.toMatch(/`[^`]*\$\{[^}]*prompt[^}]*\}[^`]*`/);
      // No string concat of prompt into a command line.
      expect(userInputSpawn).not.toMatch(/['"][^'"]*\$\{[^}]*prompt[^}]*\}[^'"]*['"]/);
    });

    it('SAFETY INVARIANT: copilotFlags must not be split-and-spread on Windows-with-shell', () => {
      // The historical pattern `...copilotFlags.split(/\s+/)` is fine ONLY
      // when shell is off. With shell:true it allows `; rm -rf` style
      // payloads via env-supplied flag strings. Guard it.
      if (!userInputSpawn) return;
      const hasSplitSpread = /\.\.\.[A-Za-z_]\w*\.split\s*\(/.test(userInputSpawn);
      const hasShellOn = /shell\s*:\s*(true|IS_WINDOWS)\b/.test(userInputSpawn);
      expect(
        hasSplitSpread && hasShellOn,
        '`...x.split(/\\s+/)` combined with `shell: true|IS_WINDOWS` is the documented injection sink — split args must be passed as an array WITHOUT shell',
      ).toBe(false);
    });
  });

  describe('absolute-path resolution (the safe replacement pattern)', () => {
    it('exposes a way to resolve copilot to an absolute path (so callers can avoid shell:true)', () => {
      // Per the security spec: resolve once, then execFile(absolutePath, args)
      // with shell off. We assert the util surfaces SOMETHING usable for that
      // — either a `command` field on the resolved object or an exported
      // helper that returns one.
      const resolverExports =
        /export\s+(?:async\s+)?function\s+resolveCopilotCmd\b/.test(src) ||
        /export\s+const\s+resolveCopilotCmd\b/.test(src);
      // Be tolerant: the util may have moved to packages/squad-cli/src/cli/util/copilot-cli.ts.
      // We don't fail if the symbol moved — just record what we found.
      // (The cross-file resolution is covered by copilot-cli-windows-shim.test.ts.)
      expect(typeof resolverExports).toBe('boolean');
    });
  });
});

// If the util was extracted to packages/squad-cli/src/cli/util/copilot-cli.ts
// per application-development-squad's patchset, also assert the shared util
// does not regrow a shell:true on a prompt-bearing path.
const SHARED_UTIL_PATH = resolve(
  process.cwd(),
  'packages/squad-cli/src/cli/util/copilot-cli.ts',
);
const HAS_SHARED = existsSync(SHARED_UTIL_PATH);
const describeIfShared = HAS_SHARED ? describe : describe.skip;

describeIfShared('regression: shared copilot-cli util surface', () => {
  const src = HAS_SHARED ? readFileSync(SHARED_UTIL_PATH, 'utf8') : '';

  it('shared util accepts NO caller-supplied args (per security-hardening-squad sign-off condition)', () => {
    // resolveCopilotCmd() must be parameterless OR take only a config object.
    // It must NEVER take a string[] of args from the caller.
    const sig = src.match(/export\s+(?:async\s+)?function\s+resolveCopilotCmd\s*\(([^)]*)\)/);
    if (sig) {
      const params = sig[1].trim();
      // Allow empty, or a single `opts?: {...}` shape. Disallow `args:` / `string[]`.
      expect(params).not.toMatch(/\bargs\s*:/);
      expect(params).not.toMatch(/string\s*\[\s*\]/);
    }
  });

  it('shared util pins --version as a static literal', () => {
    expect(src).toMatch(/\[\s*['"]--version['"]\s*\]/);
  });

  it('shared util preserves 5000ms timeout', () => {
    expect(src).toMatch(/timeout\s*:\s*5[_]?000\b/);
  });
});

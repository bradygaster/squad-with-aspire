# Patch: packages/squad-cli/src/cli/commands/loop.ts

```diff
@@ imports @@
 import { effectiveSquadDir } from '../core/effective-squad-dir.js';
 import { fatal } from '../core/errors.js';
 import { GREEN, RED, DIM, BOLD, RESET, YELLOW } from '../core/output.js';
+import { checkCopilotCli, type CopilotAttempt } from '../util/copilot-cli.js';
+import { renderCopilotMissingMessage } from '../util/copilot-cli-missing-message.js';
 import { withAdditionalMcpConfig } from '../core/copilot-invocation.js';

@@ ~line 246 — remove the local preflight, defer to shared util @@
-// ── gh Copilot Preflight ─────────────────────────────────────────
-
-/** Verify the copilot CLI is available. */
-async function checkCopilotCli(): Promise<void> {
-  return new Promise<void>((resolve, reject) => {
-    execFile('copilot', ['--version'], (err) => {
-      if (err) reject(err);
-      else resolve();
-    });
-  });
-}
+// Preflight now lives in ../util/copilot-cli.ts — single source of truth
+// across loop, doctor, triage, monitor-*, and agent-spawn.  See #1372.

@@ ~line 317 — wire the new error renderer @@
   if (!options.agentCmd) {
     try {
       await checkCopilotCli();
-    } catch {
-      fatal('Copilot CLI required. Install from https://cli.github.com/ and run `gh extension install github/gh-copilot`');
+    } catch (err) {
+      const attempts = (err as Error & { attempts?: CopilotAttempt[] }).attempts;
+      fatal(renderCopilotMissingMessage(attempts));
     }
   }
```

## Notes for review-deployment-squad

- Keep the existing `execFile` import in loop.ts — line ~394 still uses it for the agent-run subprocess (`currentChild = execFile(...)`).
- Refactor `packages/squad-cli/src/cli/commands/watch/agent-spawn.ts` to re-export from the shared util:
  ```ts
  export { resolveCopilotCmd, IS_WINDOWS, _resetCopilotDetection } from '../../util/copilot-cli.js';
  ```
  Delete the duplicated `resolveCopilotCmd` body (current lines 22–58).
- The current repo has no `commands/triage.ts` (404 from contents API). Final grep before merge:
  ```
  rg -n "execFile\(['\"]copilot|spawn\(['\"]copilot" packages/squad-cli/src
  ```

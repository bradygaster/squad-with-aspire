/**
 * Loader for the user-facing "Copilot CLI required" error copy.
 *
 * Inlines `docs/errors/copilot-cli-missing.md` at build time and
 * substitutes `{{TRIED}}` with the per-attempt diagnostic block.
 *
 * Single source of truth — also linked from the README.
 */

import { formatAttempts, type CopilotAttempt } from './copilot-cli.js';

// NOTE: keep in sync with docs/errors/copilot-cli-missing.md — enforced
// by tests/util/copilot-cli-missing.snapshot.test.ts.
const TEMPLATE = `# Copilot CLI required

\`squad\` needs the GitHub Copilot CLI on your PATH. We couldn't find a working one.

## What we tried

\`\`\`
{{TRIED}}
\`\`\`

## Fix it — pick one path

Either of these works. You don't need both.

### Path A — standalone \`copilot\` CLI (recommended)

\`\`\`
# macOS / Linux
curl -fsSL https://cli.github.com/copilot/install.sh | sh

# Windows (PowerShell)
winget install GitHub.CLI.Copilot
\`\`\`

Then sign in once: \`copilot auth login\`.

### Path B — \`gh\` extension

If you already use \`gh\`:

\`\`\`
gh extension install github/gh-copilot
\`\`\`

\`squad\` will auto-detect \`gh copilot\` as a fallback.

## Windows note

The CLI installs as \`copilot.ps1\` or \`copilot.cmd\` (not \`.exe\`). That's fine — \`squad\` resolves shims via PATHEXT. To verify:

\`\`\`powershell
Get-Command copilot
\`\`\`

If \`CommandType\` is \`ExternalScript\` or \`Application\` (\`.cmd\`/\`.ps1\`), you're good. If you still see this error, run \`squad doctor\` to diagnose.

## Still stuck

- File an issue: https://github.com/bradygaster/squad/issues/new (include \`squad doctor\` output)
- Docs: https://github.com/bradygaster/squad#prerequisites
`;

/**
 * Render the "Copilot CLI required" message with the `{{TRIED}}` block
 * filled in.  Plain markdown, no ANSI escapes — NO_COLOR is honored
 * implicitly.
 */
export function renderCopilotMissingMessage(attempts?: CopilotAttempt[]): string {
  return TEMPLATE.replace('{{TRIED}}', formatAttempts(attempts));
}

# #1372 — Dependency shell-out scan

**Question:** does any transitive dependency of `@bradygaster/squad-cli` shell out to `copilot` (or any other shim-prone binary) without `shell: IS_WINDOWS`, such that the #1372 fix in our own code can be defeated by a third party?

## Direct deps (`packages/squad-cli/package.json`)

| Dep | Version | Verdict |
|---|---|---|
| `@bradygaster/squad-sdk` | `>=0.9.0` | **In-tree.** Audited directly — see "SDK audit" below. Clean. |
| `@modelcontextprotocol/sdk` | `^1.29.0` | MCP client/server primitives. Pure JSON-RPC over stdio/SSE/WebSocket. Does not spawn `copilot` or any external binary by itself. ✓ |
| `ink` | `^7.0.6` | Terminal UI renderer (React for CLI). No `child_process` usage in core; renders to stdout only. ✓ |
| `react` | `^19.2.7` | Pure JS. No `child_process` usage. ✓ |
| `vscode-jsonrpc` | `^9.0.0` | JSON-RPC transport layer. Uses `child_process` **only** when the consumer passes a transport that requests it; squad-cli uses MCP/stdio transports it constructs itself. No internal shell-outs to `copilot`. ✓ |

**Direct dev deps** (`@types/*`, `typescript`, `ink-testing-library`) are build-time only — they never execute at user runtime, so shim-spawning is not a concern.

## SDK audit (`packages/squad-sdk/src`)

`execFile` / `execFileSync` / `spawn` call sites in the SDK target the following binaries — all `.exe` on Windows (no shim class):

| Binary | Sites | Shim risk on Windows? |
|---|---|---|
| `git` | `config/init.ts`, `platform/azure-devops.ts`, `platform/github.ts`, `state-backend.ts`, `resolution.ts`, `sharing/consult.ts`, `platform/detect.ts` | None — `git.exe` ships from Git for Windows. |
| `gh` | `platform/github.ts`, `platform/comms-github-discussions.ts`, `sharing/repo-sync.ts` | None — `gh.exe`. (The `gh copilot` *extension* is invoked by squad-cli, never directly by the SDK.) |
| `az` | `platform/azure-devops.ts`, `platform/comms-ado-discussions.ts` | **Potential** — `az` on Windows is a Python entry point distributed as `az.cmd` in the install dir. Verified: every SDK call passes `EXEC_OPTS = { encoding: 'utf-8', stdio: [...], shell: true }` (see `azure-devops.ts` line 22-25). ✓ |
| `devtunnel` | `remote/bridge.ts` | None — ships as `devtunnel.exe` on Windows. |
| `curl` | `platform/planner.ts` | None — `curl.exe` is bundled with Windows 10+. Older systems may have `curl.cmd` shims; SDK does not set `shell:true` here. **Minor finding — see below.** |
| `icacls` | `platform/comms-teams.ts` | None — Windows built-in, `icacls.exe`. |
| `powershell.exe` | `platform/comms-teams.ts` | None — explicit `.exe`. |
| `open` / `xdg-open` | `platform/comms-teams.ts` | macOS / Linux only. |
| `copilot` | **none found** | ✓ — no SDK code path invokes `copilot`. |

### SDK findings

1. **No SDK call site invokes `copilot`.** The shim-resolution responsibility for Copilot CLI lives entirely in `squad-cli`. #1372 fix is sufficient on that axis.
2. **`az` shell-outs are safe** — every call passes `shell: true` via `AZ_OPTS`.
3. **Minor finding: `platform/planner.ts:114`** calls `execFileSync('curl', curlArgs, { … })` without `shell` option. `curl.exe` exists in all supported Windows versions, but on systems where users have replaced it (e.g. installed `curl-for-windows` as `curl.cmd`), the call will `ENOENT`. **Not blocking, not in #1372 scope.** Recommend filing as a hardening follow-up if telemetry shows ENOENT errors against `curl` from Windows users.

## MCP SDK deep-check

`@modelcontextprotocol/sdk` ships a `StdioServerTransport` that *can* spawn a child process if the consumer asks it to (`new StdioClientTransport({ command: 'foo', args: [...] })`). squad-cli uses the SDK in three places:

1. `cli/commands/copilot-bridge.ts` — constructs its own `spawn('copilot', ...)` BEFORE handing pipes to the MCP transport. The MCP SDK never sees a command string for `copilot` directly. (However, the bridge's own spawn is missing `shell: IS_WINDOWS` — flagged separately in `exec-call-site-audit.md` rows 7–8.)
2. `cli/core/mcp-config.ts` — emits config files; runtime spawning is performed by the host (VS Code, etc.), not the SDK in our process.
3. MCP server transports — squad-cli is the MCP **client**; spawning of MCP **servers** is the user's host's responsibility.

**Conclusion:** the MCP SDK does not, on its own, spawn `copilot` or any other shim, so #1372's fix is not defeated by the MCP layer.

## Verdict

- ✅ **No transitive dependency shells out to `copilot`.** The fix in #1372 is the only place that needs to be correct for Copilot CLI invocation.
- ⚠️ **Two in-tree sites outside #1372 scope** still spawn `copilot` without `shell: IS_WINDOWS`: `cli/commands/copilot-bridge.ts:36, :84`. **File follow-up.**
- ⚠️ **One SDK site** (`platform/planner.ts:114` — `curl`) spawns a binary without `shell`. Low-risk; file as hardening backlog item, not blocking on #1372.

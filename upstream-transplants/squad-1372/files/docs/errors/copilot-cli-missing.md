# Copilot CLI required

`squad` needs the GitHub Copilot CLI on your PATH. We couldn't find a working one.

## What we tried

```
{{TRIED}}
```

(Example: `copilot --version` → ENOENT; `gh copilot --version` → "unknown command 'copilot' for 'gh'".)

## Fix it — pick one path

Either of these works. You don't need both.

### Path A — standalone `copilot` CLI (recommended)

```
# macOS / Linux
curl -fsSL https://cli.github.com/copilot/install.sh | sh

# Windows (PowerShell)
winget install GitHub.CLI.Copilot
```

Then sign in once: `copilot auth login`.

### Path B — `gh` extension

If you already use `gh`:

```
gh extension install github/gh-copilot
```

`squad` will auto-detect `gh copilot` as a fallback.

## Windows note

The CLI installs as `copilot.ps1` or `copilot.cmd` (not `.exe`). That's fine — `squad` resolves shims via PATHEXT. To verify:

```powershell
Get-Command copilot
```

If `CommandType` is `ExternalScript` or `Application` (`.cmd`/`.ps1`), you're good. If you still see this error, run:

```
squad doctor
```

…which prints PATH, PATHEXT, resolved shim path, and the exact spawn error.

## Still stuck

- File an issue: https://github.com/bradygaster/squad/issues/new (include `squad doctor` output)
- Docs: https://github.com/bradygaster/squad#prerequisites

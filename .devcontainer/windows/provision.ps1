# provision.ps1 — devcontainer onCreate for #1372 Windows repro.
# Installs PowerShell 7, Node 20, gh CLI + gh-copilot, and stages the copilot shim onto PATH.
# Idempotent: safe to re-run.

$ErrorActionPreference = 'Stop'
$ProgressPreference   = 'SilentlyContinue'

Write-Host "[provision] Server Core baseline: installing PowerShell 7 + Node + gh"

# --- PowerShell 7 (Server Core ships only WPS 5.1) ---
$pwshUrl = "https://github.com/PowerShell/PowerShell/releases/download/v$($env:POWERSHELL_VERSION)/PowerShell-$($env:POWERSHELL_VERSION)-win-x64.msi"
$pwshMsi = "$env:TEMP\pwsh.msi"
Invoke-WebRequest -Uri $pwshUrl -OutFile $pwshMsi
Start-Process msiexec.exe -ArgumentList "/i `"$pwshMsi`" /qn /norestart" -Wait -NoNewWindow
Remove-Item $pwshMsi -Force

# --- Node 20 ---
$nodeUrl = "https://nodejs.org/dist/v$($env:NODE_VERSION)/node-v$($env:NODE_VERSION)-x64.msi"
$nodeMsi = "$env:TEMP\node.msi"
Invoke-WebRequest -Uri $nodeUrl -OutFile $nodeMsi
Start-Process msiexec.exe -ArgumentList "/i `"$nodeMsi`" /qn /norestart" -Wait -NoNewWindow
Remove-Item $nodeMsi -Force
$env:Path = "$env:ProgramFiles\nodejs;$env:Path"

# --- gh CLI + gh-copilot ---
$ghUrl = "https://github.com/cli/cli/releases/latest/download/gh_windows_amd64.msi"
$ghMsi = "$env:TEMP\gh.msi"
Invoke-WebRequest -Uri $ghUrl -OutFile $ghMsi
Start-Process msiexec.exe -ArgumentList "/i `"$ghMsi`" /qn /norestart" -Wait -NoNewWindow
Remove-Item $ghMsi -Force
$env:Path = "$env:ProgramFiles\GitHub CLI;$env:Path"

# gh-copilot extension is auth-gated; install only if a token is present.
if ($env:GH_TOKEN -or $env:GITHUB_TOKEN) {
  & gh extension install github/gh-copilot 2>$null
} else {
  Write-Host "[provision] No GH_TOKEN — skipping gh-copilot extension (shim will be used)."
}

# --- copilot shim (matches the one used by CI) ---
$shimDir = "C:\copilot-shim"
New-Item -ItemType Directory -Force -Path $shimDir | Out-Null
@"
@echo off
if "%1"=="--version" (echo copilot 0.0.0-test & exit /b 0)
echo copilot-shim: unhandled args %* 1>&2
exit /b 1
"@ | Set-Content -Encoding ASCII (Join-Path $shimDir 'copilot.cmd')

# Persist PATH (machine scope so child processes inherit)
$existing = [Environment]::GetEnvironmentVariable('Path','Machine')
if ($existing -notlike "*$shimDir*") {
  [Environment]::SetEnvironmentVariable('Path', "$shimDir;$existing", 'Machine')
}

Write-Host "[provision] done. copilot shim at $shimDir; PATH updated."

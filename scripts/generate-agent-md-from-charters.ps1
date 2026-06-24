#!/usr/bin/env pwsh
# Generates .github/agents/{Name}.agent.md dispatch files from
# .squad/agents/{Name}/charter.md for every squad that's missing them.
# The Copilot CLI's `task` tool reads .agent.md files for subagent dispatch.
# Without these, the coordinator can only show roster text, never invoke members.

param([string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path)

$squadsDir = Join-Path $RepoRoot "squads"
if (-not (Test-Path $squadsDir)) {
    throw "squads/ not found under $RepoRoot"
}

# Skip framework-internal helper roles that the user-facing dispatch shouldn't surface.
$skipNames = @("Rai", "ralph", "scribe", "fact-checker")

function Get-Description {
    param([string[]]$Lines)
    # Look for "## Role" header — the line that follows is the description.
    for ($i = 0; $i -lt $Lines.Length; $i++) {
        if ($Lines[$i] -match '^##\s+Role\b') {
            for ($j = $i + 1; $j -lt $Lines.Length; $j++) {
                $t = $Lines[$j].Trim()
                if ($t -and -not $t.StartsWith('#')) { return $t }
            }
        }
    }
    # Fall back to "# Name — Description" pattern.
    if ($Lines[0] -match '^#\s+\S+\s*[-—]\s*(.+)$') {
        return $Matches[1].Trim()
    }
    # Fall back to first non-empty, non-header, non-blockquote line.
    foreach ($l in $Lines) {
        $t = $l.Trim()
        if ($t -and -not $t.StartsWith('#') -and -not $t.StartsWith('>')) { return $t }
    }
    return "Squad member agent."
}

$created = 0
$skipped = 0

foreach ($squad in Get-ChildItem $squadsDir -Directory) {
    $agentsDir = Join-Path $squad.FullName ".squad\agents"
    if (-not (Test-Path $agentsDir)) { continue }

    $dispatchDir = Join-Path $squad.FullName ".github\agents"
    if (-not (Test-Path $dispatchDir)) {
        New-Item -ItemType Directory -Path $dispatchDir -Force | Out-Null
    }

    foreach ($charterFile in Get-ChildItem $agentsDir -Filter "charter.md" -Recurse) {
        $name = $charterFile.Directory.Name
        if ($name.StartsWith('_')) { $skipped++; continue }
        if ($skipNames -contains $name) { $skipped++; continue }

        $agentName = if ($name -cmatch '^[a-z]') {
            ($name.Substring(0,1).ToUpper() + $name.Substring(1))
        } else { $name }

        $target = Join-Path $dispatchDir "$agentName.agent.md"
        if (Test-Path $target) { $skipped++; continue }

        $body = Get-Content $charterFile.FullName -Raw
        $lines = $body -split "`r?`n"
        $desc = Get-Description -Lines $lines
        if ($desc -match '[:#@]') { $desc = '"' + ($desc -replace '"','\"') + '"' }

        $frontmatter = "---`nname: $agentName`ndescription: $desc`n---`n`n"
        Set-Content -Path $target -Value ($frontmatter + $body) -NoNewline -Encoding UTF8
        $created++
        Write-Host ("created: {0,-32} -> {1}" -f $squad.Name, "$agentName.agent.md")
    }
}

Write-Host ""
Write-Host "Created: $created   Skipped: $skipped"

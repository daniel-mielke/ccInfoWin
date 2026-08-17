<#
.SYNOPSIS
    Installs or removes a fake workflow run in the live Claude Code session, so the app's workflow
    row and its tooltip can be inspected without waiting for a real multi-agent run to happen.

.DESCRIPTION
    Copies .planning/mockdata/workflow-run/ into the session directory the app is currently reading:

        <session>/subagents/workflows/wf_mock0001-000/   agent transcripts + journal.jsonl
        <session>/workflows/scripts/*-wf_mock0001-000.js the meta block (name, description, phases)

    The agent transcripts' LastWriteTime is then stamped into the FUTURE. JsonlService only shows a
    run whose newest agent file is younger than 30 seconds, so without this the row appears for one
    poll tick and vanishes. A future stamp keeps it on screen for the whole inspection. It is also
    what the tooltip reports as the start time, since that comes from the run directory.

    No completed-run JSON is written: the app does not read one. That file only exists after a run
    finishes, and a finished run has already been dropped by the staleness gate.

.PARAMETER Remove
    Deletes the three copied paths again. Stop the app first — it holds read handles on the
    transcripts.

.PARAMETER KeepAliveMinutes
    How far into the future to stamp the transcripts. Default 20.

.PARAMETER SessionDirectory
    Overrides session auto-detection (newest transcript in this repo's Claude Code project folder).

.EXAMPLE
    .\.planning\mockdata\mock-workflow.ps1
    .\.planning\mockdata\mock-workflow.ps1 -Remove
#>
[CmdletBinding()]
param(
    [switch]$Remove,
    [int]$KeepAliveMinutes = 20,
    [string]$SessionDirectory
)

$ErrorActionPreference = 'Stop'

$RunId  = 'wf_mock0001-000'
$Source = Join-Path $PSScriptRoot 'workflow-run'

function Resolve-SessionDirectory {
    if ($SessionDirectory) { return $SessionDirectory }

    # Claude Code names a project folder after its path with ':' and '\' replaced by '-'.
    $repoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path.TrimEnd('\')
    $projectName = $repoRoot -replace '[:\\]', '-'
    $projectDir  = Join-Path $env:USERPROFILE ".claude\projects\$projectName"

    if (-not (Test-Path $projectDir)) {
        throw "No Claude Code project folder at $projectDir. Pass -SessionDirectory explicitly."
    }

    # The session the app shows is the one being written right now: newest transcript wins.
    $newest = Get-ChildItem -Path $projectDir -Filter '*.jsonl' -File |
              Sort-Object LastWriteTime -Descending |
              Select-Object -First 1

    if (-not $newest) { throw "No session transcripts in $projectDir." }

    # <session-uuid>.jsonl -> <session-uuid>/ , the directory holding subagents/ and workflows/.
    return Join-Path $projectDir $newest.BaseName
}

$session   = Resolve-SessionDirectory
$runDir    = Join-Path $session "subagents\workflows\$RunId"
$scriptDir = Join-Path $session 'workflows\scripts'

if ($Remove) {
    if (Test-Path $runDir) {
        Remove-Item $runDir -Recurse -Force
        Write-Host "removed  $runDir"
    }

    Get-ChildItem -Path $scriptDir -Filter "*$RunId.js" -File -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "removed  $($_.FullName)" }

    Write-Host "`nMock run removed. The row disappears within one poll interval." -ForegroundColor Green
    return
}

if (-not (Test-Path $Source)) { throw "Mock data missing at $Source." }

New-Item -ItemType Directory -Path $runDir -Force | Out-Null
New-Item -ItemType Directory -Path $scriptDir -Force | Out-Null

Copy-Item -Path (Join-Path $Source "subagents\workflows\$RunId\*") -Destination $runDir -Force
Copy-Item -Path (Join-Path $Source 'workflows\scripts\*.js') -Destination $scriptDir -Force

# The staleness gate compares LastWriteTime against (now - 30s). Stamping forward keeps the row
# visible; it is the same trick used to inspect finished real runs.
$until = (Get-Date).AddMinutes($KeepAliveMinutes)
Get-ChildItem -Path $runDir -Filter 'agent-*.jsonl' -File | ForEach-Object { $_.LastWriteTime = $until }

Write-Host "installed into $session" -ForegroundColor Green
Write-Host "  run id : $RunId"
Write-Host "  visible until : $($until.ToString('HH:mm:ss')) (re-run this script to extend)"
Write-Host "`nExpected row  : (gear) $RunId - 15/15 Agents fertig - 358K Tokens"
Write-Host "Expected tooltip name: 'code-clone-review'  (NOT 'NOT A PHASE' anywhere in the phases)"
Write-Host "`nRemove with   : .\.planning\mockdata\mock-workflow.ps1 -Remove"

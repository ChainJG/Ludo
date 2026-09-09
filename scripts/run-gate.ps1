param([string]$BaseRef = '')
$ErrorActionPreference = 'Stop'
$taskGate = Get-Content -LiteralPath '.github/bot-gate.json' -Raw | ConvertFrom-Json
$taskCandidate = $taskGate.candidate
$taskChampion = $taskGate.champion
if ($BaseRef -and $BaseRef -match '^[a-fA-F0-9]{40}$') {
    $taskAdded = git diff --name-only --diff-filter=A $BaseRef HEAD -- src/Ludo.Bots/Versions
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect candidate bot changes.' }
    $taskVersions = @($taskAdded | ForEach-Object { if ($_ -match 'V(\d+)\.cs$') { [int]$Matches[1] } })
    if ($taskVersions.Count -gt 0) {
        $taskCandidate = 'v' + ($taskVersions | Measure-Object -Maximum).Maximum
        $taskChampion = $taskGate.currentChampion
    }
}
dotnet apps/Ludo.Cli/bin/Release/net10.0/Ludo.Cli.dll gate --candidate $taskCandidate --champion $taskChampion --games $taskGate.games --seed $taskGate.seed --parallel 2 --out artifacts/regression-gate.json
exit $LASTEXITCODE

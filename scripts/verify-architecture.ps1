param([string]$BaseRef = '')
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskManifestPath = Join-Path $taskRoot 'src/Ludo.Bots/releases.json'
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json -AsHashtable
function Assert-Frozen($entries) {
    foreach ($entry in $entries.GetEnumerator()) {
        $taskFile = Join-Path $taskRoot $entry.Key
        $taskVersionsRoot = [System.IO.Path]::GetFullPath((Join-Path $taskRoot 'src/Ludo.Bots/Versions')) + [System.IO.Path]::DirectorySeparatorChar
        $taskResolved = [System.IO.Path]::GetFullPath($taskFile)
        $taskNeuralFiles = @('src/Ludo.Bots/NeuralNetwork.cs','src/Ludo.Bots/Models/neural-v7.json')
        if (-not $taskResolved.StartsWith($taskVersionsRoot, [System.StringComparison]::OrdinalIgnoreCase) -and $entry.Key -notin $taskNeuralFiles) { throw 'Invalid release manifest path.' }
        if (-not (Test-Path -LiteralPath $taskFile)) { throw "Released bot source removed: $($entry.Key)" }
        $taskHash = (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($taskHash -ne $entry.Value) { throw "Released bot changed: $($entry.Key). Add a new version instead." }
    }
}
Assert-Frozen $taskManifest
if ($BaseRef -and $BaseRef -match '^[a-fA-F0-9]{40}$') {
    $taskPriorPath = git ls-tree --name-only $BaseRef -- src/Ludo.Bots/releases.json
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the base revision.' }
    if ($taskPriorPath) {
        $taskPrior = git show "${BaseRef}:src/Ludo.Bots/releases.json"
        if ($LASTEXITCODE -ne 0) { throw 'Could not read the base release manifest.' }
        Assert-Frozen ($taskPrior -join "`n" | ConvertFrom-Json -AsHashtable)
    }
}
$taskSource = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/Ludo.Engine'),(Join-Path $taskRoot 'src/Ludo.Bots') -Filter '*.cs' -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
foreach ($taskFile in $taskSource) {
    $taskText = Get-Content -LiteralPath $taskFile.FullName -Raw
    if ($taskText -match 'new\s+(System\.)?Random\s*\(|Random\.Shared|Math\.random\s*\(') { throw "Unseeded randomness found in $($taskFile.FullName)" }
}
$taskEngineProject = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src/Ludo.Engine/Ludo.Engine.csproj') -Raw)
if ($taskEngineProject.SelectNodes('//PackageReference|//ProjectReference').Count -ne 0) { throw 'The engine must have zero runtime/project dependencies.' }
foreach ($taskProjectName in @('Arena','Warzone')) {
    $taskProject = [xml](Get-Content -LiteralPath (Join-Path $taskRoot "apps/Ludo.$taskProjectName/Ludo.$taskProjectName.csproj") -Raw)
    if (-not ($taskProject.SelectNodes('//ProjectReference') | Where-Object Include -match 'Ludo.Presentation')) { throw "$taskProjectName must reference the shared presentation/core chain." }
    if ($taskProject.SelectNodes('//Compile[@Include]').Count -ne 0) { throw 'Front ends must reference shared libraries, not link copies of engine source.' }
}
Write-Output 'Verified frozen bot files, seeded randomness and shared-engine project boundaries.'

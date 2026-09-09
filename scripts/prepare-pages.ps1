param([string]$BasePath = '/Ludo/', [string]$OutputDirectory = 'artifacts/arena/wwwroot')
$ErrorActionPreference = 'Stop'
if (-not $BasePath.StartsWith('/') -or -not $BasePath.EndsWith('/') -or $BasePath.Contains('"') -or $BasePath.Contains('<')) { throw 'BasePath must be an absolute URL path ending in /.' }
$taskIndex = Join-Path $OutputDirectory 'index.html'
if (-not (Test-Path -LiteralPath $taskIndex)) { throw 'Publish Arena before preparing GitHub Pages.' }
$taskHtml = Get-Content -LiteralPath $taskIndex -Raw
$taskHtml = [regex]::Replace($taskHtml, '<base href="[^"]*"\s*/>', ('<base href="' + $BasePath + '" />'))
[System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $taskIndex), $taskHtml)
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path -LiteralPath $OutputDirectory) '.nojekyll'), '')
Write-Output "Prepared Arena for $BasePath"

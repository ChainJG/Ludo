$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')
dotnet run --project apps/Ludo.Arena -c Release --no-launch-profile --urls http://localhost:5251

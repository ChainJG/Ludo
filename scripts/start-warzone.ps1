$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')
dotnet run --project apps/Ludo.Warzone -c Release

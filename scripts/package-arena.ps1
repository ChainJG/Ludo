param([string]$OutputDirectory = 'artifacts/arena-release/wwwroot', [string]$ArchivePath = 'artifacts/Arena-GitHub-Pages.zip')
$ErrorActionPreference = 'Stop'
$taskOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$taskArchive = [System.IO.Path]::GetFullPath($ArchivePath)
if ($taskArchive.StartsWith($taskOutput + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the ZIP outside the website directory.' }
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($taskArchive)) | Out-Null
$taskStream = [System.IO.File]::Open($taskArchive, [System.IO.FileMode]::Create)
$taskZip = [System.IO.Compression.ZipArchive]::new($taskStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskOutput -File -Recurse -Force) {
        $taskRelative = [System.IO.Path]::GetRelativePath($taskOutput, $taskFile.FullName).Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, $taskFile.FullName, $taskRelative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskZip.Dispose(); $taskStream.Dispose() }
Write-Output "Packaged Arena: $taskArchive"

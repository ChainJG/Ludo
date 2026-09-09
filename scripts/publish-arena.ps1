param(
    [string]$Message = 'Update Arena',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Push-Location -LiteralPath $taskRoot
try {
    if ([string]::IsNullOrWhiteSpace($Message)) { throw 'Provide a short description of the update.' }
    $taskBranch = git branch --show-current
    if ($LASTEXITCODE -ne 0 -or $taskBranch -ne 'main') { throw 'Switch to main before publishing Arena.' }
    Write-Output 'Checking the game and website…'
    & (Join-Path $PSScriptRoot 'verify-architecture.ps1')
    dotnet build apps/Ludo.Arena -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Arena did not build. No changes were pushed.' }
    dotnet test tests/Ludo.Engine.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'The game-rule tests failed. No changes were pushed.' }
    dotnet test tests/Ludo.Runner.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'The bot and training tests failed. No changes were pushed.' }
    node --test tests/browser/motion.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'The animation tests failed. No changes were pushed.' }
    if ($CheckOnly) { Write-Output 'Checks passed. No commit or push was made.'; return }

    $taskRemote = git remote get-url origin
    if ($LASTEXITCODE -ne 0 -or $taskRemote -notin @('https://github.com/ChainJG/Ludo.git','git@github.com:ChainJG/Ludo.git')) {
        throw 'origin must point to ChainJG/Ludo. No changes were pushed.'
    }
    git fetch origin main
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch GitHub. Check your Git sign-in and connection.' }
    git merge-base --is-ancestor origin/main HEAD
    if ($LASTEXITCODE -ne 0) { throw 'GitHub has changes missing locally. Pull and resolve them, then run this again.' }

    # Include the shared core and build inputs required by Arena; leave unrelated desktop edits alone.
    $taskPaths = @('apps/Ludo.Arena','apps/Ludo.BotHost','apps/Ludo.Cli','src','assets/ludo',
        'tests/Ludo.Engine.Tests','tests/Ludo.Runner.Tests','tests/Ludo.HostFixture','tests/browser',
        '.github','scripts','docs','README.md','Directory.Build.props','global.json','Ludo.sln',
        '.gitattributes','.gitignore','package.json','package-lock.json','Update-Arena.cmd')
    $taskStaged = @(git diff --cached --name-only)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the staging area.' }
    foreach ($taskFile in $taskStaged) {
        if (-not ($taskPaths | Where-Object { $taskFile -eq $_ -or $taskFile.StartsWith($_ + '/') })) {
            throw "An unrelated file is already staged: $taskFile. Commit or unstage it before publishing Arena."
        }
    }
    git add --all -- @taskPaths
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage the Arena update.' }
    git diff --cached --quiet
    $taskDifference = $LASTEXITCODE
    if ($taskDifference -eq 1) {
        git commit -m $Message
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the update commit.' }
    } elseif ($taskDifference -ne 0) { throw 'Could not inspect staged changes.' }
    $taskAhead = git rev-list --count origin/main..HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not compare this update with GitHub.' }
    if ([int]$taskAhead -eq 0) {
        Write-Output 'No new update to publish. GitHub already has these changes.'
        Write-Output 'Arena: https://chainjg.github.io/Ludo/'
        return
    }
    git push origin HEAD:main
    if ($LASTEXITCODE -ne 0) { throw 'The push did not complete. Your local commit is preserved; fix the connection and retry.' }
    Write-Output 'Update pushed. GitHub will validate and deploy Arena automatically.'
    Write-Output 'Progress: https://github.com/ChainJG/Ludo/actions/workflows/pages.yml'
    Write-Output 'Arena: https://chainjg.github.io/Ludo/'
} finally { Pop-Location }

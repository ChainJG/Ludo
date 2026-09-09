# Publish an Arena update

- Play: https://chainjg.github.io/Ludo/
- Source: https://github.com/ChainJG/Ludo
- Deployment progress: https://github.com/ChainJG/Ludo/actions/workflows/pages.yml

## The easiest way on this computer

Save your changes, then double-click **Update-Arena.cmd** in the project folder. Leave the window open until it reports that the update was pushed. GitHub then builds, tests and publishes the website automatically. Follow the deployment-progress link to see when it is live.

For a descriptive update message, run this from the project folder:

```powershell
./scripts/publish-arena.ps1 -Message "Improve Arena's dice animation"
```

`npm run publish:arena` performs the same update with the default commit message. To run the local checks without committing or pushing, use:

```powershell
./scripts/publish-arena.ps1 -CheckOnly
```

The script needs Git, PowerShell 7, the pinned .NET SDK and Node.js. Your existing GitHub sign-in in Git Credential Manager is used by Git; no token is stored in the repository. See the README for the initial .NET/WebAssembly setup.

## What happens

The update script verifies frozen bot files, builds Arena, and runs the engine, bot/training and animation tests. It then fetches `main`, refuses to overwrite newer GitHub work, stages Arena and its shared dependencies, commits, and pushes without force.

The staged scope includes Arena, the shared C# libraries, artwork, browser/core tests, CLI/bot-host build inputs, workflows, scripts and documentation. Uncommitted Warzone UI edits and desktop-only tests stay local. An unrelated file already in the staging area stops the shortcut so that it cannot accidentally be included in the update. Previously committed changes on `main` are pushed normally.

GitHub Pages uses **GitHub Actions** as its source. Arena-related pushes to `main` trigger the deployment workflow. It runs core tests, creates exact native replay fixtures, publishes the static WebAssembly site and checks the published browser game before deploying. A failed check prevents deployment, leaving the previous successful site available. No separate server, manual ZIP upload or branch containing generated HTML is needed.

## Normal Git updates also work

You can use Visual Studio, GitHub Desktop, or ordinary Git to commit and push to `main`. The same deployment workflow runs for Arena and shared-core changes. A Warzone-only UI change runs the general CI workflow but does not rebuild the public website.

To redeploy unchanged code, open the deployment-progress link, select **Run workflow**, and choose `main`.

## When an update stops

- Local checks failed: fix the reported error and run the shortcut again. Nothing was pushed.
- GitHub has newer changes: pull them, resolve any conflicts, then retry. The shortcut never resets your work or force-pushes.
- Authentication failed: sign into `ChainJG` through Git Credential Manager, Visual Studio or GitHub Desktop and retry.
- GitHub deployment failed: open the failing workflow step. The last successful site remains live; push a fix or rerun a transient failure.
- Restore an earlier release: revert the unwanted commit on `main` and push the revert. This preserves history and redeploys the earlier behavior.

The repository is public. Build outputs, local saves, training runs and credentials are excluded by `.gitignore`. The trained v7 model distributed with the source is included intentionally; private experimental training outputs belong in `artifacts/`.

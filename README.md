# Ludo

[Play Arena](https://chainjg.github.io/Ludo/) · [Deployment progress](https://github.com/ChainJG/Ludo/actions/workflows/pages.yml) · [Publish an update](docs/PUBLISHING.md)

One C# game engine, seven versioned C# bots, and two applications:

- **Arena** is the human-playable website. Standalone Blazor WebAssembly runs entirely in the browser and publishes to GitHub Pages. It uses the supplied board, dice and token images, with phone/desktop layouts, keyboard-accessible moves, readable board descriptions, local save/resume and personal results.
- **Warzone** is the Windows WPF laboratory. Spectate with pause, turn stepping, adjustable speed, move logs and candidate scores; run parallel tournaments; inspect confidence intervals, ratings and finishing metrics; export complete replays and CSV results.

## Open and run

Open `Ludo.sln` in a .NET 10-capable IDE. The repository pins the SDK in `global.json`.

```powershell
dotnet workload install wasm-tools
dotnet build Ludo.sln -c Release
dotnet test Ludo.sln -c Release

# Arena: open http://localhost:5251
./scripts/start-arena.ps1

# Warzone, in another terminal
./scripts/start-warzone.ps1
```

Warzone requires Windows and .NET Desktop Runtime 10. Arena visitors need only a modern browser with WebAssembly support. Browser decisions run in a separate worker; there is no backend, account system, analytics or online multiplayer. Multiple human seats provide local hot-seat play.

## Publish Arena to GitHub Pages

This repository deploys Arena automatically from `main`. **Double-click `Update-Arena.cmd`** to check, commit and push an Arena update, or use `./scripts/publish-arena.ps1 -Message "Describe your update"`. See [the publishing guide](docs/PUBLISHING.md) for the exact scope and troubleshooting. Normal pushes from Visual Studio or GitHub Desktop also work.

For a new fork:

1. Put this repository in your GitHub repository.
2. In **Settings → Pages**, set **Source** to **GitHub Actions**.
3. Push to `main`, or run **Publish Arena to GitHub Pages** manually in Actions.

The workflow tests the core, publishes only Arena's static files, obtains the real Pages base path (including custom-domain settings), adds `.nojekyll`, and deploys. It never publishes Warzone or requires a .NET server.

For a manual upload to a repository named `Ludo`:

```powershell
dotnet publish apps/Ludo.Arena -c Release -o artifacts/arena-release
./scripts/prepare-pages.ps1 -BasePath /Ludo/ -OutputDirectory artifacts/arena-release/wwwroot
./scripts/package-arena.ps1
```

Upload the **contents** of `wwwroot`, including `.nojekyll` and `_framework`, to the location your Pages system serves. The ZIP contains that same layout. Change `-BasePath` for a different repository name; use `/` for a user site or root custom domain. Opening `index.html` directly from disk is not supported: use an HTTP static server.

The source repository is [ChainJG/Ludo](https://github.com/ChainJG/Ludo). Its GitHub Pages workflow publishes Arena at [chainjg.github.io/Ludo](https://chainjg.github.io/Ludo/).

The verified build from 9 September 2026 is in `artifacts/arena-game-release/wwwroot`; its upload archive is `artifacts/Arena-GitHub-Pages.zip`, prepared for `/Ludo/`. The Windows build is `artifacts/warzone-app/Ludo.Warzone.exe`; keep its accompanying files and folders together.

## Neural training and game animation

Both game screens now put the dice beside the active avatar. Arena includes dice rolls, glowing/bobbing legal tokens, square-by-square movement and capture-return effects. Warzone skips piece travel for fast spectating. Both respect reduced-motion preferences.

Warzone has a **Neural training** tab with teacher learning, game-result learning, checkpoints, continuation and validation. Arena can import the exported C# neural model. See [the training guide](docs/NEURAL-TRAINING.md) and [the latest validation](docs/UI-AND-NEURAL-VALIDATION.md). Neural v7 is experimental; it has not passed the promotion gate against v2.

## Bot laboratory

```powershell
dotnet run --project apps/Ludo.Cli -c Release -- bots
dotnet run --project apps/Ludo.Cli -c Release -- match --bots v1,v2 --seed 12345 --out artifacts/match.json
dotnet run --project apps/Ludo.Cli -c Release -- replay --file artifacts/match.json
dotnet run --project apps/Ludo.Cli -c Release -- batch --bots v1,v2 --games 10000 --parallel 4 --out artifacts/results.json
dotnet run --project apps/Ludo.Cli -c Release -- batch --players 4 --bots v1,v2,v3,v4 --games 1000 --parallel 4
dotnet run --project apps/Ludo.Cli -c Release -- gate --candidate v2 --champion v1 --games 500 --seed 880301
dotnet run --project apps/Ludo.Cli -c Release -- tune --games 100 --rounds 4 --seed 9000 --out artifacts/weights.json
dotnet run --project apps/Ludo.Cli -c Release -- gate --candidate-config artifacts/weights.json --champion v2 --games 500 --seed 123456
```

All CLI matches use isolated bot processes by default. `--inline` is an explicitly faster experimental mode for trusted built-in bots and cannot interrupt a hung function. Do not use it as the production adjudicator.

Two-player round robins require a multiple of `2 × number of pairings` games. Four-player runs require four distinct participants and a multiple of four games, using cyclic seat rotation. Every rotation shares a dice seed. Game results are stored in schedule order, so changing worker count doesn't change decisions, records or rating updates. The shared underlying dice sequence does not imply identical rolls for each bot after different moves change who rolls next.

Incomplete games stop at 20,000 rolls and are clearly marked; no winner is invented. Exported JSON includes resolved rules, seat versions/configurations, seeds, all actions, incidents, timings and full finishing order. CSV has one row per seat per game. In Warzone, **Replay longest** opens the longest action log; **Replay biggest upset** opens a win by the participant with the lowest aggregate win rate in that tournament.

The regression gate requires a win rate strictly above 55% and a Wilson 95% lower bound above 50%. It also requires no incidents/incomplete games and a conservative lower bound above 50% using entire paired seed groups. This extra bound prevents correlated mirrored games from overstating evidence. CI automatically selects a newly added version file as the candidate on pull requests, against `.github/bot-gate.json`'s current champion.

Tuning produces experimental weight configurations, not a newly released bot. Use a separate evaluation seed suite before adopting them. Higher version numbers are available for comparison and are not claims of greater strength.

Arena offers v1–v4, Compact search v6 and experimental Neural v7. Search v5 remains available in Warzone and the CLI: its deeper search exceeded the 50 ms decision budget in the browser benchmark. Arena defaults to Heuristic v2, which passed the held-out baseline gate. Compact search v6 limits expansion to 72 nodes; it is an experimental alternative, not a promoted champion. Browser speed varies by device.

Warzone rounds tournament game counts up to a complete set of seat rotations and shows the adjusted count before running.

## Structure

| Project | Owns |
| --- | --- |
| `src/Ludo.Engine` | Rules, immutable state, seeded dice, legal transitions, serialization, replay |
| `src/Ludo.Bots` | C# bot contracts, immutable version files, registry, evaluation weights |
| `src/Ludo.Runner` | Match sessions, incidents, tournament scheduling, metrics, exports |
| `src/Ludo.Native` | Desktop/CLI process isolation and watchdogs |
| `src/Ludo.Presentation` | Shared board geometry and human-readable event descriptions |
| `apps/Ludo.Arena` | Browser interface and worker adapter |
| `apps/Ludo.Warzone` | WPF interface |
| `apps/Ludo.Cli`, `apps/Ludo.BotHost` | Automation runner and isolated native bot worker |

The engine has no package or project dependencies. Both front ends consume the same project chain. Search asks that same engine about hypothetical dice and moves. Presentation only maps existing state to artwork positions; it owns no game rules.

## Released bot versions

`Versions/RandomV1.cs` through `Versions/NeuralV7.cs` are frozen by SHA-256 in `src/Ludo.Bots/releases.json`. CI checks both the current manifest and the base branch's manifest, so modifying both an old file and its hash does not bypass the guard. Add a new version file and registry entry for improvements. Shared behavior used by released bots must remain compatible; copy an evaluator into a new version if its semantics change. Engine behavior changes require an engine version increment and appropriately separated benchmark/replay data.

See [the agreed rules](docs/RULES.md) and [validation notes](docs/VALIDATION.md).

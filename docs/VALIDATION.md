# Validation — 8 September 2026

Built on Windows with .NET SDK 10.0.301 and Node 24.19.0. Engine version: 1.0.0. Released bot sources v1–v6 are pinned in `src/Ludo.Bots/releases.json`.

## Applications and shared core

- Release solution build: zero warnings and zero errors.
- Automated tests: 55 engine tests and 19 runner tests passed. Coverage includes safe stars, protected stacks and coexistence, no retroactive capture, third-six handling, exact home entry, finishing order, rule toggles, deterministic replay, process failures, illegal decisions and timeout recovery.
- Architecture guard passed: one dependency-free engine, shared project references, seeded randomness, unchanged released bot source hashes.
- Warzone initialized its match and rendered the supplied artwork in an offscreen WPF smoke test. The rendered layout was inspected. The published application's bundled bot host completed a 372-action match with zero incidents.
- Final Arena publish was tested as a static website under `/Ludo/`, matching a repository-based GitHub Pages deployment. No failing resource requests or browser page errors occurred.
- Chromium tests compared native and WebAssembly decisions, candidate scores and expansion counts for every released bot in opening and crowded four-player positions. Choices matched exactly; scores matched within 1e-8.
- Complete two-player and four-player native recordings replayed to byte-for-byte identical serialized states in the browser. Invalid inputs, legal human actions and refresh/resume with a pending decision also passed.
- Responsive phone/desktop CSS, keyboard move alternatives, readable board descriptions, live announcements and reduced-motion support are implemented. Browser testing used structured game APIs; it did not include screenshot, DOM-layout or physical-phone validation. Phone usability and performance should still be tried on actual devices.

## Browser decision timing

Single measured decisions in the final trimmed publish, in headless Chromium on the development machine. These are observations, not latency guarantees or a strength ranking.

| Bot | Opening | Crowded four-player position | Public Arena selector |
| --- | ---: | ---: | --- |
| Random v1 | 0.1 ms | <0.1 ms | Yes |
| Heuristic v2 | 0.1 ms | 0.1 ms | Yes; default |
| Weighted v3 | 0.5 ms | 4.7 ms | Yes |
| Expectimax v4 | 1.5 ms | 15.9 ms | Yes |
| Search v5 | 14.7 ms | 107.5 ms | No; desktop comparison |
| Compact search v6 | 5.1 ms | 14.8 ms | Yes; experimental |

Search v5 exceeded the default 50 ms budget in the browser. Its frozen implementation remains available for desktop research. Compact search v6 uses the same released evaluator with a 72-expansion budget. Slower devices and other positions may take longer. Browser decisions run in a separate worker with execution timing and a watchdog; native production matches use isolated worker processes. The optional native WebMCP registry was unavailable in the tested browser, so that integration remains feature-detected and was not exercised.

## Tournament evidence

All listed tournament runs used isolated native processes, a 50 ms decision budget, paired seeds or cyclic seat rotations, and had zero incidents and zero incomplete games.

| Run | Result |
| --- | --- |
| 10,000-game v2 vs v1 baseline | v2 won 8,658/10,000 (86.58%); Wilson 95% interval 85.9–87.2%; approximately 295 games/s |
| Held-out regression gate, seed 880301 | v2 won 436/500 (87.2%); Wilson interval 84.0–89.8%; conservative paired-seed lower bound 78.6%; PASS |
| 500-game v1–v5 round robin | Each bot played 200 games; wins: v1 20, v2 102, v3 121, v4 115, v5 142 |
| Final 100-game four-player run, seed 90826 | Wins: v2 25, v3 33, v4 17, v6 25; approximately 24 games/s |

The round robin and four-player samples are exploratory. They do not establish that later versions are stronger. v6 has not passed a promotion gate against v2. A two-round, 20-game-per-round tuning smoke test saved a valid experimental configuration; this small training sample is not evidence for promotion.

JSON benchmark records and CSV exports are in `artifacts/`. Final four-player CSV validation confirmed 400 seat rows, engine version, rule configuration and bot configuration metadata. Timing data is machine-dependent; choices and results are seeded, while elapsed milliseconds are not deterministic.

## Delivery

- Arena static files: `artifacts/arena-pages/wwwroot`.
- Upload archive: `artifacts/Arena-GitHub-Pages.zip` (13.68 MiB; 177 entries). Verified it contains the root index, `.nojekyll`, runtime, worker scripts and supplied artwork. The directory totals 19.54 MiB including precompressed alternatives; this is not the first-load transfer size.
- Current archive base path: `/Ludo/`. Use `prepare-pages.ps1` and repackage for another repository path or custom domain.
- Warzone: `artifacts/warzone-app/Ludo.Warzone.exe`, alongside its assets and `bot-host` directory. Requires .NET Desktop Runtime 10 on Windows.
- GitHub Actions workflows are included, but this workspace has no Git repository or remote. No live deployment or remote workflow execution has been verified.

See the root README for repeatable build, run, test and publishing instructions.

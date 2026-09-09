# Game interface and neural training — 9 September 2026

## Interface changes

Both applications use the board, dice and token artwork in `assets/ludo`. The reference image guided the blue game surface, rounded controls and avatar-based turn presentation. Simple avatar icons are drawn with SVG in Arena and drawing primitives in WPF; no external icon/font service is required.

Arena places avatars around the board and shows the die only beside the active player. Rolling cycles the supplied dice images through a short rotation/bounce before applying the real seeded roll. The displayed animation frames never influence game randomness. Legal pieces glow and stretch/bob at staggered intervals. Moves follow every intervening square with a hop; captured pieces travel backwards along their own route to base, while a ghost floats away. Input is locked during animation, and committed state is saved before movement playback. Reduced-motion preferences bypass dice and travel effects. Fast bots skip their animations.

Warzone uses the same avatar/dice arrangement, rounded controls, blue play area and legal-piece pulse. Match settings have their own tab. Dice animate at normal spectating speed; higher speeds and Instant result skip the roll effect. Piece travel and capture effects are intentionally omitted in Warzone so laboratory runs remain quick. Windows animation preferences disable decorative motion.

## Checks

- Release build: zero warnings and errors.
- 80 C# tests passed: 55 engine tests and 25 runner/presentation/training tests.
- Three motion tests passed: square-by-square hops, reduced-motion bypass, and capture sequencing/cleanup.
- Neural tests verify gradient direction, portable model serialization, malformed-model rejection, and exact continuation versus uninterrupted training.
- Motion-plan tests verify launch, home entry, every travelled square, capture return routes, and protected-stack splitting without invented captures.
- Browser contract checks passed for all seven C# bots, native/WebAssembly choice and score parity, exact two-/four-player replays, legal/invalid actions, refresh/resume, neural model import/rejection, and rejection of a second roll while the first is animating.
- In the browser test, neural v7 inference in the crowded four-player position took approximately 0.3–0.4 ms on the development machine; this is not a guarantee for other devices.
- WPF startup/render smoke test passed, and its offscreen rendering was inspected.
- The bundled neural candidate completed 100 four-player matches with no incidents or incomplete games. The separate 500-game promotion test failed on strength criteria, with no incidents; see the training guide for the complete result.

Browser checks used the application's structured interfaces. They did not include screenshot/DOM-layout inspection or physical-phone testing. The responsive layout and effects are implemented; real-device visual, touch and performance checks remain useful before a public launch.

## Delivery

The refreshed website is packaged for `/Ludo/` in `artifacts/Arena-GitHub-Pages.zip`. The matching source publish is `artifacts/arena-game-release/wwwroot`. The Windows application is `artifacts/warzone-app/Ludo.Warzone.exe`; keep its companion files together. The training guide is `docs/NEURAL-TRAINING.md`.

No GitHub deployment was performed; the workspace is not connected to a Git repository or remote. Existing GitHub Pages build and deployment instructions remain in the README. Earlier 8 September validation notes describe the prior interface and bot versions; this document records the new work.

# Agreed Ludo variant

The user's later rule confirmations override the original impassable-block rule.

- Two or four players; four tokens each. Two-player games use opposite seats (yellow/red).
- Track indices 0–51 clockwise. Entries are 0, 13, 26, 39; additional stars are 8, 21, 34, 47. All eight are safe in every game.
- Relative token progress: -1 base; 0 entry; 0–50 shared track; 51–55 private home squares; 56 final home triangle. The supplied artwork has five lane squares plus the final triangle, giving six home steps.
- A six launches a token onto its entry, without another six steps. Launch restriction can be disabled to allow any roll to launch.
- A six, capture, or reaching final home earns one additional roll. Multiple reasons do not accumulate multiple rolls. Each bonus has its own toggle.
- Three consecutive sixes within a turn discard only the third roll. The previous moves remain. A non-six resets the streak. This rule has its own toggle.
- No legal move always ends the turn, including after a six.
- Exact roll to 56; overshoots cannot move. Finished tokens cannot move.
- On stars, pieces of all colours coexist without capture.
- On an ordinary square, two or more pieces of the same colour protect that colour's stack. Opponents can pass through or land there. Moving a piece away never triggers a capture.
- On each arrival, capture every opposing singleton on an ordinary square. Other colours' protected stacks remain. This includes a third colour landing on a square with two different opposing singletons.
- Stack protection and star safety can be disabled independently. With stack protection disabled, an arrival captures all opposing pieces on an ordinary square. An optional legacy blocking toggle prevents passage and landing on opponent stacks; it is off by default.
- All four tokens home finishes a player. Continue until only one active player remains; assign that player's remaining place without requiring unnecessary final rolls.
- Disqualified players are removed from play. Normal finishers rank first; earlier disqualifications rank below later disqualifications. The result records incidents separately.

## Determinism

The engine uses a versioned mulberry32 generator with unbiased bounded integer draws. It never uses System.Random. Bot streams are independent and cannot reveal or advance the live dice stream. State and bot views use immutable records and immutable collections. Search resolves explicit possible dice through the same engine transitions.

Replay metadata pins the engine version, resolved rules and seed. The action log contains every roll, selected token and disqualification. Replays verify dice and never rerun timing enforcement. Fresh wall-clock-adjudicated matches may differ across hardware; deterministic search budgets keep ordinary bot choices reproducible.

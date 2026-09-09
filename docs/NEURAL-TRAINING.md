# Train a neural Ludo bot

Warzone now has a **Neural training** tab. Arena can load the same exported network and run it entirely in the browser. Training and inference are written in C#; no Python service, GPU, API key or server is required.

## In Warzone

1. Open **Match setup** and choose two or four players and a training seed.
2. Open **Neural training**. Start with 2,000 games, 200 teacher games, a checkpoint every 200 games, 100 validation games and learning rate 0.015. Weighted v3 is the default teacher; Heuristic v2 is also available.
3. Select **Train and save** and choose an output filename. Progress and checkpoint results appear in the window.
4. The best validation checkpoint is loaded into Yellow's bot slot when the run finishes. In Match setup, choose Heuristic v2 for the second seat. Run **Regression gate (500)** in Tournaments using a fresh seed.
5. To continue learning, load the `.latest.json` file, enable **Continue loaded network**, and use the same training seed, teacher, player count and learning settings. The game count is the number of additional games. Teacher games refer to the total lifetime warmup, not an additional warmup each time.

Training uses the agreed default rules. The custom rule switches in Match setup apply to ordinary matches and tournament gates, not training. Validation always uses two-player default-rule games against Heuristic v2. Four-player training uses finishing-place rewards and rotating learner seats.

Stopping a run preserves the last completed evaluation checkpoint. The current unfinished game and games since that checkpoint are not saved. Starting a continuation compares its initial model and new checkpoints; it does not automatically import an older run's best-model selection history.

## Saved files

For an output named `my-bot.json`:

| File | Purpose |
| --- | --- |
| `my-bot.json` | Best checkpoint on the fixed validation suite; load this to play |
| `my-bot.latest.json` | Latest network; load this to continue training |
| `my-bot.training.json` | Training settings, both models, checkpoint scores and Wilson intervals |

Writes replace files atomically one at a time. Keep each experiment under a new filename if you want to compare training runs. Exported models contain the input schema, architecture, numeric weights, game count and seed. Model imports validate dimensions and finite weight ranges. The browser limits imported files to 256 KiB.

## In Arena

Open the menu, expand **Match settings**, and choose **Load trained bot**. Select the exported best-model JSON, choose **My trained network** for a bot seat, and start a new game. Loaded model weights are included in the local match save and downloaded replay, so resuming that match retains its opponent. Personal win/loss totals currently aggregate all custom neural checkpoints under version v7.

## Command-line training

```powershell
dotnet run --project apps/Ludo.Cli -c Release -- train --games 12000 --warmup 1500 --teacher v3 --evaluate-every 1500 --evaluation-games 200 --learning-rate 0.025 --seed 74009 --out artifacts/my-bot.json

dotnet run --project apps/Ludo.Cli -c Release -- train --resume artifacts/my-bot.latest.json --games 2000 --warmup 1500 --teacher v3 --learning-rate 0.025 --seed 74009 --out artifacts/my-bot-next.json

dotnet run --project apps/Ludo.Cli -c Release -- gate --candidate-config artifacts/my-bot.json --champion v2 --games 500 --seed 123987 --out artifacts/my-bot-gate.json
```

Use `--players 4` for four-player training. CLI continuation uses the model's training seed unless you supply `--seed`. Changing it deliberately starts a different training stream. Reproducing uninterrupted training requires keeping the same seed, teacher, player count, lifetime warmup and learning rate; no optimizer momentum is hidden outside the checkpoint.

## How it learns

The model scores each legal move with 24 actor-relative features, one 24-unit tanh hidden layer, and one output. Features describe progress, home completion, captures, protection, nearby opponents and the prospective move. All candidate outcomes come from the shared engine. Inference chooses the highest score with a stable token-order tie break.

The initial teacher stage uses supervised softmax cross entropy with 5% label smoothing. The later stage samples moves from the network's softmax distribution and updates it from the final finishing-place reward. Opponents include v1, v2, v3 and a frozen snapshot of the learner at the start of a game. This is episodic REINFORCE with backpropagation, a small entropy bonus, mean per-decision gradients and norm clipping. It follows the policy-gradient approach described in [Sutton et al., Policy Gradient Methods with Function Approximation](https://proceedings.neurips.cc/paper/1999/file/464d828b85b0bed98e80ade0a5c43b0f-Paper.pdf); the compact implementation and Ludo features are project-specific.

Validation reuses a fixed, separate, mirrored seed suite so checkpoint changes are comparable. Because the best checkpoint is selected on that suite, its score is not an unbiased final performance estimate. Always use a fresh held-out gate before promotion. The gate also considers paired-seed uncertainty, incidents and incomplete games. Repeatedly changing the final test seed until a model passes would defeat that separation.

## Included experimental network

The included v7 model was selected from a 12,000-game run (seed 74009, Weighted v3 teacher for 1,500 games, learning rate 0.025, 200 validation games every 1,500 training games). The initial network scored 4.5% against v2 on validation. The best checkpoint, after 7,500 games, scored 60.5%; the final checkpoint scored 56.5%. The best checkpoint is bundled, and the latest remains available to continue learning.

On a separate 500-game suite (seed 941077), the bundled candidate won **273/500, or 54.6%**, with Wilson 95% interval **50.2–58.9%** and conservative paired-seed lower bound **46.0%**. There were no incidents or incomplete games. It **failed the promotion gate**, so Arena's default remains Heuristic v2. In a separate 100-game four-player sample it won 26 games, an exploratory result rather than a strength ranking.

There is no claim that this is the best possible Ludo bot. The system now provides a measured improvement loop. Useful next experiments are larger fresh training suites, alternative teacher schedules, learning rates and four-player experience. Changes to the released network's feature schema or inference require a new bot version. v7 inference, its feature code and its bundled weights are frozen by the release manifest; new trained JSON checkpoints are experiments, not edits to that release.

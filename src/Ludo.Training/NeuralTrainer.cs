using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Runner;

namespace Ludo.Training;

public sealed record TrainingConfig(int Games = 2000, int WarmupGames = 200, int EvaluateEvery = 200,
    int EvaluationGames = 100, uint Seed = 73001, double LearningRate = 0.015, int Players = 2, string Teacher = "v3");
public sealed record TrainingPoint(int Games, double WinRate, double Lower95, double Upper95, int EvaluationGames);
public sealed record TrainingCheckpoint(TrainingConfig Config, BotSpec Latest, BotSpec Best, ImmutableArray<TrainingPoint> History);
public sealed record TrainingProgress(int Completed, int Total, string Stage, double? WinRate = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TrainingCheckpoint))]
public partial class TrainingJsonContext : JsonSerializerContext;

public static class NeuralTrainer
{
    public static async Task<TrainingCheckpoint> TrainAsync(TrainingConfig config, NeuralModel? initial = null,
        IProgress<TrainingProgress>? progress = null, Func<TrainingCheckpoint, Task>? checkpoint = null,
        CancellationToken cancellationToken = default)
    {
        if (config.Games is < 1 or > 1000000 || config.WarmupGames < 0 || config.EvaluateEvery < 1 || config.EvaluationGames < 2
            || config.EvaluationGames % 2 != 0 || config.Players is not (2 or 4) || config.Teacher is not ("v2" or "v3") || !double.IsFinite(config.LearningRate)
            || config.LearningRate <= 0 || config.LearningRate > 0.2) throw new ArgumentException("Invalid training settings. Evaluation games must be positive and even.");
        var model = initial ?? NeuralNetwork.Initialize(config.Seed);
        model.Validate();
        var history = ImmutableArray.CreateBuilder<TrainingPoint>();
        NeuralModel best = model;
        double bestRate = -1;
        var network = new Trainable(model);
        int offset = model.TrainingGames;
        TrainingCheckpoint Snapshot() => new(config, new("v7", Model: model), new("v7", Model: best), history.ToImmutable());

        async Task Evaluate()
        {
            progress?.Report(new(model.TrainingGames - offset, config.Games, "Evaluating against Heuristic v2"));
            // Validation is fixed across checkpoints, separate from generated training streams.
            var result = await Tournaments.RunAsync(new([new("v7", Model: model), new("v2")], new() { PlayerCount = 2 },
                config.EvaluationGames, SeededRng.Derive(config.Seed, 0xF0000001), 1, 10000), () => new InlineBotExecutor(), cancellationToken: cancellationToken);
            if (result.Games.Any(g => g.Truncated || !g.Incidents.IsEmpty)) throw new InvalidOperationException("Validation did not complete cleanly.");
            var stats = Tournaments.Statistics(result)[0];
            history.Add(new(model.TrainingGames, stats.WinRate, stats.Lower95, stats.Upper95, stats.Games));
            if (stats.WinRate > bestRate) { bestRate = stats.WinRate; best = model; }
            progress?.Report(new(model.TrainingGames - offset, config.Games, "Checkpoint evaluated", stats.WinRate));
            if (checkpoint is not null) await checkpoint(Snapshot());
        }
        await Evaluate();
        // Bounded, trusted native training only. This code is never included in Arena.
        await Task.Run(async () =>
        {
            for (int episode = 0; episode < config.Games; episode++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int number = offset + episode;
                uint episodeSeed = SeededRng.Derive(config.Seed, (uint)number + 1);
                var rng = new SeededRng(SeededRng.Derive(episodeSeed, 99));
                var state = GameEngine.CreateGame(new() { PlayerCount = config.Players }, episodeSeed);
                int learner = number % config.Players;
                bool warmup = number < config.WarmupGames;
                IBot teacher = BotRegistry.Create(new(config.Teacher));
                IBot opponent = number % 4 == 3 ? new Bots.Versions.NeuralV7(model)
                    : BotRegistry.Create(new(new[] { "v1", "v2", "v3" }[number % 3]));
                var trajectory = new List<(double[][] Features, int Choice)>();
                while (!GameEngine.IsTerminal(state) && state.View.RollNumber < 20000)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (state.View.Phase == GamePhase.AwaitingRoll) { state = GameEngine.Roll(state); continue; }
                    var legal = GameEngine.GetLegalMoves(state);
                    Move selected;
                    if (state.View.CurrentPlayer != learner) selected = opponent.SelectMove(state.View, legal, rng);
                    else
                    {
                        var features = legal.Select(m => NeuralNetwork.Features(state.View, m)).ToArray();
                        int choice;
                        if (warmup)
                        {
                            selected = teacher.SelectMove(state.View, legal, rng); choice = legal.IndexOf(selected);
                            network.Update([(features, choice)], 1, config.LearningRate, smoothing: .05);
                        }
                        else
                        {
                            var probabilities = network.Probabilities(features);
                            double sample = rng.NextUInt() / ((double)uint.MaxValue + 1);
                            choice = probabilities.Length - 1;
                            for (int i = 0; i < probabilities.Length; i++) { sample -= probabilities[i]; if (sample < 0) { choice = i; break; } }
                            selected = legal[choice]; trajectory.Add((features, choice));
                        }
                    }
                    state = GameEngine.ApplyMove(state, selected);
                }
                if (!GameEngine.IsTerminal(state)) throw new InvalidOperationException("A training game exceeded the roll limit; the last completed checkpoint is preserved.");
                if (!warmup)
                {
                    int place = state.View.FinishingOrder.IndexOf(learner);
                    double reward = 1 - 2.0 * place / (config.Players - 1);
                    network.Update(trajectory, reward, config.LearningRate, entropy: .01);
                }
                model = network.Export($"Trained {number + 1:N0} games", number + 1, config.Seed);
                if ((episode + 1) % 10 == 0) progress?.Report(new(episode + 1, config.Games, warmup ? $"Learning from {BotRegistry.Label(new(config.Teacher))}" : "Learning from game results"));
                if ((episode + 1) % config.EvaluateEvery == 0 || episode + 1 == config.Games) await Evaluate();
            }
        }, cancellationToken);
        return Snapshot();
    }

    // A one-hidden-layer policy, trained by softmax cross entropy then episodic REINFORCE.
    public sealed class Trainable
    {
        private readonly int inputs, hidden;
        private readonly double[] w, b, output;
        public Trainable(NeuralModel model) { model.Validate(); inputs = model.Inputs; hidden = model.Hidden; w = model.InputWeights.ToArray(); b = model.Biases.ToArray(); output = model.OutputWeights.ToArray(); }
        private double[] Activations(double[] features)
        {
            var values = new double[hidden];
            for (int h = 0; h < hidden; h++) { double sum = b[h]; for (int i = 0; i < inputs; i++) sum += w[h * inputs + i] * features[i]; values[h] = Math.Tanh(sum); }
            return values;
        }
        public double[] Probabilities(double[][] features)
        {
            var logits = features.Select(f => Activations(f).Select((a, h) => a * output[h]).Sum()).ToArray();
            double max = logits.Max(); var exps = logits.Select(x => Math.Exp(x - max)).ToArray(); double sum = exps.Sum();
            return exps.Select(x => x / sum).ToArray();
        }
        public void Update(IReadOnlyList<(double[][] Features, int Choice)> trajectory, double reward, double rate, double smoothing = 0, double entropy = 0)
        {
            if (trajectory.Count == 0) return;
            var dw = new double[w.Length]; var db = new double[b.Length]; var dv = new double[output.Length];
            foreach (var step in trajectory)
            {
                var probabilities = Probabilities(step.Features);
                double expectedLog = probabilities.Sum(p => p * Math.Log(Math.Max(1e-15, p)));
                for (int action = 0; action < probabilities.Length; action++)
                {
                    double target = (step.Choice == action ? 1 - smoothing : 0) + smoothing / probabilities.Length;
                    double gradient = reward * (target - probabilities[action])
                        - entropy * probabilities[action] * (Math.Log(Math.Max(1e-15, probabilities[action])) - expectedLog);
                    var values = Activations(step.Features[action]);
                    for (int h = 0; h < hidden; h++)
                    {
                        dv[h] += gradient * values[h];
                        double delta = gradient * output[h] * (1 - values[h] * values[h]); db[h] += delta;
                        for (int i = 0; i < inputs; i++) dw[h * inputs + i] += delta * step.Features[action][i];
                    }
                }
            }
            double norm = Math.Sqrt(dw.Concat(db).Concat(dv).Sum(x => x * x)) / trajectory.Count;
            double scale = rate / trajectory.Count / Math.Max(1, norm / 5);
            void Apply(double[] values, double[] gradients) { for (int i = 0; i < values.Length; i++) values[i] = Math.Clamp(values[i] + scale * gradients[i], -50, 50); }
            Apply(w, dw); Apply(b, db); Apply(output, dv);
        }
        public NeuralModel Export(string name, int games, uint seed) => new(name, 1, inputs, hidden, w.ToImmutableArray(), b.ToImmutableArray(), output.ToImmutableArray(), games, seed);
    }
}

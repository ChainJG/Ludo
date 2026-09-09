using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludo.Engine;

namespace Ludo.Bots;

public sealed record NeuralModel(string Name, int Schema, int Inputs, int Hidden,
    ImmutableArray<double> InputWeights, ImmutableArray<double> Biases, ImmutableArray<double> OutputWeights,
    int TrainingGames = 0, uint TrainingSeed = 0)
{
    public bool Equals(NeuralModel? other) => ReferenceEquals(this, other) || other is not null
        && Name == other.Name && Schema == other.Schema && Inputs == other.Inputs && Hidden == other.Hidden
        && TrainingGames == other.TrainingGames && TrainingSeed == other.TrainingSeed
        && InputWeights.AsSpan().SequenceEqual(other.InputWeights.AsSpan()) && Biases.AsSpan().SequenceEqual(other.Biases.AsSpan())
        && OutputWeights.AsSpan().SequenceEqual(other.OutputWeights.AsSpan());
    public override int GetHashCode() => HashCode.Combine(Name, Schema, Inputs, Hidden, TrainingGames, TrainingSeed);
    public void Validate()
    {
        if (Schema != 1 || Inputs != NeuralNetwork.FeatureCount || Hidden is < 4 or > 64 || string.IsNullOrWhiteSpace(Name) || Name.Length > 100
            || TrainingGames < 0 || InputWeights.IsDefault || Biases.IsDefault || OutputWeights.IsDefault
            || InputWeights.Length != Inputs * Hidden || Biases.Length != Hidden || OutputWeights.Length != Hidden)
            throw new ArgumentException("The neural model has an unsupported shape or metadata.");
        if (InputWeights.Concat(Biases).Concat(OutputWeights).Any(x => !double.IsFinite(x) || Math.Abs(x) > 100))
            throw new ArgumentException("Neural weights must be finite and between -100 and 100.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NeuralModel))]
public partial class NeuralJsonContext : JsonSerializerContext;

// Shared, portable inference. Training lives in Ludo.Training and never ships in the browser.
public static class NeuralNetwork
{
    public const int FeatureCount = 24;
    private static readonly Lazy<NeuralModel> bundled = new(() =>
    {
        using var stream = typeof(NeuralNetwork).Assembly.GetManifestResourceStream("Ludo.Bots.neural-v7.json");
        var model = JsonSerializer.Deserialize(stream ?? throw new InvalidOperationException("The bundled neural model is missing."), NeuralJsonContext.Default.NeuralModel)!;
        model.Validate(); return model;
    });
    public static NeuralModel Bundled => bundled.Value;

    public static NeuralModel Initialize(uint seed, int hidden = 24)
    {
        if (hidden is < 4 or > 64) throw new ArgumentOutOfRangeException(nameof(hidden));
        var rng = new SeededRng(seed);
        double Weight(double scale) => (rng.NextUInt() / (double)uint.MaxValue * 2 - 1) * scale;
        return new("New network", 1, FeatureCount, hidden,
            Enumerable.Range(0, FeatureCount * hidden).Select(_ => Weight(Math.Sqrt(6.0 / (FeatureCount + hidden)))).ToImmutableArray(),
            Enumerable.Repeat(0.0, hidden).ToImmutableArray(),
            Enumerable.Range(0, hidden).Select(_ => Weight(0.2)).ToImmutableArray(), 0, seed);
    }

    public static double Score(NeuralModel model, double[] features)
    {
        double score = 0;
        for (int h = 0; h < model.Hidden; h++)
        {
            double sum = model.Biases[h];
            for (int i = 0; i < model.Inputs; i++) sum += model.InputWeights[h * model.Inputs + i] * features[i];
            score += Math.Tanh(sum) * model.OutputWeights[h];
        }
        return score;
    }

    // Actor-relative features, independent of colour, token labels and hidden dice state.
    public static double[] Features(GameStateView view, Move move)
    {
        int actor = view.CurrentPlayer; var before = view.Players[actor];
        var after = GameEngine.ApplyMove(view, move); var own = after.Players[actor];
        int from = before.Tokens[move.TokenId], to = own.Tokens[move.TokenId];
        int square = GameEngine.TrackIndex(own.Seat, to);
        bool star = after.Rules.SafeStars && GameEngine.IsSafeSquare(square);
        int stack = square < 0 ? 0 : own.Tokens.Count(p => GameEngine.TrackIndex(own.Seat, p) == square);
        var opponents = after.Players.Where((_, p) => p != actor).ToArray();
        double progress = own.Tokens.Sum(p => Math.Max(0, p)) / 224.0;
        double opponentProgress = opponents.Max(p => p.Tokens.Sum(t => Math.Max(0, t))) / 224.0;
        double behind = 0, ahead = 0;
        if (square >= 0)
            foreach (var opponent in opponents)
            foreach (int token in opponent.Tokens)
            {
                int other = GameEngine.TrackIndex(opponent.Seat, token); if (other < 0) continue;
                int gap = (square - other + 52) % 52;
                if (gap is >= 1 and <= 6) behind += (7 - gap) / 6.0;
                gap = (other - square + 52) % 52;
                if (gap is >= 1 and <= 6) ahead += (7 - gap) / 6.0;
            }
        int priorSquare = GameEngine.TrackIndex(before.Seat, from);
        bool split = priorSquare >= 0 && before.Tokens.Count(p => GameEngine.TrackIndex(before.Seat, p) == priorSquare) == 2;
        return [Math.Max(0, from) / 56.0, Math.Max(0, to) / 56.0, from == -1 ? 1 : 0, to == 56 ? 1 : 0,
            after.LastEvent.Captures.Length / 3.0, star ? 1 : 0, stack >= 2 && after.Rules.ProtectStacks ? 1 : 0,
            to >= 51 ? 1 : 0, progress, opponentProgress, own.Tokens.Count(p => p == -1) / 4.0,
            own.Tokens.Count(p => p == 56) / 4.0, opponents.Max(p => p.Tokens.Count(t => t == 56)) / 4.0,
            Math.Min(1, behind / 4), Math.Min(1, ahead / 4), split ? 1 : 0, view.Die / 6.0,
            view.ConsecutiveSixes / 3.0, after.CurrentPlayer == actor ? 1 : 0, view.Players.Length / 4.0,
            own.Status == PlayerStatus.Finished ? 1 : 0, star || stack >= 2 && after.Rules.ProtectStacks || to >= 51 ? 0 : Math.Min(1, behind / 4),
            progress - opponentProgress, (to - Math.Max(0, from)) / 56.0];
    }
}

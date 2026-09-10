using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots;

public readonly record struct MoveScore(Move Move, double Score, string Reason);
public sealed record BotChoice(Move Move, ImmutableArray<MoveScore> Candidates, int Nodes = 0);
public interface IBot
{
    string Id { get; }
    int Version { get; }
    string Label { get; }
    Move SelectMove(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng) => Analyze(view, legalMoves, rng).Move;
    BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng);
}

public sealed record EvaluationWeights(double Progress = 1, double Safety = 6, double Stack = 4,
    double Capture = 18, double Risk = 1, double Home = 65, double Launch = 12);
public sealed record BotSpec(string Key, EvaluationWeights? Weights = null, NeuralModel? Model = null);
public sealed record BotDefinition(string Key, string Id, int Version, string Label, string Changelog, bool BrowserRecommended = true);

public static class BotRegistry
{
    public static ImmutableArray<BotDefinition> All { get; } =
    [
        new("v1", "random", 1, "Random v1", "Uniform random legal move; comparison baseline."),
        new("v2", "heuristic", 2, "Heuristic v2", "Capture, finish, launch, then progress."),
        new("v3", "weighted", 3, "Weighted v3", "Progress, protection, capture value and next-roll exposure."),
        new("v4", "expectimax", 4, "Expectimax v4", "Enumerates the next six dice outcomes using the v3 evaluator."),
        new("v5", "search", 5, "Search v5", "Two-roll max-n expectimax; retained for desktop comparisons.", false),
        new("v6", "compact-search", 6, "Compact search v6", "Two-roll search with a smaller expansion budget for browsers."),
        new("v7", "neural", 7, "Neural v7", "Trainable neural policy with portable C# inference."),
        new("v8", "chatgpt-tactician", 8, "ChatGPT Tactician v8", "Bonus-roll planning, capture exposure and exact private-lane endgames."),
        new("v9", "fable", 9, "Fable v9", "Engine-resolved bonus-roll chains, opponent replies and a probabilistic race model scored as win chances.")
    ];

    public static IBot Create(BotSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Model is not null && spec.Key != "v7") throw new ArgumentException("A neural model requires bot v7.");
        if (spec.Weights is { } w && new[] { w.Progress, w.Safety, w.Stack, w.Capture, w.Risk, w.Home, w.Launch }
            .Any(value => !double.IsFinite(value) || Math.Abs(value) > 1000000))
            throw new ArgumentException("Bot weights must be finite numbers between -1,000,000 and 1,000,000.");
        return spec.Key switch
        {
        "v1" => new Versions.RandomV1(),
        "v2" => new Versions.HeuristicV2(),
        "v3" => new Versions.WeightedV3(spec.Weights ?? Versions.WeightedV3.DefaultWeights),
        "v4" => new Versions.ExpectimaxV4(spec.Weights ?? Versions.WeightedV3.DefaultWeights),
        "v5" => new Versions.SearchV5(spec.Weights ?? Versions.WeightedV3.DefaultWeights),
        "v6" => new Versions.CompactSearchV6(spec.Weights ?? Versions.WeightedV3.DefaultWeights),
        "v7" => new Versions.NeuralV7(spec.Model ?? NeuralNetwork.Bundled),
        "v8" => new Versions.ChatGptV8(spec.Weights),
        "v9" => new Versions.FableV9(),
        _ => throw new ArgumentException($"Unknown bot version '{spec.Key}'.")
        };
    }

    public static string Label(BotSpec spec) => spec.Model is not null ? $"Neural · {spec.Model.Name}" : All.First(b => b.Key == spec.Key).Label + (spec.Weights is null ? "" : " · custom weights");
}

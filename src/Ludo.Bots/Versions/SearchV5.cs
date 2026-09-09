using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class SearchV5(EvaluationWeights weights) : IBot
{
    public string Id => "search";
    public int Version => 5;
    public string Label => "Search v5";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng) =>
        ExpectimaxV4.AnalyzeDepth(view, legalMoves, weights, 2, 768);
}

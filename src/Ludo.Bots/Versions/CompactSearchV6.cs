using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class CompactSearchV6(EvaluationWeights weights) : IBot
{
    public string Id => "compact-search";
    public int Version => 6;
    public string Label => "Compact search v6";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng) =>
        ExpectimaxV4.AnalyzeDepth(view, legalMoves, weights, 2, 72);
}

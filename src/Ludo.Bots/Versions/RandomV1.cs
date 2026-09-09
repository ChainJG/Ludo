using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class RandomV1 : IBot
{
    public string Id => "random";
    public int Version => 1;
    public string Label => "Random v1";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng)
    {
        if (legalMoves.IsEmpty) throw new ArgumentException("A bot requires at least one legal move.");
        return new(legalMoves[rng.NextInt(legalMoves.Length)],
            legalMoves.Select(move => new MoveScore(move, 1.0 / legalMoves.Length, "Uniform random choice")).ToImmutableArray());
    }
}

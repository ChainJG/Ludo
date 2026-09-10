using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class HeuristicV2 : IBot
{
    public string Id => "heuristic";
    public int Version => 2;
    public string Label => "Heuristic v2";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng)
    {
        var candidates = legalMoves.Select(move =>
        {
            var after = GameEngine.ApplyMove(view, move);
            var evt = after.LastEvent;
            double score = evt.Captures.Length * 1000000 + (evt.To == GameEngine.Home ? 100000 : 0)
                + (evt.From == GameEngine.Base ? 10000 : 0) + evt.To;
            string reason = evt.Captures.Length > 0 ? "Capture" : evt.To == GameEngine.Home ? "Finish a token"
                : evt.From == GameEngine.Base ? "Leave base" : "Advance";
            return new MoveScore(move, score, reason);
        }).OrderByDescending(c => c.Score).ThenBy(c => c.Move.TokenId).ToImmutableArray();
        if (candidates.IsEmpty) throw new ArgumentException("A bot requires at least one legal move.");
        return new(candidates[0].Move, candidates);
    }
}

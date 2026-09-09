using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class WeightedV3(EvaluationWeights weights) : IBot
{
    public static EvaluationWeights DefaultWeights { get; } = new(Progress: 1, Safety: 6, Stack: 4, Capture: 18, Risk: 1, Home: 65, Launch: 12);
    public string Id => "weighted";
    public int Version => 3;
    public string Label => "Weighted v3";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng)
    {
        var candidates = legalMoves.Select(move =>
        {
            var after = GameEngine.ApplyMove(view, move);
            double score = Evaluate(after, view.CurrentPlayer, weights, true) + after.LastEvent.Captures.Length * weights.Capture;
            return new MoveScore(move, score, "Progress + protection + captures − next-roll exposure");
        }).OrderByDescending(c => c.Score).ThenBy(c => c.Move.TokenId).ToImmutableArray();
        if (candidates.IsEmpty) throw new ArgumentException("A bot requires at least one legal move.");
        return new(candidates[0].Move, candidates);
    }

    public static double Evaluate(GameStateView view, int playerIndex, EvaluationWeights weights, bool includeRisk)
    {
        int place = view.FinishingOrder.IndexOf(playerIndex);
        if (place == 0) return 100000;
        if (view.Players[playerIndex].Status is PlayerStatus.Disqualified or PlayerStatus.LastRemaining) return -100000;
        if (place >= 0) return 100000 - place * 30000;
        double own = PositionScore(view, playerIndex, weights);
        var rivals = Enumerable.Range(0, view.Players.Length).Where(i => i != playerIndex && view.Players[i].Status == PlayerStatus.Playing).ToArray();
        double opposition = rivals.Length == 0 ? 0 : rivals.Average(i => PositionScore(view, i, weights));
        if (includeRisk && weights.Risk != 0)
        {
            // Enumerate real engine moves, so exposure respects stars, mixed stacks and rule toggles.
            foreach (int opponent in rivals)
            {
                for (int die = 1; die <= 6; die++)
                {
                    var hypothetical = view with { CurrentPlayer = opponent, Phase = GamePhase.AwaitingRoll, Die = 0, ConsecutiveSixes = 0 };
                    hypothetical = GameEngine.ResolveRoll(hypothetical, die);
                    if (hypothetical.Phase != GamePhase.AwaitingMove || hypothetical.CurrentPlayer != opponent) continue;
                    double worstLoss = 0;
                    foreach (var move in GameEngine.GetLegalMoves(hypothetical))
                    {
                        var after = GameEngine.ApplyMove(hypothetical, move);
                        double loss = after.LastEvent.Captures.Where(c => c.PlayerIndex == playerIndex)
                            .Sum(c => 12 + view.Players[playerIndex].Tokens[c.TokenId] * 0.8);
                        worstLoss = Math.Max(worstLoss, loss);
                    }
                    own -= worstLoss * weights.Risk / 6;
                }
            }
        }
        return own - opposition * 0.65;
    }

    private static double PositionScore(GameStateView view, int playerIndex, EvaluationWeights weights)
    {
        var player = view.Players[playerIndex];
        double result = 0;
        foreach (int progress in player.Tokens)
        {
            if (progress == GameEngine.Base) { result -= weights.Launch; continue; }
            result += weights.Progress * (progress * 0.65 + progress * progress * 0.012);
            if (progress == GameEngine.Home) result += weights.Home;
            int square = GameEngine.TrackIndex(player.Seat, progress);
            if (progress >= GameEngine.HomeLaneStart || (view.Rules.SafeStars && GameEngine.IsSafeSquare(square)))
                result += weights.Safety;
            else if (view.Rules.ProtectStacks && player.Tokens.Count(p => p == progress) >= 2)
                result += weights.Stack;
        }
        return result;
    }
}

using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

public sealed class ExpectimaxV4(EvaluationWeights weights) : IBot
{
    public string Id => "expectimax";
    public int Version => 4;
    public string Label => "Expectimax v4";
    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng) =>
        AnalyzeDepth(view, legalMoves, weights, 1, 384);

    public static BotChoice AnalyzeDepth(GameStateView view, ImmutableArray<Move> legalMoves,
        EvaluationWeights weights, int depth, int totalBudget)
    {
        if (legalMoves.IsEmpty) throw new ArgumentException("A bot requires at least one legal move.");
        int nodes = 0;
        var scores = ImmutableArray.CreateBuilder<MoveScore>();
        foreach (var move in legalMoves)
        {
            var after = GameEngine.ApplyMove(view, move);
            int budget = totalBudget / legalMoves.Length;
            int initialBudget = budget;
            var values = Search(after, weights, depth, ref budget);
            double score = values[view.CurrentPlayer] + after.LastEvent.Captures.Length * weights.Capture;
            // Local exposure is evaluated for each candidate, not just the first branch in a budget-limited tree.
            score += 0.25 * (WeightedV3.Evaluate(after, view.CurrentPlayer, weights, true)
                - WeightedV3.Evaluate(after, view.CurrentPlayer, weights, false));
            scores.Add(new(move, score, $"Expected value across {depth} future roll(s)"));
            nodes += initialBudget - budget;
        }
        var candidates = scores.OrderByDescending(s => s.Score).ThenBy(s => s.Move.TokenId).ToImmutableArray();
        return new(candidates[0].Move, candidates, nodes);
    }

    private static double[] Search(GameStateView view, EvaluationWeights weights, int depth, ref int budget)
    {
        double[] Leaf() => Enumerable.Range(0, view.Players.Length).Select(i => WeightedV3.Evaluate(view, i, weights, false)).ToArray();
        if (depth == 0 || budget <= 0 || view.Phase == GamePhase.Finished) return Leaf();
        if (view.Phase == GamePhase.AwaitingRoll)
        {
            var expectation = new double[view.Players.Length];
            for (int die = 1; die <= 6; die++)
            {
                var child = GameEngine.ResolveRoll(view, die);
                int allowance = budget / (7 - die), originalAllowance = allowance;
                var values = Search(child, weights, child.Phase == GamePhase.AwaitingMove ? depth : depth - 1, ref allowance);
                budget -= originalAllowance - allowance;
                for (int player = 0; player < values.Length; player++) expectation[player] += values[player] / 6;
            }
            return expectation;
        }
        double[]? best = null;
        var legal = GameEngine.GetLegalMoves(view);
        if (budget < legal.Length) return Leaf();
        int remaining = legal.Length;
        foreach (var move in legal)
        {
            budget--;
            int allowance = Math.Max(0, (budget - (remaining - 1)) / remaining), originalAllowance = allowance;
            var values = Search(GameEngine.ApplyMove(view, move), weights, depth - 1, ref allowance);
            budget -= originalAllowance - allowance;
            remaining--;
            // Each opponent optimizes its own position; four-player Ludo is not a two-player minimax game.
            if (best is null || values[view.CurrentPlayer] > best[view.CurrentPlayer]) best = values;
        }
        return best ?? Leaf();
    }
}

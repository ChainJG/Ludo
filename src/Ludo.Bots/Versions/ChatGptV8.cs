using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

/// <summary>Bounded, deterministic chance search with turn-aware capture exposure.</summary>
public sealed class ChatGptV8(EvaluationWeights? configuration = null) : IBot
{
    public static EvaluationWeights DefaultWeights { get; } = new(Progress: 1, Safety: 4, Stack: 3,
        Capture: 8, Risk: 1.15, Home: 18, Launch: 7);
    public const int SearchBudget = 384;
    private readonly EvaluationWeights weights = configuration ?? DefaultWeights;
    private static readonly double[] RemainingRolls = BuildRemainingRolls();
    private static readonly double[][] EndgamePasses = [BuildEndgame(false), BuildEndgame(true)];
    public string Id => "chatgpt-tactician";
    public int Version => 8;
    public string Label => "ChatGPT Tactician v8";

    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng)
    {
        if (legalMoves.IsEmpty) throw new ArgumentException("A legal move is required.", nameof(legalMoves));
        int actor = view.CurrentPlayer, nodes = 0;
        var choices = ImmutableArray.CreateBuilder<MoveScore>(legalMoves.Length);
        foreach (var move in legalMoves)
        {
            var after = GameEngine.ApplyMove(view, move);
            int budget = SearchBudget / legalMoves.Length - 1, initial = budget;
            // Every root choice receives the same budget. All six dice outcomes are
            // averaged; future live dice and elapsed time never influence a decision.
            var values = Search(after, 2, 1, ref budget);
            double score = values[actor] + CaptureCredit(view, after, actor);
            nodes += 1 + initial - budget;
            choices.Add(new(move, score, Explain(view, after, move)));
        }
        var ranked = choices.OrderByDescending(c => c.Score).ThenBy(c => c.Move.TokenId).ToImmutableArray();
        return new(ranked[0].Move, ranked, nodes);
    }

    private double[] Search(GameStateView view, int depth, int extensions, ref int budget)
    {
        if (depth <= 0 || budget <= 0 || view.Phase == GamePhase.Finished) return Evaluate(view);
        if (view.Phase == GamePhase.AwaitingRoll)
        {
            var mean = new double[view.Players.Length];
            for (int die = 1; die <= 6; die++)
            {
                var rolled = GameEngine.ResolveRoll(view, die);
                int allowance = budget / (7 - die), original = allowance;
                var value = Search(rolled, rolled.Phase == GamePhase.AwaitingMove ? depth : depth - 1, extensions, ref allowance);
                budget -= original - allowance;
                for (int p = 0; p < mean.Length; p++) mean[p] += value[p] / 6;
            }
            return mean;
        }
        var legal = GameEngine.GetLegalMoves(view);
        if (budget < legal.Length) return Evaluate(view);
        int actor = view.CurrentPlayer;
        var options = new List<(GameStateView State, double[] Values, int Token)>();
        foreach (var move in legal)
        {
            budget--;
            var after = GameEngine.ApplyMove(view, move);
            var values = Evaluate(after);
            values[actor] += CaptureCredit(view, after, actor);
            options.Add((after, values, move.TokenId));
        }
        // Only the most promising replies receive deeper search, but every legal
        // reply is scored first. Opponents optimize their own outcome in four-player games.
        var promising = options.OrderByDescending(o => o.Values[actor]).ThenBy(o => o.Token).Take(2).ToArray();
        double[]? best = null;
        for (int i = 0; i < promising.Length; i++)
        {
            var option = promising[i];
            bool bonus = option.State.CurrentPlayer == actor && option.State.Phase == GamePhase.AwaitingRoll;
            int nextDepth = depth - 1, nextExtensions = extensions;
            if (bonus && extensions > 0) { nextDepth++; nextExtensions--; }
            int allowance = budget / (promising.Length - i), original = allowance;
            var values = nextDepth > 0 && allowance > 0
                ? Search(option.State, nextDepth, nextExtensions, ref allowance) : option.Values;
            if (!ReferenceEquals(values, option.Values)) values[actor] += CaptureCredit(view, option.State, actor);
            budget -= original - allowance;
            if (best is null || values[actor] > best[actor]) best = values;
        }
        return best!;
    }

    private double CaptureCredit(GameStateView before, GameStateView after, int actor)
    {
        double credit = 0;
        foreach (var capture in after.LastEvent.Captures)
        {
            var victim = before.Players[capture.PlayerIndex];
            // Delaying an opponent's final runner can be more valuable than taking
            // a freshly launched pawn. The positional loss is also visible to search.
            int finished = victim.Tokens.Count(p => p == GameEngine.Home);
            credit += weights.Capture * (1 + finished * .25);
        }
        return credit;
    }

    private double[] Evaluate(GameStateView view)
    {
        int count = view.Players.Length;
        var position = new double[count];
        for (int p = 0; p < count; p++) position[p] = Position(view, p);
        var utility = new double[count];
        for (int p = 0; p < count; p++)
        {
            int place = view.FinishingOrder.IndexOf(p);
            if (place >= 0) { utility[p] = 100000 - place * 50000; continue; }
            if (view.Players[p].Status == PlayerStatus.Disqualified) { utility[p] = -100000; continue; }
            double strongest = double.NegativeInfinity, sum = 0; int rivals = 0;
            for (int other = 0; other < count; other++)
            {
                if (other == p || view.Players[other].Status != PlayerStatus.Playing) continue;
                strongest = Math.Max(strongest, position[other]); sum += position[other]; rivals++;
            }
            utility[p] = position[p] - (rivals == 0 ? 0 : .8 * (.65 * strongest + .35 * sum / rivals));
        }
        return utility;
    }

    private double Position(GameStateView view, int index)
    {
        var player = view.Players[index];
        if (player.Status != PlayerStatus.Playing) return 0;
        double score = 0;
        int deployed = 0, unfinished = 0, endgame = 0, multiplier = 1;
        bool allPrivate = true;
        foreach (int progress in player.Tokens)
        {
            if (progress == GameEngine.Base) { score -= weights.Launch; allPrivate = false; continue; }
            if (progress < GameEngine.Home) { deployed++; unfinished++; }
            score += (RemainingRolls[0] - RemainingRolls[progress]) * 4 * weights.Progress;
            if (progress == GameEngine.Home) { score += weights.Home; }
            else if (progress >= GameEngine.HomeLaneStart) score += weights.Safety;
            else
            {
                allPrivate = false;
                int square = GameEngine.TrackIndex(player.Seat, progress);
                bool safe = view.Rules.SafeStars && GameEngine.IsSafeSquare(square);
                int stack = player.Tokens.Count(t => t == progress);
                if (safe) score += weights.Safety;
                else if (stack >= 2 && view.Rules.ProtectStacks) score += weights.Stack;
                else score -= Exposure(view, index, progress, square) * weights.Risk;
            }
            if (progress >= GameEngine.HomeLaneStart) endgame += (GameEngine.Home - progress) * multiplier;
            multiplier *= 6;
        }
        // Opening flexibility has diminishing returns: a six need not always launch
        // another pawn when a developed runner can use its bonus roll more effectively.
        score += deployed switch { 1 => 10, 2 => 15, 3 => 17, 4 => 18, _ => 0 };
        if (allPrivate && unfinished > 0)
            score += (6 * unfinished - EndgamePasses[view.Rules.ExtraRollOnHome ? 1 : 0][endgame] - 1) * 4;
        return score;
    }

    private static double Exposure(GameStateView view, int index, int progress, int square)
    {
        double survival = 1;
        for (int other = 0; other < view.Players.Length; other++)
        {
            if (other == index || view.Players[other].Status != PlayerStatus.Playing) continue;
            var opponent = view.Players[other];
            int threatenedDice = 0;
            foreach (int enemy in opponent.Tokens)
            {
                if (enemy == GameEngine.Base)
                {
                    if (GameEngine.TrackIndex(opponent.Seat, 0) == square)
                        threatenedDice |= view.Rules.LaunchRequiresSix ? 1 << 6 : 126;
                    continue;
                }
                if (enemy >= GameEngine.HomeLaneStart) continue;
                int gap = (square - GameEngine.TrackIndex(opponent.Seat, enemy) + 52) % 52;
                // gap==0 is co-location, not a new arrival and therefore not an attack.
                if (gap is < 1 or > 6 || enemy + gap >= GameEngine.HomeLaneStart) continue;
                if (view.Rules.StacksBlockMovement && IsBlocked(view, other, enemy, gap)) continue;
                if (gap == 6 && view.Rules.ForfeitThirdSix && view.CurrentPlayer == other && view.ConsecutiveSixes >= 2) continue;
                threatenedDice |= 1 << gap;
            }
            int possibilities = System.Numerics.BitOperations.PopCount((uint)threatenedDice);
            int ownOrder = TurnDistance(view, index), enemyOrder = TurnDistance(view, other);
            // A player receiving the next roll has a chance to escape. Explicit search
            // resolves the first replies; this discounts threats beyond its horizon.
            double opportunity = ownOrder < enemyOrder ? .35 : 1;
            survival *= 1 - possibilities / 6.0 * opportunity;
        }
        return (1 - survival) * (18 + progress * 1.35);
    }

    private static int TurnDistance(GameStateView view, int player) => (player - view.CurrentPlayer + view.Players.Length) % view.Players.Length;
    private static bool IsBlocked(GameStateView view, int player, int from, int distance)
    {
        for (int step = 1; step <= distance; step++)
        {
            int square = GameEngine.TrackIndex(view.Players[player].Seat, from + step);
            for (int other = 0; other < view.Players.Length; other++)
                if (other != player && view.Players[other].Tokens.Count(t => GameEngine.TrackIndex(view.Players[other].Seat, t) == square) >= 2) return true;
        }
        return false;
    }

    private static string Explain(GameStateView before, GameStateView after, Move move)
    {
        var evt = after.LastEvent;
        var reasons = new List<string>();
        if (!evt.Captures.IsEmpty) reasons.Add($"capture {evt.Captures.Length}; deny opponent progress");
        if (evt.To == GameEngine.Home) reasons.Add("exact finish");
        if (evt.From == GameEngine.Base) reasons.Add("deploy another runner");
        int square = GameEngine.TrackIndex(before.Players[before.CurrentPlayer].Seat, evt.From);
        bool shared = square >= 0 && before.Players.Where((_, p) => p != before.CurrentPlayer)
            .Any(p => p.Tokens.Any(t => GameEngine.TrackIndex(p.Seat, t) == square));
        if (shared) reasons.Add("leave shared square; assess new-arrival threats");
        if (after.CurrentPlayer == before.CurrentPlayer && after.Phase == GamePhase.AwaitingRoll)
            reasons.Add("plan bonus roll, including third-six risk");
        if (reasons.Count == 0) reasons.Add("balance racing, protection and capture exposure");
        return string.Join(" · ", reasons);
    }

    private static double[] BuildRemainingRolls()
    {
        var values = new double[57];
        for (int p = 55; p >= 0; p--)
        {
            int legal = Math.Min(6, 56 - p); double sum = 6;
            for (int die = 1; die <= legal; die++) sum += values[p + die];
            values[p] = sum / legal;
        }
        return values;
    }

    // Exact dynamic program for the 6^4 private-lane states: expected times control
    // passes before all four finish, choosing the best pawn for each possible die.
    private static double[] BuildEndgame(bool homeBonus)
    {
        var values = new double[1296];
        for (int state = 1; state < values.Length; state++)
        {
            double sum = 0; int legal = 0;
            for (int die = 1; die <= 6; die++)
            {
                double best = double.PositiveInfinity;
                for (int t = 0, factor = 1; t < 4; t++, factor *= 6)
                {
                    int remaining = state / factor % 6;
                    if (remaining < die) continue;
                    int child = state - die * factor;
                    double pass = child == 0 || (homeBonus && remaining == die) ? 0 : 1;
                    best = Math.Min(best, values[child] + pass);
                }
                if (double.IsFinite(best)) { sum += best; legal++; } else sum++;
            }
            values[state] = sum / legal;
        }
        return values;
    }
}

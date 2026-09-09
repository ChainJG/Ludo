using System.Collections.Immutable;
using System.Numerics;
using Ludo.Engine;

namespace Ludo.Bots.Versions;

// All quantities are in pips (track steps). Temperature converts pip differences into win chances.
public sealed record FableParameters(double LaunchPenalty = 8, double HomeBonus = 4, double LaneBonus = 2, double StarBonus = 1.5,
    double RiskWeight = 1, double CaptureTempo = 3.5, double MoverDiscount = 0.5, double FutureWeight = 3,
    double MobilityWeight = 3, double CampWeight = 2, double SniperWeight = 0.5, double Temperature = 25,
    double PinWeight = 0, double DeployBonus = 0, double TempoBonus = 0);

// Fable v9. Max-n expectimax through the shared engine, so every hypothetical roll and move (bonus rolls
// after a six, capture or finish, third-six forfeits and every rule toggle) is resolved by the rules
// themselves. A six is therefore never "stop six squares ahead": the search plays the bonus roll too.
// Leaves use a probabilistic race model: each player's material in pips minus the expected loss from every
// capture an opponent could make before that player moves again (direct rolls and six-then-roll chains),
// converted into win chances with a softmax. Win chances make the search risk-averse when ahead and
// risk-seeking when behind, which a linear material score cannot express.
public sealed class FableV9(FableParameters? parameters = null, int turns = 1, int budget = 2000) : IBot
{
    private const int Track = GameEngine.HomeLaneStart - 1; // 50: last shared-track step
    private readonly FableParameters p = parameters ?? new();
    public string Id => "fable";
    public int Version => 9;
    public string Label => "Fable v9";

    public BotChoice Analyze(GameStateView view, ImmutableArray<Move> legalMoves, IRng rng)
    {
        if (legalMoves.IsEmpty) throw new ArgumentException("A bot requires at least one legal move.");
        int me = view.CurrentPlayer, nodes = 0;
        var scores = ImmutableArray.CreateBuilder<MoveScore>(legalMoves.Length);
        foreach (var move in legalMoves)
        {
            var after = GameEngine.ApplyMove(view, move);
            int allowance = budget / legalMoves.Length, initial = allowance;
            double value = Continue(after, me, turns + 1, ref allowance)[me];
            nodes += initial - allowance + 1;
            var evt = after.LastEvent;
            string what = evt.Captures.Length > 0 ? "Capture" : evt.To == GameEngine.Home ? "Finish" : evt.From == GameEngine.Base ? "Launch" : "Advance";
            scores.Add(new(move, value, $"{what} · win estimate {value:P1} after the bonus-roll chain and {turns} opponent turn(s)"));
        }
        var candidates = scores.OrderByDescending(s => s.Score).ThenBy(s => s.Move.TokenId).ToImmutableArray();
        return new(candidates[0].Move, candidates, nodes);
    }

    // After a move: the same player rolls again (bonus) inside the same turn; otherwise a new turn begins.
    private double[] Continue(GameStateView state, int mover, int depth, ref int budget)
    {
        if (state.Phase == GamePhase.Finished) return Terminal(state);
        return Turn(state, state.CurrentPlayer == mover ? depth : depth - 1, ref budget);
    }

    // Every die outcome receives an equal share of the remaining budget, so all branches of one decision are
    // cut at a comparable depth instead of the first branches consuming everything.
    private double[] Turn(GameStateView state, int depth, ref int budget)
    {
        if (depth == 0 || budget < 6) return Leaf(state);
        var expectation = new double[state.Players.Length];
        for (int die = 1; die <= 6; die++)
        {
            int allowance = budget / (7 - die) - 1, original = allowance;
            budget--;
            var rolled = GameEngine.ResolveRoll(state, die);
            var values = rolled.Phase == GamePhase.AwaitingMove ? Choose(rolled, depth, ref allowance)
                : rolled.Phase == GamePhase.Finished ? Terminal(rolled) : Turn(rolled, depth - 1, ref allowance);
            budget -= original - allowance;
            for (int i = 0; i < values.Length; i++) expectation[i] += values[i] / 6;
        }
        return expectation;
    }

    // Each player maximizes their own value: four-player Ludo is not a two-sided minimax game.
    private double[] Choose(GameStateView state, int depth, ref int budget)
    {
        int mover = state.CurrentPlayer;
        var legal = GameEngine.GetLegalMoves(state);
        if (budget < legal.Length) return Leaf(state);
        int remaining = legal.Length;
        double[]? best = null;
        foreach (var move in legal)
        {
            budget--;
            int allowance = Math.Max(0, (budget - (remaining - 1)) / remaining), original = allowance;
            var values = Continue(GameEngine.ApplyMove(state, move), mover, depth, ref allowance);
            budget -= original - allowance;
            remaining--;
            if (best is null || values[mover] > best[mover]) best = values;
        }
        return best!;
    }

    private static double PlaceScore(int place, int players) => (players - 1 - place) / (double)(players - 1);

    private static double[] Terminal(GameStateView state)
    {
        var values = new double[state.Players.Length];
        for (int place = 0; place < state.FinishingOrder.Length; place++)
            values[state.FinishingOrder[place]] = state.Players[state.FinishingOrder[place]].Status == PlayerStatus.Disqualified ? 0 : PlaceScore(place, values.Length);
        return values;
    }

    private double[] Leaf(GameStateView state)
    {
        int n = state.Players.Length, placed = state.FinishingOrder.Length, active = 0;
        var strength = new double[n];
        var share = new double[n];
        var values = new double[n];
        double best = double.NegativeInfinity, mean = 0, sum = 0;
        for (int i = 0; i < n; i++)
        {
            if (state.Players[i].Status != PlayerStatus.Playing) continue;
            strength[i] = Evaluate(state, i, p);
            best = Math.Max(best, strength[i]);
            mean += strength[i]; active++;
        }
        mean /= Math.Max(1, active);
        for (int i = 0; i < n; i++)
            if (state.Players[i].Status == PlayerStatus.Playing) sum += share[i] = Math.Exp((strength[i] - best) / p.Temperature);
        // The next finisher takes the next place; the others are expected to share the remaining places.
        double nextPrize = PlaceScore(placed, n), laterPrize = 0;
        for (int place = placed + 1; place < n; place++) laterPrize += PlaceScore(place, n) / (n - placed - 1);
        for (int i = 0; i < n; i++)
        {
            var player = state.Players[i];
            if (player.Status == PlayerStatus.Playing)
            {
                double chance = share[i] / sum;
                // A tiny linear term keeps material preferences once win chances saturate.
                values[i] = chance * nextPrize + (1 - chance) * laterPrize + 1e-5 * (strength[i] - mean);
            }
            else if (player.Status == PlayerStatus.Finished) values[i] = PlaceScore(state.FinishingOrder.IndexOf(i), n);
        }
        return values;
    }

    // Race strength of one player in pips, seen from a state where state.CurrentPlayer is about to roll.
    public static double Evaluate(GameStateView state, int index, FableParameters? parameters = null)
    {
        var p = parameters ?? new();
        var rules = state.Rules;
        var player = state.Players[index];
        int seat = player.Seat, mover = state.CurrentPlayer, enemyActive = 0, opponents = 0;
        for (int o = 0; o < state.Players.Length; o++)
        {
            if (o == index || state.Players[o].Status != PlayerStatus.Playing) continue;
            opponents++;
            foreach (int r in state.Players[o].Tokens) if (r < GameEngine.HomeLaneStart) enemyActive++;
        }
        double total = 0;
        foreach (int q in player.Tokens)
        {
            if (q == GameEngine.Base) { total -= p.LaunchPenalty; continue; }
            if (q == GameEngine.Home) { total += q + p.HomeBonus; continue; }
            total += q;
            if (q >= GameEngine.HomeLaneStart) { total += p.LaneBonus; continue; }
            int square = GameEngine.TrackIndex(seat, q);
            // Every enemy token still on the shared track will have to be passed or outrun.
            if (opponents > 0) total -= p.FutureWeight * (Track - q) / (double)Track * enemyActive / (4.0 * opponents);
            bool star = rules.SafeStars && GameEngine.IsSafeSquare(square);
            bool stacked = rules.ProtectStacks && player.Tokens.Count(t => t == q) >= 2;
            if (star) total += p.StarBonus;
            if (star || stacked)
            {
                double unpinned = 1;
                for (int o = 0; o < state.Players.Length; o++)
                {
                    if (o == index || state.Players[o].Status != PlayerStatus.Playing) continue;
                    var enemy = state.Players[o];
                    int entry = GameEngine.TrackIndex(enemy.Seat, 0);
                    // Spawn camping: every token the enemy launches must step off its entry into this token's range.
                    if (star && square == entry) total += p.CampWeight * enemy.Tokens.Count(t => t == GameEngine.Base);
                    foreach (int r in enemy.Tokens)
                    {
                        if (r >= GameEngine.HomeLaneStart) continue;
                        int from = r == GameEngine.Base ? entry : GameEngine.TrackIndex(enemy.Seat, r);
                        int gap = (square - from + GameEngine.TrackLength) % GameEngine.TrackLength;
                        // Sniper park: traffic behind a safe token must pass in front of it.
                        if (gap >= 1 && gap <= 18 && Math.Max(0, r) + gap <= Track)
                            total += p.SniperWeight * (Math.Max(0, r) + p.LaunchPenalty) / 6;
                        // Pinned: an enemy sharing this square or sitting just behind it covers every square this
                        // token can step to. Leaving later with an ordinary roll will cost a shot; a six will not.
                        if (r != GameEngine.Base && gap <= 5 && r + gap + 1 <= Track) unpinned *= 5.0 / 6;
                    }
                }
                total -= p.PinWeight * (1 - unpinned) * (q + p.LaunchPenalty + p.CaptureTempo);
                continue;
            }
            double survive = 1;
            for (int o = 0; o < state.Players.Length; o++)
            {
                if (o == index || state.Players[o].Status != PlayerStatus.Playing) continue;
                var (direct, chained) = Reach(rules, state.Players[o], square);
                double chance = BitOperations.PopCount((uint)direct) / 6.0
                    + ((direct & (1 << 6)) == 0 ? BitOperations.PopCount((uint)chained) / 36.0 : 0);
                survive *= 1 - chance;
            }
            double capture = 1 - survive;
            if (index == mover) capture *= p.MoverDiscount; // the mover can still react first
            total -= capture * (q + p.LaunchPenalty + p.CaptureTempo) * p.RiskWeight;
        }
        int movable = 0;
        for (int die = 1; die <= 6; die++)
            foreach (int q in player.Tokens)
                if (q == GameEngine.Base ? die == 6 || !rules.LaunchRequiresSix : q != GameEngine.Home && q + die <= GameEngine.Home) { movable++; break; }
        // Runners on the board give choices; the first ones matter most.
        int runners = player.Tokens.Count(t => t != GameEngine.Base && t != GameEngine.Home);
        total += p.DeployBonus * (runners switch { 0 => 0, 1 => 1, 2 => 1.5, 3 => 1.7, _ => 1.8 });
        // Having the move is worth roughly one roll; this keeps leaves cut before and after a turn comparable.
        if (index == mover) total += p.TempoBonus;
        return total + p.MobilityWeight * movable / 6.0;
    }

    // Die values (bits 1-6) with which this opponent could land on the square next turn: directly,
    // or after a six and its bonus roll (including launching with the six and stepping off the entry).
    private static (int Direct, int Chained) Reach(RuleConfig rules, PlayerState opponent, int square)
    {
        int direct = 0, chained = 0, seat = opponent.Seat;
        foreach (int r in opponent.Tokens)
        {
            if (r == GameEngine.Base)
            {
                int entry = GameEngine.TrackIndex(seat, 0);
                if (entry == square) direct |= rules.LaunchRequiresSix ? 1 << 6 : 0b1111110;
                else if (rules.ExtraRollOnSix)
                {
                    int gap = (square - entry + GameEngine.TrackLength) % GameEngine.TrackLength;
                    if (gap is >= 1 and <= 6) chained |= 1 << gap;
                }
            }
            else if (r < GameEngine.HomeLaneStart)
            {
                int gap = (square - GameEngine.TrackIndex(seat, r) + GameEngine.TrackLength) % GameEngine.TrackLength;
                if (gap == 0 || r + gap > Track) continue; // same square, or beyond this token's exit into its home lane
                if (gap <= 6) direct |= 1 << gap;
                else if (gap <= 12 && rules.ExtraRollOnSix) chained |= 1 << (gap - 6);
            }
        }
        return (direct, chained);
    }
}

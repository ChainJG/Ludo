using System.Collections.Immutable;
using System.Text.Json;

namespace Ludo.Engine;

public static class GameEngine
{
    public const string Version = "1.0.0";
    public const int Base = -1;
    public const int Home = 56;
    public const int HomeLaneStart = 51;
    public const int TrackLength = 52;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { TypeInfoResolver = EngineJsonContext.Default };

    public static GameState CreateGame(RuleConfig config, uint seed)
    {
        config.Validate();
        var seats = config.PlayerCount == 2 ? new[] { 0, 2 } : [0, 1, 2, 3];
        var players = seats.Select(seat => new PlayerState(seat, [-1, -1, -1, -1])).ToImmutableArray();
        return new(new(config, players, 0, GamePhase.AwaitingRoll, 0, 0, 1, 0, [], [],
            new(EventKind.Started, 0)), seed);
    }

    public static int TrackIndex(int seat, int progress) => progress is >= 0 and < HomeLaneStart
        ? (seat * 13 + progress) % TrackLength : -1;

    public static bool IsSafeSquare(int index) => index >= 0 && index % 13 is 0 or 8;

    public static ImmutableArray<Move> GetLegalMoves(GameState state) => GetLegalMoves(state.View);

    public static ImmutableArray<Move> GetLegalMoves(GameStateView state)
    {
        if (state.Phase != GamePhase.AwaitingMove) return [];
        var result = ImmutableArray.CreateBuilder<Move>(4);
        var player = state.Players[state.CurrentPlayer];
        for (int token = 0; token < 4; token++)
        {
            int from = player.Tokens[token];
            if (from == Home || (from == Base && state.Rules.LaunchRequiresSix && state.Die != 6)) continue;
            int to = from == Base ? 0 : from + state.Die;
            if (to > Home) continue;
            if (state.Rules.StacksBlockMovement)
            {
                bool blocked = false;
                for (int step = Math.Max(0, from + 1); step <= to && step < HomeLaneStart; step++)
                {
                    int square = TrackIndex(player.Seat, step);
                    for (int opponent = 0; opponent < state.Players.Length; opponent++)
                    {
                        if (opponent == state.CurrentPlayer) continue;
                        if (CountOnSquare(state.Players[opponent], square) >= 2) { blocked = true; break; }
                    }
                    if (blocked) break;
                }
                if (blocked) continue;
            }
            result.Add(new(token));
        }
        return result.ToImmutable();
    }

    public static GameState Roll(GameState state)
    {
        RequirePhase(state.View, GamePhase.AwaitingRoll);
        var rng = new SeededRng(state.DiceState);
        int die = rng.NextInt(6) + 1;
        return new(ResolveRoll(state.View, die), rng.State);
    }

    // Search and rule tests supply outcomes explicitly. This cannot reveal or consume future live dice.
    public static GameStateView ResolveRoll(GameStateView state, int die)
    {
        RequirePhase(state, GamePhase.AwaitingRoll);
        if (die is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(die));
        int sixes = die == 6 ? state.ConsecutiveSixes + 1 : 0;
        state = state with
        {
            Die = die, ConsecutiveSixes = sixes, RollNumber = state.RollNumber + 1,
            Phase = GamePhase.AwaitingMove,
            LastEvent = new(EventKind.Rolled, state.CurrentPlayer, die)
        };
        if (sixes >= 3 && state.Rules.ForfeitThirdSix)
            return NextTurn(state with { LastEvent = new(EventKind.ThirdSix, state.CurrentPlayer, die) });
        if (GetLegalMoves(state).IsEmpty)
            return NextTurn(state with { LastEvent = new(EventKind.NoLegalMove, state.CurrentPlayer, die) });
        return state;
    }

    public static GameState ApplyMove(GameState state, Move move) => state with { View = ApplyMove(state.View, move) };

    public static GameStateView ApplyMove(GameStateView state, Move move)
    {
        if (!GetLegalMoves(state).Contains(move)) throw new ArgumentException("The move is not legal.", nameof(move));
        int current = state.CurrentPlayer;
        var player = state.Players[current];
        int from = player.Tokens[move.TokenId];
        int to = from == Base ? 0 : from + state.Die;
        var players = state.Players.SetItem(current, player with { Tokens = player.Tokens.SetItem(move.TokenId, to) });
        var captured = ImmutableArray.CreateBuilder<CapturedToken>();
        int square = TrackIndex(player.Seat, to);
        if (square >= 0 && !(state.Rules.SafeStars && IsSafeSquare(square)))
        {
            for (int opponent = 0; opponent < players.Length; opponent++)
            {
                if (opponent == current) continue;
                int count = CountOnSquare(players[opponent], square);
                if (count == 0 || (count >= 2 && state.Rules.ProtectStacks)) continue;
                var tokens = players[opponent].Tokens;
                for (int token = 0; token < 4; token++)
                {
                    if (TrackIndex(players[opponent].Seat, tokens[token]) != square) continue;
                    tokens = tokens.SetItem(token, Base);
                    captured.Add(new(opponent, token));
                }
                players = players.SetItem(opponent, players[opponent] with { Tokens = tokens });
            }
        }
        var bonus = BonusReason.None;
        if (state.Die == 6 && state.Rules.ExtraRollOnSix) bonus |= BonusReason.Six;
        if (captured.Count > 0 && state.Rules.ExtraRollOnCapture) bonus |= BonusReason.Capture;
        if (to == Home && state.Rules.ExtraRollOnHome) bonus |= BonusReason.Home;
        state = state with { Players = players, LastEvent = new(EventKind.Moved, current, state.Die,
            move.TokenId, from, to, captured.ToImmutable(), bonus) };
        if (players[current].Tokens.All(position => position == Home))
        {
            state = state with
            {
                Players = players.SetItem(current, players[current] with { Status = PlayerStatus.Finished }),
                FinishingOrder = state.FinishingOrder.Add(current),
                LastEvent = state.LastEvent with { Bonus = BonusReason.None }
            };
            state = FinishIfDecided(state);
            return state.Phase == GamePhase.Finished ? state : NextTurn(state);
        }
        return bonus != BonusReason.None ? state with { Phase = GamePhase.AwaitingRoll, Die = 0 } : NextTurn(state);
    }

    public static GameState Disqualify(GameState state, int playerIndex)
    {
        if (IsTerminal(state) || playerIndex < 0 || playerIndex >= state.View.Players.Length ||
            state.View.Players[playerIndex].Status != PlayerStatus.Playing)
            throw new ArgumentException("Only an active player can be disqualified.", nameof(playerIndex));
        var player = state.View.Players[playerIndex];
        var view = state.View with
        {
            Players = state.View.Players.SetItem(playerIndex, player with { Tokens = [-1, -1, -1, -1], Status = PlayerStatus.Disqualified }),
            DisqualificationOrder = state.View.DisqualificationOrder.Add(playerIndex),
            LastEvent = new(EventKind.Disqualified, playerIndex)
        };
        view = FinishIfDecided(view);
        if (view.Phase != GamePhase.Finished && playerIndex == view.CurrentPlayer) view = NextTurn(view);
        return state with { View = view };
    }

    public static bool IsTerminal(GameState state) => state.View.Phase == GamePhase.Finished;
    public static GameResult GetResult(GameState state)
    {
        if (!IsTerminal(state)) throw new InvalidOperationException("The game is still running.");
        return new(state.View.FinishingOrder, state.View.DisqualificationOrder, state.View.TurnNumber, state.View.RollNumber);
    }

    public static string Serialize(GameState state) => JsonSerializer.Serialize(state, JsonOptions);
    public static GameState Deserialize(string json)
    {
        var state = JsonSerializer.Deserialize<GameState>(json, JsonOptions) ?? throw new FormatException("Missing game state.");
        Validate(state);
        return state;
    }

    public static void Validate(GameState state)
    {
        var view = state.View;
        if (view is null || view.Rules is null || view.LastEvent is null) throw new FormatException("Missing game data.");
        view.Rules.Validate();
        if (view.Players.IsDefault || view.Players.Length != view.Rules.PlayerCount ||
            view.CurrentPlayer < 0 || view.CurrentPlayer >= view.Players.Length ||
            !Enum.IsDefined(view.Phase) || view.TurnNumber < 1 || view.RollNumber < 0 ||
            view.ConsecutiveSixes < 0 || view.Die is < 0 or > 6 ||
            view.FinishingOrder.IsDefault || view.DisqualificationOrder.IsDefault)
            throw new FormatException("Invalid game state.");
        if (view.Players.Select(p => p.Seat).Distinct().Count() != view.Players.Length ||
            view.Players.Any(p => p.Seat is < 0 or > 3 || p.Tokens.IsDefault || p.Tokens.Length != 4 ||
                p.Tokens.Any(t => t is < Base or > Home) || !Enum.IsDefined(p.Status)))
            throw new FormatException("Invalid player state.");
        if (view.FinishingOrder.Concat(view.DisqualificationOrder).Any(p => p < 0 || p >= view.Players.Length) ||
            view.FinishingOrder.Distinct().Count() != view.FinishingOrder.Length ||
            view.DisqualificationOrder.Distinct().Count() != view.DisqualificationOrder.Length)
            throw new FormatException("Invalid finishing order.");
        if (view.Phase == GamePhase.AwaitingMove && (view.Die == 0 || GetLegalMoves(view).IsEmpty))
            throw new FormatException("Invalid pending move.");
        if (view.Phase != GamePhase.Finished && view.Players[view.CurrentPlayer].Status != PlayerStatus.Playing)
            throw new FormatException("The current player is inactive.");
        if (view.Phase == GamePhase.Finished && view.FinishingOrder.Length != view.Players.Length)
            throw new FormatException("A finished game must rank every player.");
        for (int i = 0; i < view.Players.Length; i++)
        {
            var player = view.Players[i];
            if (player.Seat != (view.Players.Length == 2 ? i * 2 : i)) throw new FormatException("Invalid board seat assignment.");
            if (player.Status == PlayerStatus.Finished && player.Tokens.Any(t => t != Home)) throw new FormatException("Finished player has unfinished tokens.");
            if (player.Status == PlayerStatus.Playing && player.Tokens.All(t => t == Home)) throw new FormatException("Finished player is still active.");
            if (player.Status == PlayerStatus.Disqualified && (player.Tokens.Any(t => t != Base) || !view.DisqualificationOrder.Contains(i)))
                throw new FormatException("Invalid disqualification state.");
            if (player.Status != PlayerStatus.Disqualified && view.DisqualificationOrder.Contains(i)) throw new FormatException("Invalid disqualification order.");
            if (view.Phase != GamePhase.Finished && view.FinishingOrder.Contains(i) != (player.Status == PlayerStatus.Finished))
                throw new FormatException("Invalid provisional finishing order.");
            if (player.Status == PlayerStatus.LastRemaining && view.Phase != GamePhase.Finished) throw new FormatException("Game should be finished.");
        }
        if (view.Phase == GamePhase.Finished && (view.Players.Any(p => p.Status == PlayerStatus.Playing) || view.Players.Count(p => p.Status == PlayerStatus.LastRemaining) > 1))
            throw new FormatException("Invalid final player statuses.");
        if (view.Phase != GamePhase.AwaitingMove && view.Die != 0) throw new FormatException("No move is pending for this die.");
    }

    private static int CountOnSquare(PlayerState player, int square) =>
        player.Tokens.Count(position => TrackIndex(player.Seat, position) == square);

    private static GameStateView NextTurn(GameStateView state)
    {
        int next = state.CurrentPlayer;
        do { next = (next + 1) % state.Players.Length; }
        while (state.Players[next].Status != PlayerStatus.Playing);
        return state with { CurrentPlayer = next, Phase = GamePhase.AwaitingRoll, Die = 0,
            ConsecutiveSixes = 0, TurnNumber = state.TurnNumber + 1 };
    }

    private static GameStateView FinishIfDecided(GameStateView state)
    {
        var active = Enumerable.Range(0, state.Players.Length).Where(i => state.Players[i].Status == PlayerStatus.Playing).ToArray();
        if (active.Length > 1) return state;
        if (active.Length == 1)
        {
            int last = active[0];
            state = state with { Players = state.Players.SetItem(last, state.Players[last] with { Status = PlayerStatus.LastRemaining }),
                FinishingOrder = state.FinishingOrder.Add(last) };
        }
        // Earlier disqualifications rank below later ones. Incidents remain distinguishable from normal finishes.
        return state with { Phase = GamePhase.Finished, Die = 0,
            FinishingOrder = state.FinishingOrder.AddRange(state.DisqualificationOrder.Reverse()) };
    }

    private static void RequirePhase(GameStateView state, GamePhase phase)
    {
        if (state.Phase != phase) throw new InvalidOperationException($"Expected {phase}, found {state.Phase}.");
    }
}

using System.Collections.Immutable;
using System.Diagnostics;
using Ludo.Bots;
using Ludo.Engine;

namespace Ludo.Runner;

public sealed record BotRequest(BotSpec Bot, GameStateView View, uint Seed);
public sealed record BotExecution(BotChoice? Choice, double ElapsedMilliseconds, string? Error = null);
public interface IBotExecutor : IAsyncDisposable
{
    ValueTask<BotExecution> ExecuteAsync(BotRequest request, double budgetMilliseconds, CancellationToken cancellationToken);
}

// Explicitly for trusted, bounded built-in bots and throughput experiments. Production hosts use isolation.
public sealed class InlineBotExecutor : IBotExecutor
{
    public ValueTask<BotExecution> ExecuteAsync(BotRequest request, double budgetMilliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var bot = BotRegistry.Create(request.Bot);
            var rng = new SeededRng(request.Seed);
            var legal = GameEngine.GetLegalMoves(request.View);
            var watch = Stopwatch.StartNew();
            var choice = bot.Analyze(request.View, legal, rng);
            return ValueTask.FromResult(new BotExecution(choice, watch.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception) { return ValueTask.FromResult(new BotExecution(null, 0, exception.Message)); }
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record MatchIncident(int Player, int Roll, string Reason);
public sealed record PlayerMetrics(int Moves, int Captures, int Lost, double TotalMoveMilliseconds);
public sealed record MatchRecord(string EngineVersion, uint Seed, RuleConfig Rules, ImmutableArray<BotSpec?> Seats,
    ImmutableArray<RecordedAction> Actions, ImmutableArray<MatchIncident> Incidents,
    ImmutableArray<PlayerMetrics> Metrics, GameResult? Result, bool Truncated, double BudgetMilliseconds)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public GameRecording Recording => new(EngineVersion, Rules, Seed, Actions);
}

public sealed class MatchSession
{
    private readonly List<RecordedAction> actions = [];
    private readonly List<MatchIncident> incidents = [];
    private readonly PlayerMetrics[] metrics;
    public uint Seed { get; }
    public ImmutableArray<BotSpec?> Seats { get; }
    public double BudgetMilliseconds { get; }
    public GameState State { get; private set; }
    public BotChoice? LastChoice { get; private set; }
    public IReadOnlyList<RecordedAction> Actions => actions.AsReadOnly();
    public IReadOnlyList<MatchIncident> Incidents => incidents.AsReadOnly();

    public MatchSession(RuleConfig rules, uint seed, ImmutableArray<BotSpec?> seats, double budgetMilliseconds = 50)
    {
        if (seats.Length != rules.PlayerCount) throw new ArgumentException("Each player needs a seat assignment.");
        if (!double.IsFinite(budgetMilliseconds) || budgetMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
        foreach (var bot in seats) if (bot is not null) _ = BotRegistry.Create(bot);
        Seed = seed;
        Seats = seats;
        BudgetMilliseconds = budgetMilliseconds;
        State = GameEngine.CreateGame(rules, seed);
        metrics = Enumerable.Range(0, seats.Length).Select(_ => new PlayerMetrics(0, 0, 0, 0)).ToArray();
    }

    public void Roll()
    {
        State = GameEngine.Roll(State);
        actions.Add(new(ActionKind.Roll, State.View.LastEvent.Die));
        LastChoice = null;
    }

    public void Move(Move move)
    {
        int player = State.View.CurrentPlayer;
        State = GameEngine.ApplyMove(State, move);
        actions.Add(new(ActionKind.Move, move.TokenId));
        var captures = State.View.LastEvent.Captures;
        metrics[player] = metrics[player] with { Moves = metrics[player].Moves + 1, Captures = metrics[player].Captures + captures.Length };
        foreach (var capture in captures)
            metrics[capture.PlayerIndex] = metrics[capture.PlayerIndex] with { Lost = metrics[capture.PlayerIndex].Lost + 1 };
    }

    public async ValueTask AdvanceAsync(IBotExecutor executor, CancellationToken cancellationToken = default)
    {
        if (GameEngine.IsTerminal(State)) return;
        if (State.View.Phase == GamePhase.AwaitingRoll) { Roll(); return; }
        int player = State.View.CurrentPlayer;
        var bot = Seats[player] ?? throw new InvalidOperationException("This seat is controlled by a human.");
        var request = new BotRequest(bot, State.View,
            SeededRng.Derive(SeededRng.Derive(Seed, (uint)player + 1), (uint)State.View.RollNumber));
        BotExecution execution;
        try { execution = await executor.ExecuteAsync(request, BudgetMilliseconds, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { execution = new(null, 0, exception.Message); }
        cancellationToken.ThrowIfCancellationRequested();
        string? error = execution.Error;
        if (error is null && (!double.IsFinite(execution.ElapsedMilliseconds) || execution.ElapsedMilliseconds < 0)) error = "Invalid bot timing result.";
        if (error is null && execution.ElapsedMilliseconds > BudgetMilliseconds) error = $"Time budget exceeded ({execution.ElapsedMilliseconds:F2} ms > {BudgetMilliseconds:F2} ms).";
        if (error is null && (execution.Choice is null || !GameEngine.GetLegalMoves(State).Contains(execution.Choice.Move))) error = "Bot returned an illegal move.";
        if (error is not null)
        {
            incidents.Add(new(player, State.View.RollNumber, error));
            actions.Add(new(ActionKind.Disqualify, player));
            State = GameEngine.Disqualify(State, player);
            LastChoice = null;
            return;
        }
        metrics[player] = metrics[player] with { TotalMoveMilliseconds = metrics[player].TotalMoveMilliseconds + execution.ElapsedMilliseconds };
        LastChoice = execution.Choice;
        Move(execution.Choice!.Move);
    }

    public MatchRecord Snapshot(bool truncated = false) => new(GameEngine.Version, Seed, State.View.Rules, Seats,
        actions.ToImmutableArray(), incidents.ToImmutableArray(), metrics.ToImmutableArray(),
        GameEngine.IsTerminal(State) ? GameEngine.GetResult(State) : null, truncated, BudgetMilliseconds);

    public static MatchSession Restore(MatchRecord record)
    {
        var state = record.Recording.Replay();
        var session = new MatchSession(record.Rules, record.Seed, record.Seats, record.BudgetMilliseconds) { State = state };
        session.actions.AddRange(record.Actions);
        session.incidents.AddRange(record.Incidents);
        if (record.Metrics.Length != record.Seats.Length) throw new FormatException("Invalid metrics.");
        for (int i = 0; i < session.metrics.Length; i++) session.metrics[i] = record.Metrics[i];
        return session;
    }
}

using System.Collections.Immutable;
using System.Text.Json;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Runner;

namespace Ludo.Runner.Tests;

public class RunnerTests
{
    [Fact]
    public void SearchPrefersCaptureOverAllowingAnImmediateLoss()
    {
        var view = GameEngine.CreateGame(new() { PlayerCount = 2 }, 1).View;
        view = view with { Phase = GamePhase.AwaitingMove, Die = 1,
            Players = [view.Players[0] with { Tokens = [23, 0, -1, -1] }, view.Players[1] with { Tokens = [50, 56, 56, 56] }] };
        var choice = BotRegistry.Create(new("v4")).Analyze(view, GameEngine.GetLegalMoves(view), new SeededRng(1));
        Assert.Equal(new Move(0), choice.Move);
    }
    [Theory]
    [InlineData("v1")] [InlineData("v2")] [InlineData("v3")] [InlineData("v4")] [InlineData("v5")] [InlineData("v6")] [InlineData("v7")] [InlineData("v8")] [InlineData("v9")]
    public void EveryBotIsDeterministicLegalAndCannotMutateView(string key)
    {
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 5).View, 6);
        string before = JsonSerializer.Serialize(view, GameEngine.JsonOptions);
        var bot = BotRegistry.Create(new(key));
        var legal = GameEngine.GetLegalMoves(view);
        var first = bot.Analyze(view, legal, new SeededRng(77));
        var second = bot.Analyze(view, legal, new SeededRng(77));
        Assert.Contains(first.Move, legal);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(before, JsonSerializer.Serialize(view, GameEngine.JsonOptions));
    }

    [Fact]
    public void MirroredMatchesUseSameSeedAndBalancedSeats()
    {
        var config = new TournamentConfig([new("v1"), new("v2"), new("v3")], new() { PlayerCount = 2 }, 60);
        var schedule = Tournaments.Schedule(config);
        Assert.Equal(60, schedule.Length);
        foreach (var group in schedule.GroupBy(m => m.SeedGroup))
        {
            Assert.Equal(2, group.Count());
            Assert.Single(group.Select(m => m.Seed).Distinct());
            Assert.Equal(group.First().Seats.Reverse(), group.Last().Seats);
        }
        foreach (var bot in config.Bots)
            Assert.Equal(schedule.Count(m => m.Seats[0] == bot), schedule.Count(m => m.Seats[1] == bot));
    }

    [Fact]
    public void FourPlayersVisitEverySeatEqually()
    {
        var config = new TournamentConfig([new("v1"), new("v2"), new("v3"), new("v4")], new(), 40);
        var schedule = Tournaments.Schedule(config);
        foreach (var bot in config.Bots)
            for (int seat = 0; seat < 4; seat++) Assert.Equal(10, schedule.Count(m => m.Seats[seat] == bot));
        Assert.Throws<ArgumentException>(() => Tournaments.Schedule(config with { Games = 41 }));
    }

    [Theory]
    [InlineData(0, 100, 0, 0.0369935)]
    [InlineData(50, 100, 0.4038315, 0.5961685)]
    [InlineData(100, 100, 0.9630065, 1)]
    public void WilsonIntervalsMatchKnownValues(int wins, int games, double lower, double upper)
    {
        var actual = Tournaments.Wilson(wins, games);
        Assert.InRange(actual.Lower, lower - 0.000001, lower + 0.000001);
        Assert.InRange(actual.Upper, upper - 0.000001, upper + 0.000001);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task IllegalMoveOrTimeoutForfeitsAndReplays(bool timeout)
    {
        var session = new MatchSession(new() { PlayerCount = 2, LaunchRequiresSix = false }, 1, [new("v1"), new("v2")]);
        await using var executor = new FakeExecutor(timeout ? new(new(new(0), []), 51) : new(new(new(99), []), 1));
        await session.AdvanceAsync(executor);
        await session.AdvanceAsync(executor);
        Assert.True(GameEngine.IsTerminal(session.State));
        Assert.Single(session.Incidents);
        Assert.Equal(new[] { 1, 0 }, session.State.View.FinishingOrder);
        Assert.Equal(GameEngine.Serialize(session.State), GameEngine.Serialize(session.Snapshot().Recording.Replay()));
    }

    [Fact]
    public async Task RestoredSessionProducesSameContinuation()
    {
        var original = new MatchSession(new() { PlayerCount = 2 }, 14, [new("v1"), new("v2")], 10000);
        await using var executor = new InlineBotExecutor();
        for (int i = 0; i < 35; i++) await original.AdvanceAsync(executor);
        var restored = MatchSession.Restore(original.Snapshot());
        for (int i = 0; i < 100 && !GameEngine.IsTerminal(original.State); i++)
        {
            await original.AdvanceAsync(executor);
            await restored.AdvanceAsync(executor);
        }
        Assert.Equal(GameEngine.Serialize(original.State), GameEngine.Serialize(restored.State));
        Assert.Equal(original.Actions, restored.Actions);
    }

    [Fact]
    public async Task ParallelismDoesNotChangeDecisionsResultsOrRatings()
    {
        var config = new TournamentConfig([new("v1"), new("v2")], new() { PlayerCount = 2 }, 20, BudgetMilliseconds: 10000);
        var serial = await Tournaments.RunAsync(config, () => new InlineBotExecutor());
        var parallel = await Tournaments.RunAsync(config with { Parallelism = 4 }, () => new InlineBotExecutor());
        Assert.Equal(serial.Games.Select(g => g.Recording.Serialize()), parallel.Games.Select(g => g.Recording.Serialize()));
        Assert.Equal(Tournaments.Statistics(serial).Select(s => s.Elo), Tournaments.Statistics(parallel).Select(s => s.Elo));
        Assert.All(serial.Games, g => Assert.False(g.Truncated));
    }

    [Fact]
    public async Task IncompleteGamesAreNotInventedWins()
    {
        var config = new TournamentConfig([new("v1"), new("v2")], new() { PlayerCount = 2 }, 2, MaximumRolls: 1);
        var result = await Tournaments.RunAsync(config, () => new InlineBotExecutor());
        Assert.All(result.Games, game => { Assert.True(game.Truncated); Assert.Null(game.Result); });
        Assert.All(Tournaments.Statistics(result), row => Assert.Equal(0, row.Games));
    }

    private sealed class FakeExecutor(BotExecution response) : IBotExecutor
    {
        public ValueTask<BotExecution> ExecuteAsync(BotRequest request, double budgetMilliseconds, CancellationToken cancellationToken) => ValueTask.FromResult(response);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

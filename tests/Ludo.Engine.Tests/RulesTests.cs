using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Engine.Tests;

public class RulesTests
{
    private static GameStateView Position(int die, params int[][] tokens)
    {
        var state = GameEngine.CreateGame(new() { PlayerCount = tokens.Length }, 1).View;
        return state with { Phase = GamePhase.AwaitingMove, Die = die,
            Players = state.Players.Select((p, i) => p with { Tokens = tokens[i].ToImmutableArray() }).ToImmutableArray() };
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void CreatesFourTokensPerPlayerAndOppositeSeatsForTwo(int count)
    {
        var state = GameEngine.CreateGame(new() { PlayerCount = count }, 123);
        Assert.Equal(count, state.View.Players.Length);
        Assert.All(state.View.Players, p => Assert.Equal(new[] { -1, -1, -1, -1 }, p.Tokens));
        if (count == 2) Assert.Equal(new[] { 0, 2 }, state.View.Players.Select(p => p.Seat));
        GameEngine.Validate(state);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(5)]
    public void RejectsUnsupportedPlayerCounts(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GameEngine.CreateGame(new() { PlayerCount = count }, 1));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void NonSixCannotLaunchAndPassesAutomatically(int die)
    {
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 1).View, die);
        Assert.Equal(1, view.CurrentPlayer);
        Assert.Equal(EventKind.NoLegalMove, view.LastEvent.Kind);
        Assert.Empty(GameEngine.GetLegalMoves(view));
    }

    [Fact]
    public void SixLaunchesToEntryAndGrantsAnotherRoll()
    {
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 1).View, 6);
        Assert.Equal(4, GameEngine.GetLegalMoves(view).Length);
        view = GameEngine.ApplyMove(view, new(2));
        Assert.Equal(0, view.Players[0].Tokens[2]);
        Assert.Equal(0, view.CurrentPlayer);
        Assert.Equal(GamePhase.AwaitingRoll, view.Phase);
        Assert.Equal(BonusReason.Six, view.LastEvent.Bonus);
    }

    [Fact]
    public void LaunchRestrictionCanBeDisabled()
    {
        var view = GameEngine.CreateGame(new() { LaunchRequiresSix = false }, 1).View;
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 2), new(0));
        Assert.Equal(0, view.Players[0].Tokens[0]);
        Assert.Equal(1, view.CurrentPlayer);
    }

    [Fact]
    public void ThirdSixKeepsFirstTwoMovesAndDiscardsOnlyThirdRoll()
    {
        var view = GameEngine.CreateGame(new(), 1).View;
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 6), new(0));
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 6), new(0));
        var forfeited = GameEngine.ResolveRoll(view, 6);
        Assert.Equal(6, forfeited.Players[0].Tokens[0]);
        Assert.Equal(1, forfeited.CurrentPlayer);
        Assert.Equal(EventKind.ThirdSix, forfeited.LastEvent.Kind);
        Assert.Equal(0, forfeited.ConsecutiveSixes);
        Assert.Equal(3, forfeited.RollNumber);
    }

    [Fact]
    public void ThirdSixRuleCanBeDisabled()
    {
        var view = GameEngine.CreateGame(new() { ForfeitThirdSix = false }, 1).View;
        for (int i = 0; i < 4; i++) view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 6), new(0));
        Assert.Equal(18, view.Players[0].Tokens[0]);
        Assert.Equal(0, view.CurrentPlayer);
    }

    [Fact]
    public void NonSixResetsStreakEvenWhenCaptureExtendsTurn()
    {
        var view = Position(1, [4, -1, -1, -1], [31, -1, -1, -1]) with
        { Phase = GamePhase.AwaitingRoll, Die = 0, ConsecutiveSixes = 2 };
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 1), new(0));
        Assert.Equal(0, view.CurrentPlayer);
        Assert.Equal(0, view.ConsecutiveSixes);
        view = GameEngine.ResolveRoll(view, 6);
        Assert.Equal(GamePhase.AwaitingMove, view.Phase);
        Assert.Equal(1, view.ConsecutiveSixes);
    }

    [Fact]
    public void SixBonusCanBeDisabled()
    {
        var view = GameEngine.CreateGame(new() { ExtraRollOnSix = false }, 1).View;
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 6), new(0));
        Assert.Equal(1, view.CurrentPlayer);
    }

    [Theory]
    [InlineData(1, 55, true)] [InlineData(2, 55, false)] [InlineData(6, 50, true)]
    [InlineData(6, 51, false)] [InlineData(1, 56, false)]
    public void ExactRollHomeEntry(int die, int progress, bool legal)
    {
        var view = Position(die, [progress, 56, 56, 56], [-1, -1, -1, -1]);
        Assert.Equal(legal, GameEngine.GetLegalMoves(view).Contains(new Move(0)));
    }

    [Fact]
    public void HomeBonusCanBeDisabled()
    {
        var view = Position(1, [55, 0, -1, -1], [-1, -1, -1, -1]);
        Assert.Equal(0, GameEngine.ApplyMove(view, new(0)).CurrentPlayer);
        Assert.Equal(1, GameEngine.ApplyMove(view with { Rules = view.Rules with { ExtraRollOnHome = false } }, new(0)).CurrentPlayer);
    }

    [Fact]
    public void CaptureSendsSingletonToBaseAndGrantsBonus()
    {
        var view = Position(1, [4, -1, -1, -1], [31, -1, -1, -1]);
        var after = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(-1, after.Players[1].Tokens[0]);
        Assert.Equal(new CapturedToken(1, 0), Assert.Single(after.LastEvent.Captures));
        Assert.Equal(BonusReason.Capture, after.LastEvent.Bonus);
        Assert.Equal(0, after.CurrentPlayer);
        Assert.Equal(31, view.Players[1].Tokens[0]);
        Assert.Equal(4, view.Players[0].Tokens[0]);
    }

    [Fact]
    public void CaptureBonusCanBeDisabled()
    {
        var view = Position(1, [4, -1, -1, -1], [31, -1, -1, -1]);
        var after = GameEngine.ApplyMove(view with { Rules = view.Rules with { ExtraRollOnCapture = false } }, new(0));
        Assert.Equal(-1, after.Players[1].Tokens[0]);
        Assert.Equal(1, after.CurrentPlayer);
    }

    [Fact]
    public void AllEightStarsProtectEveryColourIncludingInactiveEntries()
    {
        int[] stars = [0, 8, 13, 21, 26, 34, 39, 47];
        Assert.Equal(stars, Enumerable.Range(0, 52).Where(GameEngine.IsSafeSquare));
        foreach (int square in stars)
        {
            // Pick the moving colour so the desired star is ahead on its route.
            int active = square == 0 ? 1 : 0;
            var view = GameEngine.CreateGame(new(), 1).View;
            int from = (square - active * 13 + 52) % 52 - 1;
            var players = view.Players.SetItem(active, view.Players[active] with { Tokens = [from, -1, -1, -1] });
            int opponent = (active + 1) % 4;
            int opposingProgress = (square - opponent * 13 + 52) % 52;
            players = players.SetItem(opponent, players[opponent] with { Tokens = [opposingProgress, -1, -1, -1] });
            view = view with { Players = players, CurrentPlayer = active, Phase = GamePhase.AwaitingMove, Die = 1 };
            var after = GameEngine.ApplyMove(view, new(0));
            Assert.Equal(opposingProgress, after.Players[opponent].Tokens[0]);
            Assert.Empty(after.LastEvent.Captures);
        }
    }

    [Fact]
    public void SafeStarsCanBeDisabled()
    {
        var view = Position(1, [7, -1, -1, -1], [34, -1, -1, -1]);
        view = view with { Rules = view.Rules with { SafeStars = false } };
        Assert.Equal(-1, GameEngine.ApplyMove(view, new(0)).Players[1].Tokens[0]);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void OpponentMayLandOnProtectedStack(int size)
    {
        int[] stack = Enumerable.Repeat(31, size).Concat(Enumerable.Repeat(-1, 4 - size)).ToArray();
        var view = Position(1, [4, -1, -1, -1], stack);
        var after = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(5, after.Players[0].Tokens[0]);
        Assert.Equal(stack, after.Players[1].Tokens);
        Assert.Empty(after.LastEvent.Captures);
    }

    [Fact]
    public void OpponentMayPassProtectedStack()
    {
        var view = Position(2, [4, -1, -1, -1], [31, 31, -1, -1]);
        Assert.Equal(6, GameEngine.ApplyMove(view, new(0)).Players[0].Tokens[0]);
    }

    [Fact]
    public void SplittingStackDoesNotCaptureCoOccupant()
    {
        var view = Position(1, [5, 5, -1, -1], [31, -1, -1, -1]);
        var after = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(5, after.Players[0].Tokens[1]);
        Assert.Equal(31, after.Players[1].Tokens[0]);
        Assert.Empty(after.LastEvent.Captures);
    }

    [Fact]
    public void NewArrivalCapturesEveryOpposingSingletonOnMixedSquare()
    {
        var view = Position(1, [5, -1, -1, -1], [44, -1, -1, -1], [30, -1, -1, -1], [-1, -1, -1, -1])
            with { CurrentPlayer = 2 };
        var after = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(-1, after.Players[0].Tokens[0]);
        Assert.Equal(-1, after.Players[1].Tokens[0]);
        Assert.Equal(2, after.LastEvent.Captures.Length);
    }

    [Fact]
    public void NewArrivalCapturesSingletonButLeavesOtherColoursPair()
    {
        var view = Position(1, [5, 5, -1, -1], [44, -1, -1, -1], [30, -1, -1, -1], [-1, -1, -1, -1])
            with { CurrentPlayer = 2 };
        var after = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(new[] { 5, 5, -1, -1 }, after.Players[0].Tokens);
        Assert.Equal(-1, after.Players[1].Tokens[0]);
        Assert.Single(after.LastEvent.Captures);
    }

    [Fact]
    public void DisablingStackProtectionAllowsCapturingWholeStack()
    {
        var view = Position(1, [4, -1, -1, -1], [31, 31, -1, -1]);
        view = view with { Rules = view.Rules with { ProtectStacks = false } };
        Assert.Equal(2, GameEngine.ApplyMove(view, new(0)).LastEvent.Captures.Length);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void LegacyBlockingPreventsLandingAndPassing(int die)
    {
        var view = Position(die, [4, 56, 56, 56], [31, 31, -1, -1]);
        view = view with { Rules = view.Rules with { StacksBlockMovement = true } };
        Assert.Empty(GameEngine.GetLegalMoves(view));
    }

    [Fact]
    public void OwnPiecesMayStackFreelyEvenWithLegacyBlocking()
    {
        var view = Position(1, [4, 5, 5, 5], [-1, -1, -1, -1]);
        view = view with { Rules = view.Rules with { StacksBlockMovement = true } };
        Assert.Equal(new[] { 5, 5, 5, 5 }, GameEngine.ApplyMove(view, new(0)).Players[0].Tokens);
    }

    [Fact]
    public void NoLegalMoveOnSixEndsTurn()
    {
        var view = Position(6, [55, 56, 56, 56], [-1, -1, -1, -1]) with { Phase = GamePhase.AwaitingRoll, Die = 0 };
        var after = GameEngine.ResolveRoll(view, 6);
        Assert.Equal(1, after.CurrentPlayer);
        Assert.Equal(EventKind.NoLegalMove, after.LastEvent.Kind);
    }

    [Fact]
    public void HomeLanesArePrivate()
    {
        var view = Position(1, [51, -1, -1, -1], [52, -1, -1, -1]);
        var after = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(52, after.Players[0].Tokens[0]);
        Assert.Equal(52, after.Players[1].Tokens[0]);
        Assert.Empty(after.LastEvent.Captures);
    }

    [Fact]
    public void BonusesDoNotAccumulateExtraRolls()
    {
        var view = Position(6, [1, -1, -1, -1], [33, -1, -1, -1]);
        view = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(BonusReason.Six | BonusReason.Capture, view.LastEvent.Bonus);
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 1), new(0));
        Assert.Equal(1, view.CurrentPlayer);
    }

    [Fact]
    public void FourPlayerGameContinuesAndRecordsFullOrder()
    {
        var view = Position(1, [55, 56, 56, 56], [55, 56, 56, 56], [55, 56, 56, 56], [0, -1, -1, -1]);
        view = GameEngine.ApplyMove(view, new(0));
        Assert.Equal(GamePhase.AwaitingRoll, view.Phase);
        Assert.Equal(new[] { 0 }, view.FinishingOrder);
        Assert.Equal(1, view.CurrentPlayer);
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 1), new(0));
        view = GameEngine.ApplyMove(GameEngine.ResolveRoll(view, 1), new(0));
        Assert.Equal(GamePhase.Finished, view.Phase);
        Assert.Equal(new[] { 0, 1, 2, 3 }, view.FinishingOrder);
        Assert.Equal(PlayerStatus.LastRemaining, view.Players[3].Status);
        Assert.Equal(0, view.Players[3].Tokens[0]);
    }

    [Fact]
    public void DisqualifiedPlayersRankBelowFinishersAndAreRemoved()
    {
        var state = GameEngine.CreateGame(new(), 1);
        state = GameEngine.Disqualify(state, 0);
        Assert.Equal(1, state.View.CurrentPlayer);
        state = GameEngine.Disqualify(state, 2);
        state = GameEngine.Disqualify(state, 1);
        Assert.True(GameEngine.IsTerminal(state));
        Assert.Equal(new[] { 3, 1, 2, 0 }, GameEngine.GetResult(state).FinishingOrder);
        Assert.Equal(new[] { 0, 2, 1 }, GameEngine.GetResult(state).DisqualifiedPlayers);
    }

    [Fact]
    public void IllegalActionsAreRejectedWithoutChangingState()
    {
        var state = GameEngine.CreateGame(new(), 1);
        string before = GameEngine.Serialize(state);
        Assert.Throws<ArgumentException>(() => GameEngine.ApplyMove(state, new(0)));
        var view = GameEngine.ResolveRoll(state.View, 6);
        Assert.Throws<ArgumentException>(() => GameEngine.ApplyMove(view, new(4)));
        Assert.Throws<InvalidOperationException>(() => GameEngine.ResolveRoll(view, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameEngine.ResolveRoll(state.View, 0));
        Assert.Throws<InvalidOperationException>(() => GameEngine.GetResult(state));
        Assert.Equal(before, GameEngine.Serialize(state));
    }
}

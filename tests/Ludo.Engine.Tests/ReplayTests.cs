using System.Collections.Immutable;
using Ludo.Engine;

namespace Ludo.Engine.Tests;

public class ReplayTests
{
    [Theory]
    [InlineData(0u, 2)] [InlineData(1u, 4)] [InlineData(4294967295u, 4)]
    public void SeedAndMoveLogReproduceExactFinalState(uint seed, int players)
    {
        var rules = new RuleConfig { PlayerCount = players };
        var state = GameEngine.CreateGame(rules, seed);
        var botRng = new SeededRng(SeededRng.Derive(seed, 1));
        var actions = ImmutableArray.CreateBuilder<RecordedAction>();
        for (int step = 0; step < 100000 && !GameEngine.IsTerminal(state); step++)
        {
            if (state.View.Phase == GamePhase.AwaitingRoll)
            {
                state = GameEngine.Roll(state);
                actions.Add(new(ActionKind.Roll, state.View.LastEvent.Die));
            }
            else
            {
                var moves = GameEngine.GetLegalMoves(state);
                var move = moves[botRng.NextInt(moves.Length)];
                state = GameEngine.ApplyMove(state, move);
                actions.Add(new(ActionKind.Move, move.TokenId));
            }
            GameEngine.Validate(state);
        }
        Assert.True(GameEngine.IsTerminal(state));
        var log = new GameRecording(GameEngine.Version, rules, seed, actions.ToImmutable());
        Assert.Equal(GameEngine.Serialize(state), GameEngine.Serialize(GameRecording.Deserialize(log.Serialize()).Replay()));
        Assert.Equal(GameEngine.Serialize(state), GameEngine.Serialize(GameEngine.Deserialize(GameEngine.Serialize(state))));
    }

    [Fact]
    public void ReplayRejectsAlteredDiceAndEngineVersion()
    {
        var state = GameEngine.Roll(GameEngine.CreateGame(new(), 44));
        var replay = new GameRecording(GameEngine.Version, new(), 44,
            [new(ActionKind.Roll, state.View.LastEvent.Die % 6 + 1)]);
        Assert.Throws<FormatException>(() => replay.Replay());
        Assert.Throws<NotSupportedException>(() => (replay with { EngineVersion = "unknown" }).Replay());
    }

    [Fact]
    public void ReplayRecordsDisqualificationsWithoutRerunningClocks()
    {
        var recording = new GameRecording(GameEngine.Version, new() { PlayerCount = 2 }, 1,
            [new(ActionKind.Disqualify, 0)]);
        Assert.Equal(new[] { 1, 0 }, recording.Replay().View.FinishingOrder);
    }

    [Fact]
    public void BotRandomDrawsDoNotChangeDice()
    {
        var first = GameEngine.CreateGame(new(), 765);
        var second = GameEngine.CreateGame(new(), 765);
        var bot = new SeededRng(SeededRng.Derive(765, 1));
        for (int i = 0; i < 10000; i++) bot.NextUInt();
        Assert.Equal(GameEngine.Serialize(GameEngine.Roll(first)), GameEngine.Serialize(GameEngine.Roll(second)));
        Assert.DoesNotContain(typeof(GameStateView).GetProperties(), p => p.Name.Contains("Rng") || p.Name.Contains("DiceState"));
    }

    [Fact]
    public void RngSequenceIsPinned()
    {
        var rng = new SeededRng(1);
        // These words are the published mulberry32 arithmetic, with uint wraparound.
        Assert.Equal(2693262067u, rng.NextUInt());
        Assert.Equal(11749833u, rng.NextUInt());
        Assert.Equal(2265367787u, rng.NextUInt());
    }

    [Fact]
    public void GeneratorCanResumeAndBoundsAreValidated()
    {
        var rng = new SeededRng(22);
        rng.NextUInt();
        var resumed = new SeededRng(rng.State);
        Assert.Equal(rng.NextUInt(), resumed.NextUInt());
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        for (int i = 0; i < 1000; i++) Assert.InRange(rng.NextInt(6), 0, 5);
    }

    [Fact]
    public void ImmutableCollectionsCannotMutateRealStateThroughCopies()
    {
        var state = GameEngine.CreateGame(new(), 1);
        var mutable = state.View.Players[0].Tokens.ToArray();
        mutable[0] = 50;
        var replacement = state.View.Players[0] with { Tokens = [50, 50, 50, 50] };
        _ = state.View with { Players = state.View.Players.SetItem(0, replacement) };
        Assert.All(state.View.Players[0].Tokens, token => Assert.Equal(-1, token));
    }

    [Fact]
    public void DeserializeRejectsContradictoryPlayerStatuses()
    {
        var state = GameEngine.CreateGame(new(), 1);
        var invalid = state with { View = state.View with { Players = state.View.Players.SetItem(0,
            state.View.Players[0] with { Status = PlayerStatus.Finished }) } };
        Assert.Throws<FormatException>(() => GameEngine.Deserialize(GameEngine.Serialize(invalid)));
    }

    [Fact]
    public void ReachableStatesPreserveInvariantsAcrossRuleCombinations()
    {
        for (uint seed = 0; seed < 64; seed++)
        {
            var rules = new RuleConfig { PlayerCount = seed % 2 == 0 ? 2 : 4,
                LaunchRequiresSix = (seed & 1) == 0, ExtraRollOnSix = (seed & 2) == 0,
                ExtraRollOnCapture = (seed & 4) == 0, ExtraRollOnHome = (seed & 8) == 0,
                SafeStars = (seed & 16) == 0, ProtectStacks = (seed & 32) == 0,
                StacksBlockMovement = seed % 3 == 0, ForfeitThirdSix = seed % 5 != 0 };
            var state = GameEngine.CreateGame(rules, seed);
            var rng = new SeededRng(SeededRng.Derive(seed, 3));
            for (int step = 0; step < 800 && !GameEngine.IsTerminal(state); step++)
            {
                if (state.View.Phase == GamePhase.AwaitingRoll) state = GameEngine.Roll(state);
                else
                {
                    var moves = GameEngine.GetLegalMoves(state);
                    state = GameEngine.ApplyMove(state, moves[rng.NextInt(moves.Length)]);
                }
                GameEngine.Validate(state);
                Assert.All(state.View.Players, p => Assert.Equal(4, p.Tokens.Length));
            }
        }
    }
}

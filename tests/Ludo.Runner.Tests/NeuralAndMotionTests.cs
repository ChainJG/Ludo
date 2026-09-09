using System.Collections.Immutable;
using System.Text.Json;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Presentation;
using Ludo.Training;

namespace Ludo.Runner.Tests;

public class NeuralAndMotionTests
{
    [Fact]
    public void NeuralGradientIncreasesRewardedChoiceAndDecreasesPunishedChoice()
    {
        var model = NeuralNetwork.Initialize(123);
        var features = new[] { Enumerable.Repeat(0.0, 24).ToArray(), Enumerable.Repeat(0.5, 24).ToArray() };
        var network = new NeuralTrainer.Trainable(model);
        double before = network.Probabilities(features)[1];
        network.Update([(features, 1)], 1, .02);
        Assert.True(network.Probabilities(features)[1] > before);
        var punished = new NeuralTrainer.Trainable(model);
        punished.Update([(features, 1)], -1, .02);
        Assert.True(punished.Probabilities(features)[1] < before);
        Assert.Equal(1, network.Probabilities(features).Sum(), 12);
    }

    [Fact]
    public void ExportedNetworkSurvivesPortableJsonWithIdenticalScores()
    {
        var spec = new BotSpec("v7", Model: NeuralNetwork.Initialize(432));
        var copy = JsonSerializer.Deserialize<BotSpec>(JsonSerializer.Serialize(spec, RunnerJson.Options), RunnerJson.Options)!;
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 3).View, 6);
        var legal = GameEngine.GetLegalMoves(view);
        Assert.Equal(BotRegistry.Create(spec).Analyze(view, legal, new SeededRng(1)).Candidates.ToArray(),
            BotRegistry.Create(copy).Analyze(view, legal, new SeededRng(1)).Candidates.ToArray());
        Assert.Equal(spec, copy);
        Assert.Throws<ArgumentException>(() => (spec.Model! with { InputWeights = [double.NaN] }).Validate());
        Assert.Throws<ArgumentException>(() => (spec.Model! with { Schema = 900 }).Validate());
    }

    [Fact]
    public async Task ResumedTrainingMatchesUninterruptedTrainingAndPreservesInitialModel()
    {
        var initial = NeuralNetwork.Initialize(98, 4); string original = JsonSerializer.Serialize(initial);
        var config = new TrainingConfig(4, 1, 2, 2, 98, .02);
        var full = await NeuralTrainer.TrainAsync(config, initial);
        var half = await NeuralTrainer.TrainAsync(config with { Games = 2 }, initial);
        var resumed = await NeuralTrainer.TrainAsync(config with { Games = 2 }, half.Latest.Model);
        Assert.Equal(JsonSerializer.Serialize(full.Latest), JsonSerializer.Serialize(resumed.Latest));
        Assert.NotEqual(original, JsonSerializer.Serialize(full.Latest.Model));
        Assert.Equal(original, JsonSerializer.Serialize(initial));
        Assert.Equal(3, full.History.Length);
    }

    [Fact]
    public void MotionVisitsEverySquareAndCaptureReturnsThroughVictimsRoute()
    {
        var before = GameEngine.CreateGame(new() { PlayerCount = 2 }, 1).View;
        // Yellow moves 23 -> 27; Red progress 1 occupies global square 27.
        before = before with { Phase = GamePhase.AwaitingMove, Die = 4,
            Players = [before.Players[0] with { Tokens = [23, -1, -1, -1] }, before.Players[1] with { Tokens = [1, -1, -1, -1] }] };
        var after = GameEngine.ApplyMove(before, new(0));
        var motion = BoardMotion.Between(before, after);
        Assert.Equal(2, motion.Moves.Length);
        Assert.False(motion.Moves[0].Captured); Assert.Equal(5, motion.Moves[0].Points.Length);
        Assert.True(motion.Moves[1].Captured); Assert.Equal(3, motion.Moves[1].Points.Length);
        var entry = BoardPresentation.Location(2, 0, 0);
        Assert.Equal(BoardPresentation.ToPercent(entry), motion.Moves[1].Points[1]);
        Assert.Equal(23, before.Players[0].Tokens[0]);
    }

    [Fact]
    public void LaunchAndHomeMotionUseCorrectEndpointsAndStackSplitDoesNotInventCapture()
    {
        var before = GameEngine.ResolveRoll(GameEngine.CreateGame(new() { PlayerCount = 2 }, 1).View, 6);
        Assert.Equal(2, BoardMotion.Between(before, GameEngine.ApplyMove(before, new(0))).Moves[0].Points.Length);
        before = before with { Die = 2, Players = [before.Players[0] with { Tokens = [54, 4, 4, -1] }, before.Players[1] with { Tokens = [30, -1, -1, -1] }] };
        Assert.Equal(3, BoardMotion.Between(before, GameEngine.ApplyMove(before, new(0))).Moves[0].Points.Length);
        Assert.Single(BoardMotion.Between(before, GameEngine.ApplyMove(before, new(1))).Moves);
    }

    [Theory]
    [InlineData(0, 0, -1, 202, 202)]
    [InlineData(1, 1, -1, 919, 202)]
    [InlineData(2, 0, -1, 1048, 1016)]
    [InlineData(3, 3, -1, 331, 889)]
    [InlineData(0, 0, 0, 155, 528)]
    [InlineData(1, 0, 0, 706.75, 158.25)]
    [InlineData(2, 0, 0, 1098.75, 684.25)]
    [InlineData(3, 0, 0, 546.25, 1062.5)]
    public void PawnFeetMatchArtworkHomeAndEntryCenters(int seat, int token, int progress, double x, double y)
    {
        var point = BoardPresentation.ToPercent(BoardPresentation.Location(seat, token, progress));
        Assert.Equal(x, point.X * 12.54, 6);
        Assert.Equal(y, point.Y * 12.54, 6);
    }
}

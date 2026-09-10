using System.Collections.Immutable;
using Ludo.Bots;
using Ludo.Bots.Versions;
using Ludo.Engine;

namespace Ludo.Runner.Tests;

// Watchable behaviours of Fable v9, each in a hand-built two-player position.
// Yellow (seat 0) moves; Red (seat 2) squares are (26 + progress) % 52.
public class FableTests
{
    private static GameStateView Position(int die, int[] yellow, int[] red, int sixes = 0)
    {
        var view = GameEngine.CreateGame(new() { PlayerCount = 2 }, 1).View;
        return view with { Phase = GamePhase.AwaitingMove, Die = die, ConsecutiveSixes = sixes,
            Players = [view.Players[0] with { Tokens = yellow.ToImmutableArray() }, view.Players[1] with { Tokens = red.ToImmutableArray() }] };
    }

    private static Move Pick(GameStateView view) => BotRegistry.Create(new("v9")).Analyze(view, GameEngine.GetLegalMoves(view), new SeededRng(1)).Move;

    [Fact]
    public void KillsWhenACaptureIsAvailable()
    {
        // Red progress 38 sits on square 12, three ahead of Yellow's token on 9.
        Assert.Equal(new Move(0), Pick(Position(3, [9, 20, -1, -1], [38, -1, -1, -1])));
    }

    [Fact]
    public void KeepsASniperParkedOnAStarWhileTrafficApproachesFromBehind()
    {
        // Red tokens on squares 2 and 4 must pass Yellow's star on 8. Stepping to 10 would walk into their range.
        Assert.Equal(new Move(1), Pick(Position(2, [8, 30, -1, -1], [28, 30, -1, -1])));
    }

    [Fact]
    public void CampsOnTheEnemyEntryWhileTheyStillHaveTokensInBase()
    {
        // 20 -> 26 is Red's entry star: safe, and every launched Red token must step off it into range.
        Assert.Equal(new Move(0), Pick(Position(6, [20, 31, 56, 56], [-1, -1, -1, 40])));
    }

    [Fact]
    public void LeavesASharedStarWithASixBecauseTheBonusRollCarriesItOutOfRange()
    {
        // Red progress 34 shares star 8 with Yellow's token, and Red has three other free runners, so Yellow is
        // the side that will eventually be forced off the star. The six is spent on the escape (8 -> 14, then the
        // bonus roll carries the token beyond a single die) instead of the quiet advance 30 -> 36.
        // Search v5 and ChatGPT Tactician v8 both prefer the quiet advance here.
        Assert.Equal(new Move(0), Pick(Position(6, [8, 30, 56, 56], [34, 5, 15, 20], 1)));
    }

    [Fact]
    public void EvaluationIsSymmetricAndSearchIsBudgetedAndSeatIndependent()
    {
        var start = GameEngine.CreateGame(new(), 3).View;
        Assert.Equal(FableV9.Evaluate(start, 0), FableV9.Evaluate(start, 2));
        var view = GameEngine.ResolveRoll(start, 6);
        var choice = BotRegistry.Create(new("v9")).Analyze(view, GameEngine.GetLegalMoves(view), new SeededRng(1));
        Assert.InRange(choice.Nodes, 1, 2500);
        Assert.All(choice.Candidates, c => Assert.InRange(c.Score, 0, 1));
    }
}

using System.Collections.Immutable;
using System.Text.Json;

namespace Ludo.Engine;

public enum ActionKind { Roll, Move, Disqualify }
public readonly record struct RecordedAction(ActionKind Kind, int Value);
public sealed record GameRecording(string EngineVersion, RuleConfig Rules, uint Seed, ImmutableArray<RecordedAction> Actions)
{
    public GameState Replay()
    {
        if (EngineVersion != GameEngine.Version) throw new NotSupportedException($"Replay requires engine {EngineVersion}.");
        var state = GameEngine.CreateGame(Rules, Seed);
        foreach (var action in Actions)
        {
            state = action.Kind switch
            {
                ActionKind.Roll => GameEngine.Roll(state),
                ActionKind.Move => GameEngine.ApplyMove(state, new(action.Value)),
                ActionKind.Disqualify => GameEngine.Disqualify(state, action.Value),
                _ => throw new FormatException("Unknown replay action.")
            };
            if (action.Kind == ActionKind.Roll && state.View.LastEvent.Die != action.Value)
                throw new FormatException("The recorded die does not match the seeded sequence.");
        }
        return state;
    }

    public string Serialize() => JsonSerializer.Serialize(this, GameEngine.JsonOptions);
    public static GameRecording Deserialize(string json) =>
        JsonSerializer.Deserialize<GameRecording>(json, GameEngine.JsonOptions) ?? throw new FormatException("Missing replay.");
}

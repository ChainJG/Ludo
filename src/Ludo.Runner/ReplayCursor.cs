using Ludo.Engine;

namespace Ludo.Runner;

public sealed class ReplayCursor
{
    public MatchRecord Record { get; }
    public GameState State { get; private set; }
    public int Position { get; private set; }
    public bool AtEnd => Position >= Record.Actions.Length;
    public ReplayCursor(MatchRecord record)
    {
        if (record.EngineVersion != GameEngine.Version) throw new NotSupportedException("This replay needs a different engine version.");
        Record = record;
        State = GameEngine.CreateGame(record.Rules, record.Seed);
    }
    public void Step()
    {
        if (AtEnd) return;
        var action = Record.Actions[Position];
        var next = action.Kind switch
        {
            ActionKind.Roll => GameEngine.Roll(State),
            ActionKind.Move => GameEngine.ApplyMove(State, new(action.Value)),
            ActionKind.Disqualify => GameEngine.Disqualify(State, action.Value),
            _ => throw new FormatException("Invalid replay action.")
        };
        if (action.Kind == ActionKind.Roll && next.View.LastEvent.Die != action.Value) throw new FormatException("Replay dice mismatch.");
        State = next;
        Position++;
    }
}

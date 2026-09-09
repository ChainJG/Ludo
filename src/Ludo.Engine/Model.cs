using System.Collections.Immutable;

namespace Ludo.Engine;

public sealed record RuleConfig
{
    public int PlayerCount { get; init; } = 4;
    public bool LaunchRequiresSix { get; init; } = true;
    public bool ExtraRollOnSix { get; init; } = true;
    public bool ForfeitThirdSix { get; init; } = true;
    public bool ExtraRollOnCapture { get; init; } = true;
    public bool ExtraRollOnHome { get; init; } = true;
    public bool SafeStars { get; init; } = true;
    public bool ProtectStacks { get; init; } = true;
    public bool StacksBlockMovement { get; init; }

    public void Validate()
    {
        if (PlayerCount is not (2 or 4))
            throw new ArgumentOutOfRangeException(nameof(PlayerCount), "Choose two or four players.");
    }
}

public enum GamePhase { AwaitingRoll, AwaitingMove, Finished }
public enum PlayerStatus { Playing, Finished, LastRemaining, Disqualified }
public enum EventKind { Started, Rolled, Moved, NoLegalMove, ThirdSix, Disqualified }
[Flags]
public enum BonusReason { None = 0, Six = 1, Capture = 2, Home = 4 }

public sealed record PlayerState(int Seat, ImmutableArray<int> Tokens, PlayerStatus Status = PlayerStatus.Playing);
public readonly record struct Move(int TokenId);
public readonly record struct CapturedToken(int PlayerIndex, int TokenId);
public sealed record GameEvent(EventKind Kind, int PlayerIndex, int Die = 0, int TokenId = -1,
    int From = -1, int To = -1, ImmutableArray<CapturedToken> Captures = default,
    BonusReason Bonus = BonusReason.None)
{
    public ImmutableArray<CapturedToken> Captures { get; init; } = Captures.IsDefault ? [] : Captures;
}

// This is the complete information available to bots. Dice generator state is deliberately absent.
public sealed record GameStateView(
    RuleConfig Rules,
    ImmutableArray<PlayerState> Players,
    int CurrentPlayer,
    GamePhase Phase,
    int Die,
    int ConsecutiveSixes,
    int TurnNumber,
    int RollNumber,
    ImmutableArray<int> FinishingOrder,
    ImmutableArray<int> DisqualificationOrder,
    GameEvent LastEvent);

public sealed record GameState(GameStateView View, uint DiceState);
public sealed record GameResult(ImmutableArray<int> FinishingOrder, ImmutableArray<int> DisqualifiedPlayers,
    int Turns, int Rolls);

public interface IRng
{
    uint NextUInt();
    int NextInt(int exclusiveMax);
}

// The algorithm and seed derivation are versioned with the engine, rather than using System.Random.
public sealed class SeededRng(uint seed) : IRng
{
    public uint State { get; private set; } = seed;
    public uint NextUInt()
    {
        unchecked
        {
            State += 0x6D2B79F5;
            uint value = State;
            value = (value ^ (value >> 15)) * (value | 1);
            value ^= value + ((value ^ (value >> 7)) * (value | 61));
            return value ^ (value >> 14);
        }
    }

    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        // Rejection avoids modulo bias, including for the six-sided die.
        uint limit = unchecked(0u - (uint)exclusiveMax) % (uint)exclusiveMax;
        uint value;
        do { value = NextUInt(); } while (value < limit);
        return (int)(value % (uint)exclusiveMax);
    }

    public static uint Derive(uint seed, uint stream)
    {
        unchecked
        {
            uint value = seed ^ (stream * 0x9E3779B9u);
            value = (value ^ (value >> 16)) * 0x85EBCA6Bu;
            value = (value ^ (value >> 13)) * 0xC2B2AE35u;
            return value ^ (value >> 16);
        }
    }
}

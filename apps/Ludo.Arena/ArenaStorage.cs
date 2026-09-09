using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ludo.Engine;
using Ludo.Runner;

namespace Ludo.Arena;

public sealed record LocalRecord(int Wins, int Losses);
public sealed record SavedArena(MatchRecord Match, Dictionary<string, LocalRecord> Records, bool Counted);
public sealed record GameReadout(GameStateView State, ImmutableArray<Move> LegalMoves, bool CanRoll, bool CanMove,
    bool Busy, string Error, bool HasSavedGame);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavedArena))]
[JsonSerializable(typeof(GameReadout))]
[JsonSerializable(typeof(Ludo.Presentation.BoardMotion))]
public partial class ArenaJsonContext : JsonSerializerContext;

public static class ArenaJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    { TypeInfoResolver = JsonTypeInfoResolver.Combine(ArenaJsonContext.Default, RunnerJsonContext.Default, EngineJsonContext.Default) };
}

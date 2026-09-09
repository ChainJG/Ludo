using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ludo.Bots;
using Ludo.Engine;

namespace Ludo.Runner;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BotRequest))]
[JsonSerializable(typeof(BotExecution))]
[JsonSerializable(typeof(MatchRecord))]
[JsonSerializable(typeof(TournamentResult))]
[JsonSerializable(typeof(BotSpec))]
public partial class RunnerJsonContext : JsonSerializerContext;

public static class RunnerJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    { TypeInfoResolver = JsonTypeInfoResolver.Combine(RunnerJsonContext.Default, EngineJsonContext.Default) };
}

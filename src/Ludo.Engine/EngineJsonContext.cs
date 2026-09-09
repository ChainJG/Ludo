using System.Text.Json.Serialization;

namespace Ludo.Engine;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(GameStateView))]
[JsonSerializable(typeof(GameRecording))]
[JsonSerializable(typeof(GameResult))]
public partial class EngineJsonContext : JsonSerializerContext;

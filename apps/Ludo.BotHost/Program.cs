using System.Text.Json;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Runner;

await using var executor = new InlineBotExecutor();
// Warm JIT paths before a measured request. Startup never consumes a player's decision budget.
var warm = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 1).View, 6);
foreach (var definition in BotRegistry.All)
    for (int i = 0; i < 3; i++) await executor.ExecuteAsync(new(new(definition.Key), warm, 1), 50, default);
Console.WriteLine("ready");
while (await Console.In.ReadLineAsync() is { } line)
{
    BotExecution result;
    try
    {
        var request = JsonSerializer.Deserialize<BotRequest>(line, RunnerJson.Options) ?? throw new FormatException("Empty request.");
        Console.WriteLine("started");
        result = await executor.ExecuteAsync(request, 50, default);
    }
    catch (Exception exception)
    {
        result = new(null, 0, exception.Message);
    }
    Console.WriteLine(JsonSerializer.Serialize(result, RunnerJson.Options));
}

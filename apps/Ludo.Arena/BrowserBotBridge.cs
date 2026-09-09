using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Runner;
using Microsoft.JSInterop;

namespace Ludo.Arena;

[SupportedOSPlatform("browser")]
public static partial class BrowserBotBridge
{
    [JSExport]
    public static string Evaluate(string json)
    {
        var request = JsonSerializer.Deserialize<BotRequest>(json, RunnerJson.Options) ?? throw new FormatException("Invalid bot request.");
        var execution = new InlineBotExecutor().ExecuteAsync(request, 50, default).Result;
        return JsonSerializer.Serialize(execution, RunnerJson.Options);
    }
    [JSExport]
    public static void Warmup()
    {
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 1).View, 6);
        foreach (var bot in BotRegistry.All)
            for (int i = 0; i < 3; i++) _ = BotRegistry.Create(new(bot.Key)).Analyze(view, GameEngine.GetLegalMoves(view), new SeededRng(1));
    }
    [JSExport]
    public static string Replay(string json)
    {
        var record = JsonSerializer.Deserialize<MatchRecord>(json, RunnerJson.Options) ?? throw new FormatException("Invalid match.");
        return GameEngine.Serialize(record.Recording.Replay());
    }
}

public sealed class BrowserBotExecutor(IJSObjectReference module) : IBotExecutor
{
    public async ValueTask<BotExecution> ExecuteAsync(BotRequest request, double budgetMilliseconds, CancellationToken cancellationToken)
    {
        string result = await module.InvokeAsync<string>("executeBot", cancellationToken,
            JsonSerializer.Serialize(request, RunnerJson.Options), budgetMilliseconds);
        return JsonSerializer.Deserialize<BotExecution>(result, RunnerJson.Options) ?? throw new FormatException("Invalid worker response.");
    }
    public ValueTask DisposeAsync() => module.InvokeVoidAsync("stopWorker");
}

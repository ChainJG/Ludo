using System.Text.Json;
using Ludo.Runner;

Console.WriteLine("ready");
while (await Console.In.ReadLineAsync() is { } line)
{
    var request = JsonSerializer.Deserialize<BotRequest>(line, RunnerJson.Options)!;
    Console.WriteLine("started");
    if (request.Seed == 666) await Task.Delay(Timeout.Infinite);
    if (request.Seed == 999) Environment.Exit(9);
    Console.WriteLine(JsonSerializer.Serialize(new BotExecution(new(new(0), []), 1), RunnerJson.Options));
}

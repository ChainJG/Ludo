using System.Collections.Immutable;
using System.Text.Json;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Native;
using Ludo.Runner;
using Ludo.Training;

string Value(string option, string fallback) { int index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
bool Flag(string option) => args.Contains(option);
var jsonOptions = new JsonSerializerOptions(RunnerJson.Options) { WriteIndented = false };
async Task Save(string path, object value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, jsonOptions));
}
try
{
    string command = args.FirstOrDefault() ?? "help";
    if (command == "help")
    {
        Console.WriteLine("Neural training: train --games 2000 --teacher v3 --out my-bot.json\nContinue training: train --resume my-bot.latest.json --games 2000 --out next-bot.json\nOptions: --warmup 200 --evaluate-every 200 --evaluation-games 100 --learning-rate 0.015 --players 2 --seed 73001\n         --teacher v9 (any bot version) --opponents v2,v3,v8,v9 (self-play opponents after warmup)\n");
        Console.WriteLine("Ludo laboratory\n  bots\n  match --bots v1,v2 --seed 1 --out match.json\n  batch --bots v1,v2 --games 500 --seed 1 --parallel 4 --out results.json\n  gate --candidate v2 --champion v1 --games 500\n  replay --file match.json\n  tune --games 100 --rounds 4 --seed 9000 --out weights.json\n\nAll matches use isolated bot processes by default. --inline is for trusted throughput experiments.\nFour-player runs: --players 4 --bots v1,v2,v3,v4. Budgets: --budget 50.");
        return 0;
    }
    if (command == "train")
    {
        NeuralModel? initial = null;
        if (Flag("--resume"))
        {
            var saved = JsonSerializer.Deserialize<BotSpec>(await File.ReadAllTextAsync(Value("--resume", "model.json")), RunnerJson.Options);
            initial = saved?.Model ?? throw new FormatException("Resume requires an exported neural bot file.");
            _ = BotRegistry.Create(saved!);
        }
        var config = new TrainingConfig(int.Parse(Value("--games", "2000")), int.Parse(Value("--warmup", "200")),
            int.Parse(Value("--evaluate-every", "200")), int.Parse(Value("--evaluation-games", "100")),
            uint.Parse(Value("--seed", (initial?.TrainingSeed ?? 73001).ToString())),
            double.Parse(Value("--learning-rate", "0.015"), System.Globalization.CultureInfo.InvariantCulture), int.Parse(Value("--players", "2")), Value("--teacher", "v3"),
            Value("--opponents", "v1,v2,v3"));
        string path = Value("--out", "artifacts/neural-bot.json");
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        var progress = new Progress<TrainingProgress>(p => { if (p.Completed % 100 == 0 || p.WinRate is not null) Console.WriteLine($"{p.Completed}/{p.Total} · {p.Stage}" + (p.WinRate is { } rate ? $" · validation {rate:P1}" : "")); });
        var trained = await NeuralTrainer.TrainAsync(config, initial, progress, state => TrainingFiles.SaveAsync(path, state), cancel.Token);
        await TrainingFiles.SaveAsync(path, trained);
        Console.WriteLine($"Saved best validation model: {Path.GetFullPath(path)}. Latest model and training history saved alongside. Evaluate on fresh seeds before promotion.");
        return 0;
    }
    if (command == "bots")
    {
        foreach (var bot in BotRegistry.All) Console.WriteLine($"{bot.Key}: {bot.Label} — {bot.Changelog}");
        return 0;
    }
    if (command == "replay")
    {
        var record = JsonSerializer.Deserialize<MatchRecord>(await File.ReadAllTextAsync(Value("--file", "match.json")), jsonOptions)
            ?? throw new FormatException("Invalid match record.");
        var state = record.Recording.Replay();
        if (Flag("--state-out")) await File.WriteAllTextAsync(Value("--state-out", "state.json"), GameEngine.Serialize(state));
        Console.WriteLine($"Replay verified. {record.Actions.Length} actions; phase {state.View.Phase}; order {string.Join(", ", state.View.FinishingOrder)}.");
        return 0;
    }
    if (command == "analyze")
    {
        var request = JsonSerializer.Deserialize<BotRequest>(await File.ReadAllTextAsync(Value("--file", "request.json")), RunnerJson.Options)
            ?? throw new FormatException("Invalid bot request.");
        var execution = await new InlineBotExecutor().ExecuteAsync(request, 50, default);
        Console.WriteLine(JsonSerializer.Serialize(execution, RunnerJson.Options));
        return 0;
    }
    int players = int.Parse(Value("--players", "2"));
    double budget = double.Parse(Value("--budget", "50"), System.Globalization.CultureInfo.InvariantCulture);
    uint seed = uint.Parse(Value("--seed", "1"));
    var botKeys = Value("--bots", "v1,v2").Split(',');
    var bots = botKeys.Select(key => new BotSpec(key.Trim())).ToImmutableArray();
    Func<IBotExecutor> executorFactory = Flag("--inline") ? () => new InlineBotExecutor()
        : () => new ProcessBotExecutor(Value("--host", ProcessBotExecutor.DefaultHostPath));
    if (command == "match")
    {
        var match = new MatchSession(new() { PlayerCount = players }, seed, bots.Select(b => (BotSpec?)b).ToImmutableArray(), budget);
        await using var executor = executorFactory();
        while (!GameEngine.IsTerminal(match.State) && match.State.View.RollNumber < 20000) await match.AdvanceAsync(executor);
        var record = match.Snapshot(!GameEngine.IsTerminal(match.State));
        string path = Value("--out", "match.json");
        await Save(path, record);
        Console.WriteLine($"{record.Actions.Length} actions, {record.Incidents.Length} incidents. Saved {Path.GetFullPath(path)}");
        return record.Truncated ? 2 : 0;
    }
    if (command == "tune")
    {
        var rng = new SeededRng(seed);
        var best = new EvaluationWeights();
        int games = int.Parse(Value("--games", "100"));
        int rounds = int.Parse(Value("--rounds", "4"));
        double bestScore = double.NegativeInfinity;
        for (int round = 0; round < rounds; round++)
        {
            double Perturb(double value) => value * (0.7 + rng.NextInt(601) / 1000.0);
            var candidate = round == 0 ? best : best with { Progress = Perturb(best.Progress), Safety = Perturb(best.Safety),
                Stack = Perturb(best.Stack), Capture = Perturb(best.Capture), Risk = Perturb(best.Risk), Home = Perturb(best.Home), Launch = Perturb(best.Launch) };
            var config = new TournamentConfig([new("v3", candidate), new("v2")], new() { PlayerCount = 2 }, games,
                SeededRng.Derive(seed, 10000), int.Parse(Value("--parallel", "2")), budget);
            var result = await Tournaments.RunAsync(config, executorFactory);
            double score = Tournaments.Statistics(result)[0].WinRate;
            Console.WriteLine($"Tuning round {round + 1}: {score:P1}");
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        await Save(Value("--out", "weights.json"), new BotSpec("v3", best));
        Console.WriteLine("Saved experimental weights. Evaluate on new seeds before promoting a new frozen version.");
        return 0;
    }
    if (command is not ("batch" or "gate")) throw new ArgumentException("Unknown command. Run help.");
    if (command == "gate")
    {
        players = 2;
        var candidate = Flag("--candidate-config")
            ? JsonSerializer.Deserialize<BotSpec>(await File.ReadAllTextAsync(Value("--candidate-config", "weights.json")), jsonOptions)
                ?? throw new FormatException("Invalid candidate configuration.")
            : new(Value("--candidate", "v2"));
        bots = [candidate, new(Value("--champion", "v1"))];
    }
    var tournament = new TournamentConfig(bots, new() { PlayerCount = players }, int.Parse(Value("--games", "500")), seed,
        int.Parse(Value("--parallel", "2")), budget);
    var resultSet = await Tournaments.RunAsync(tournament, executorFactory);
    var statistics = Tournaments.Statistics(resultSet);
    foreach (var row in statistics) Console.WriteLine($"{row.Bot}: {row.Wins}/{row.Games} ({row.WinRate:P1}), 95% Wilson [{row.Lower95:P1}, {row.Upper95:P1}], Elo {row.Elo:F0}, incidents {row.Incidents}");
    Console.WriteLine($"{resultSet.Games.Length / resultSet.ElapsedSeconds:F1} games/s; {resultSet.ElapsedSeconds:F2}s; incomplete {resultSet.Games.Count(g => g.Truncated)}");
    string output = Value("--out", "results.json");
    await Save(output, resultSet);
    await File.WriteAllTextAsync(Path.ChangeExtension(output, ".csv"), Tournaments.Csv(resultSet));
    if (command == "gate")
    {
        bool pass = statistics[0].WinRate > 0.55 && statistics[0].Lower95 > 0.5
            && Tournaments.PairedLower95(resultSet, bots[0]) > 0.5
            && resultSet.Games.All(g => !g.Truncated && g.Incidents.IsEmpty);
        Console.WriteLine(pass ? "PASS" : "FAIL");
        Console.WriteLine($"Conservative bound across paired seed groups: {Tournaments.PairedLower95(resultSet, bots[0]):P1}");
        return pass ? 0 : 1;
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

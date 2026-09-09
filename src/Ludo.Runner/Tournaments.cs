using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ludo.Bots;
using Ludo.Engine;

namespace Ludo.Runner;

public sealed record TournamentConfig(ImmutableArray<BotSpec> Bots, RuleConfig Rules, int Games = 500,
    uint BaseSeed = 1, int Parallelism = 1, double BudgetMilliseconds = 50, int MaximumRolls = 20000);
public sealed record ScheduledMatch(int Index, int SeedGroup, uint Seed, ImmutableArray<BotSpec?> Seats);
public sealed record TournamentProgress(int Completed, int Total, double GamesPerSecond);
public sealed record TournamentResult(TournamentConfig Config, ImmutableArray<MatchRecord> Games, double ElapsedSeconds);
public sealed record BotStatistics(string Bot, int Games, int Wins, double WinRate, double Lower95, double Upper95,
    double AveragePlace, double AverageTurns, double Captures, double Lost, double MoveMilliseconds, int Incidents, double Elo);

public static class Tournaments
{
    public static ImmutableArray<ScheduledMatch> Schedule(TournamentConfig config)
    {
        config.Rules.Validate();
        if (config.Bots.Length < 2) throw new ArgumentException("Choose at least two bots.");
        if (config.Bots.Distinct().Count() != config.Bots.Length) throw new ArgumentException("Tournament participants must be distinct versions/configurations.");
        if (config.Games < 1 || config.Games > 1000000 || config.Parallelism < 1 || config.MaximumRolls < 1)
            throw new ArgumentOutOfRangeException(nameof(config));
        var lineups = new List<ImmutableArray<BotSpec>>();
        if (config.Rules.PlayerCount == 2)
        {
            for (int a = 0; a < config.Bots.Length; a++)
                for (int b = a + 1; b < config.Bots.Length; b++) lineups.Add([config.Bots[a], config.Bots[b]]);
        }
        else
        {
            if (config.Bots.Length != 4) throw new ArgumentException("Four-player tournaments require four distinct participants.");
            lineups.Add(config.Bots);
        }
        int block = lineups.Count * config.Rules.PlayerCount;
        if (config.Games % block != 0) throw new ArgumentException($"Use a multiple of {block} games for complete balanced seat rotations.");
        var result = ImmutableArray.CreateBuilder<ScheduledMatch>();
        for (int cycle = 0; result.Count < config.Games; cycle++)
        {
            for (int pairing = 0; pairing < lineups.Count; pairing++)
            {
                int group = cycle * lineups.Count + pairing;
                uint seed = SeededRng.Derive(config.BaseSeed, (uint)group + 1);
                var lineup = lineups[pairing];
                for (int rotation = 0; rotation < lineup.Length; rotation++)
                {
                    var seats = Enumerable.Range(0, lineup.Length).Select(i => (BotSpec?)lineup[(i + rotation) % lineup.Length]).ToImmutableArray();
                    result.Add(new(result.Count, group, seed, seats));
                }
            }
        }
        return result.ToImmutable();
    }

    public static async Task<TournamentResult> RunAsync(TournamentConfig config, Func<IBotExecutor> executorFactory,
        IProgress<TournamentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var schedule = Schedule(config);
        var results = new MatchRecord[schedule.Length];
        int next = -1, completed = 0;
        var clock = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, Math.Min(config.Parallelism, schedule.Length)).Select(_ => Task.Run(async () =>
        {
            await using var executor = executorFactory();
            int index;
            while ((index = Interlocked.Increment(ref next)) < schedule.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = schedule[index];
                var session = new MatchSession(config.Rules, match.Seed, match.Seats, config.BudgetMilliseconds);
                while (!GameEngine.IsTerminal(session.State) && session.State.View.RollNumber < config.MaximumRolls)
                    await session.AdvanceAsync(executor, cancellationToken);
                results[index] = session.Snapshot(!GameEngine.IsTerminal(session.State));
                int count = Interlocked.Increment(ref completed);
                progress?.Report(new(count, schedule.Length, count / Math.Max(0.001, clock.Elapsed.TotalSeconds)));
            }
        }, cancellationToken));
        await Task.WhenAll(tasks);
        return new(config, results.ToImmutableArray(), clock.Elapsed.TotalSeconds);
    }

    public static (double Lower, double Upper) Wilson(int wins, int games)
    {
        if (games == 0) return (0, 1);
        if (wins < 0 || wins > games || games < 0) throw new ArgumentOutOfRangeException(nameof(wins));
        const double z = 1.959963984540054;
        double p = (double)wins / games, denominator = 1 + z * z / games;
        double center = (p + z * z / (2 * games)) / denominator;
        double radius = z * Math.Sqrt(p * (1 - p) / games + z * z / (4.0 * games * games)) / denominator;
        return (Math.Max(0, center - radius), Math.Min(1, center + radius));
    }

    // A conservative bound treats each mirrored seed group as one independent bounded observation.
    // This avoids treating correlated mirrored games as independent evidence for a small improvement.
    public static double PairedLower95(TournamentResult result, BotSpec bot)
    {
        int size = result.Config.Rules.PlayerCount;
        var observations = result.Games.Chunk(size).Where(group => group.Length == size && group.All(g => g.Result is not null && g.Seats.Contains(bot)))
            .Select(group => group.Average(g => g.Seats[g.Result!.FinishingOrder[0]] == bot ? 1.0 : 0.0)).ToArray();
        if (observations.Length == 0) return 0;
        return Math.Max(0, observations.Average() - Math.Sqrt(Math.Log(40) / (2 * observations.Length)));
    }

    public static ImmutableArray<BotStatistics> Statistics(TournamentResult result)
    {
        var ratings = result.Config.Bots.ToDictionary(b => b, _ => 1500.0);
        // Consume stable schedule order, never task completion order. Elo uses pairwise finishing comparisons.
        foreach (var game in result.Games.Where(g => g.Result is not null))
        {
            var order = game.Result!.FinishingOrder;
            var deltas = ratings.Keys.ToDictionary(b => b, _ => 0.0);
            for (int a = 0; a < order.Length; a++) for (int b = a + 1; b < order.Length; b++)
            {
                var winner = game.Seats[order[a]]!; var loser = game.Seats[order[b]]!;
                double expected = 1 / (1 + Math.Pow(10, (ratings[loser] - ratings[winner]) / 400));
                double change = 24.0 / (order.Length - 1) * (1 - expected);
                deltas[winner] += change; deltas[loser] -= change;
            }
            foreach (var entry in deltas) ratings[entry.Key] += entry.Value;
        }
        return result.Config.Bots.Select(bot =>
        {
            var entries = result.Games.Where(g => g.Result is not null && g.Seats.Contains(bot)).Select(g => (Game: g, Seat: g.Seats.IndexOf(bot))).ToArray();
            int games = entries.Length, wins = entries.Count(e => e.Game.Result!.FinishingOrder[0] == e.Seat);
            var interval = Wilson(wins, games);
            int moves = entries.Sum(e => e.Game.Metrics[e.Seat].Moves);
            return new BotStatistics(BotRegistry.Label(bot), games, wins, games == 0 ? 0 : (double)wins / games, interval.Lower, interval.Upper,
                games == 0 ? 0 : entries.Average(e => e.Game.Result!.FinishingOrder.IndexOf(e.Seat) + 1),
                games == 0 ? 0 : entries.Average(e => e.Game.Result!.Turns),
                games == 0 ? 0 : entries.Average(e => e.Game.Metrics[e.Seat].Captures),
                games == 0 ? 0 : entries.Average(e => e.Game.Metrics[e.Seat].Lost),
                moves == 0 ? 0 : entries.Sum(e => e.Game.Metrics[e.Seat].TotalMoveMilliseconds) / moves,
                result.Games.Sum(g => g.Incidents.Count(i => g.Seats[i.Player] == bot)), ratings[bot]);
        }).ToImmutableArray();
    }

    public static string Csv(TournamentResult result)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder("game,seed,seat,bot,place,turns,rolls,captures,lost,moves,move_ms,incidents,truncated,engine_version,rule_config,bot_config,budget_ms\n");
        for (int game = 0; game < result.Games.Length; game++)
        {
            var record = result.Games[game];
            for (int seat = 0; seat < record.Seats.Length; seat++)
            {
                var metric = record.Metrics[seat];
                text.AppendLine(string.Join(",", game, record.Seed, seat, record.Seats[seat]!.Key,
                    record.Result is null ? "" : record.Result.FinishingOrder.IndexOf(seat) + 1,
                    record.Result?.Turns, record.Result?.Rolls, metric.Captures, metric.Lost, metric.Moves,
                    metric.TotalMoveMilliseconds.ToString("F4", CultureInfo.InvariantCulture), record.Incidents.Count(i => i.Player == seat), record.Truncated,
                    record.EngineVersion, Quote(System.Text.Json.JsonSerializer.Serialize(record.Rules, RunnerJson.Options)),
                    Quote(System.Text.Json.JsonSerializer.Serialize(record.Seats[seat], RunnerJson.Options)), record.BudgetMilliseconds.ToString(CultureInfo.InvariantCulture)));
            }
        }
        return text.ToString();
    }
}

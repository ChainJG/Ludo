using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ludo.Bots;
using Ludo.Engine;
using Ludo.Native;
using Ludo.Presentation;
using Ludo.Runner;
using Ludo.Training;
using Microsoft.Win32;

namespace Ludo.Warzone;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<string> log = [];
    private readonly DispatcherTimer timer = new();
    private readonly ProcessBotExecutor executor = new(ProcessBotExecutor.DefaultHostPath);
    private readonly CancellationTokenSource lifetime = new();
    private MatchSession? session;
    private ReplayCursor? replay;
    private TournamentResult? results;
    private CancellationTokenSource? batchCancellation;
    private bool busy, playing, instant;
    private BotSpec? customBot;
    private GameState? Current => replay?.State ?? session?.State;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var box in new[] { Bot0, Bot1, Bot2, Bot3 }) { box.ItemsSource = BotRegistry.All; box.DisplayMemberPath = "Label"; }
        Bot0.SelectedIndex = 1; Bot1.SelectedIndex = 0; Bot2.SelectedIndex = 2; Bot3.SelectedIndex = 3;
        // Any deterministic released bot can teach; the neural bot itself and the random baseline are excluded.
        TeacherBox.ItemsSource = BotRegistry.All.Where(b => b.Key is not ("v1" or "v7")).ToArray(); TeacherBox.DisplayMemberPath = "Label";
        TeacherBox.SelectedItem = BotRegistry.All.First(b => b.Key == "v3");
        ParallelBox.Text = Math.Clamp(Environment.ProcessorCount - 1, 1, 8).ToString();
        MoveLog.ItemsSource = log;
        timer.Tick += async (_, _) => { if (playing && !busy) await Guard(async () => await StepAction()); };
        timer.Interval = TimeSpan.FromMilliseconds(600);
        Loaded += (_, _) => NewMatch();
        Closed += async (_, _) => { timer.Stop(); lifetime.Cancel(); batchCancellation?.Cancel(); await executor.DisposeAsync(); };
    }

    private RuleConfig Rules() => new()
    {
        PlayerCount = PlayerCountBox.SelectedIndex == 0 ? 2 : 4,
        LaunchRequiresSix = LaunchRule.IsChecked == true, ExtraRollOnSix = SixRule.IsChecked == true,
        ForfeitThirdSix = ThirdRule.IsChecked == true, ExtraRollOnCapture = CaptureRule.IsChecked == true,
        ExtraRollOnHome = HomeRule.IsChecked == true, SafeStars = StarsRule.IsChecked == true,
        ProtectStacks = StackRule.IsChecked == true, StacksBlockMovement = BlockingRule.IsChecked == true
    };
    private ImmutableArray<BotSpec> SelectedBots() => new[] { Bot0, Bot1, Bot2, Bot3 }.Take(Rules().PlayerCount)
        .Select((box, index) => index == 0 && customBot is not null ? customBot : new BotSpec(((BotDefinition)box.SelectedItem).Key)).ToImmutableArray();
    private double Budget() => double.Parse(BudgetBox.Text, CultureInfo.InvariantCulture);
    private void Pause() { playing = false; timer.Stop(); PlayButton.Content = "Play"; }
    private void NewMatch()
    {
        if (busy) return;
        Pause(); replay = null;
        session = new(Rules(), uint.Parse(SeedBox.Text), SelectedBots().Select(b => (BotSpec?)b).ToImmutableArray(), Budget());
        log.Clear(); Render(); StatusText.Text = "New match ready. Play or step through a turn.";
    }
    private void SetupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExtraSeats is not null) ExtraSeats.Visibility = PlayerCountBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (Seat1Label is not null) Seat1Label.Text = PlayerCountBox.SelectedIndex == 1 ? "Blue" : "Red";
    }
    private void BotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (customBot is not null && Bot0.SelectedItem is BotDefinition definition && definition.Key != customBot.Key)
        { customBot = null; CustomBotLabel.Text = ""; }
    }
    private async void LoadBotConfigClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Bot configuration JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        var loaded = JsonSerializer.Deserialize<BotSpec>(await File.ReadAllTextAsync(dialog.FileName), RunnerJson.Options) ?? throw new FormatException("Invalid bot configuration.");
        _ = BotRegistry.Create(loaded);
        Bot0.SelectedItem = BotRegistry.All.First(b => b.Key == loaded.Key);
        customBot = loaded;
        CustomBotLabel.Text = "Loaded configuration: " + BotRegistry.Label(loaded);
    });
    private async void NewMatchClick(object sender, RoutedEventArgs e) => await Guard(() => { NewMatch(); return Task.CompletedTask; });
    private void PlayClick(object sender, RoutedEventArgs e)
    {
        if (playing) { Pause(); return; }
        playing = true; PlayButton.Content = "Pause"; timer.Start();
    }
    private async Task StepAction()
    {
        if (busy || Current is null) return;
        if (GameEngine.IsTerminal(Current) || replay?.AtEnd == true) { Pause(); return; }
        busy = true;
        try
        {
            if (!instant && Current.View.Phase == GamePhase.AwaitingRoll && SpeedSlider.Value <= 3)
                await Board.RollAsync((int)(420 / SpeedSlider.Value), lifetime.Token);
            if (replay is not null) replay.Step();
            else await session!.AdvanceAsync(executor, lifetime.Token);
            string description = BoardPresentation.Describe(Current!.View);
            log.Add($"{Current.View.RollNumber:0000}  {description}");
            if (log.Count > 500) log.RemoveAt(0);
            Render();
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, 600 / SpeedSlider.Value));
            if (GameEngine.IsTerminal(Current) || replay?.AtEnd == true) Pause();
        }
        finally { busy = false; }
    }
    private async void StepClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        Pause(); if (busy || Current is null) return;
        int turn = Current.View.TurnNumber;
        do { await StepAction(); } while (Current.View.TurnNumber == turn && !GameEngine.IsTerminal(Current) && replay?.AtEnd != true);
    });
    private async void InstantClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        Pause();
        instant = true;
        try { while (!busy && Current is not null && !GameEngine.IsTerminal(Current) && replay?.AtEnd != true && Current.View.RollNumber < 20000) await StepAction(); }
        finally { instant = false; }
    });
    private void Render()
    {
        if (Current is null) return;
        var view = Current.View;
        Board.Update(view, (replay?.Record.Seats ?? session!.Seats).Select(b => b is null ? "Human" : BotRegistry.Label(b)).ToArray());
        TurnText.Text = GameEngine.IsTerminal(Current)
            ? string.Join("  ›  ", view.FinishingOrder.Select(p => BoardPresentation.Colours[view.Players[p].Seat]))
            : $"{BoardPresentation.Colours[view.Players[view.CurrentPlayer].Seat]} · turn {view.TurnNumber} · die {view.LastEvent.Die}";
        ChoiceText.Text = replay is not null ? $"Recorded replay · {replay.Position}/{replay.Record.Actions.Length} actions"
            : session?.LastChoice is { } choice ? string.Join("\n", choice.Candidates.Select(c => $"Token {c.Move.TokenId + 1}: {c.Score:F2}\n{c.Reason}"))
            : "Candidate scores appear after a bot chooses a move.";
        StatusText.Text = BoardPresentation.Describe(view);
        if (session?.Incidents.LastOrDefault() is { } incident && replay is null) StatusText.Text += " " + incident.Reason;
        if (log.Count > 0) MoveLog.ScrollIntoView(log[^1]);
    }
    private async void RunBatchClick(object sender, RoutedEventArgs e) => await Guard(() => RunBatch(false));
    private async void GateClick(object sender, RoutedEventArgs e) => await Guard(() => RunBatch(true));
    private async Task RunBatch(bool gate)
    {
        if (batchCancellation is not null) return;
        Pause();
        var rules = gate ? Rules() with { PlayerCount = 2 } : Rules();
        var bots = gate ? SelectedBots().Take(2).ToImmutableArray() : RoundRobinBox.IsChecked == true
            ? BotRegistry.All.Select(b => new BotSpec(b.Key)).ToImmutableArray() : SelectedBots();
        var requestedGames = gate ? 500 : int.Parse(GamesBox.Text);
        if (requestedGames <= 0) throw new ArgumentException("Choose a positive number of games.");
        var block = rules.PlayerCount == 2 ? bots.Length * (bots.Length - 1) : 4;
        var scheduledGames = checked((requestedGames + block - 1) / block * block);
        if (!gate) GamesBox.Text = scheduledGames.ToString();
        var config = new TournamentConfig(bots, rules, scheduledGames, uint.Parse(SeedBox.Text), int.Parse(ParallelBox.Text), Budget());
        batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        RunBatchButton.IsEnabled = false;
        try
        {
            var progress = new Progress<TournamentProgress>(p =>
            {
                if (p.Completed % 10 != 0 && p.Completed != p.Total) return;
                BatchProgress.Value = (double)p.Completed / p.Total;
                BatchStatus.Text = $"{p.Completed:N0} / {p.Total:N0} games · {p.GamesPerSecond:F1} games/s";
            });
            results = await Tournaments.RunAsync(config, () => new ProcessBotExecutor(ProcessBotExecutor.DefaultHostPath), progress, batchCancellation.Token);
            ShowResults();
            if (gate)
            {
                var candidate = Tournaments.Statistics(results)[0];
                bool pass = candidate.WinRate > 0.55 && candidate.Lower95 > 0.5 && Tournaments.PairedLower95(results, bots[0]) > 0.5
                    && results.Games.All(g => !g.Truncated && g.Incidents.IsEmpty);
                BatchStatus.Text += pass ? " · PASS" : " · FAIL";
            }
        }
        finally { batchCancellation.Dispose(); batchCancellation = null; RunBatchButton.IsEnabled = true; }
    }
    private void CancelBatchClick(object sender, RoutedEventArgs e) => batchCancellation?.Cancel();
    private void ShowResults()
    {
        if (results is null) return;
        var statistics = Tournaments.Statistics(results);
        ResultsGrid.ItemsSource = statistics.Select(s => new { s.Bot, s.Games, s.Wins, WinRate = s.WinRate.ToString("P1"),
            Wilson95 = $"{s.Lower95:P1}–{s.Upper95:P1}", Place = Math.Round(s.AveragePlace, 2), Turns = Math.Round(s.AverageTurns, 1),
            Captures = Math.Round(s.Captures, 2), Lost = Math.Round(s.Lost, 2), MoveMs = Math.Round(s.MoveMilliseconds, 3), s.Incidents, Elo = Math.Round(s.Elo) }).ToArray();
        BatchStatus.Text = $"{results.Games.Length:N0} games · {results.Games.Length / results.ElapsedSeconds:F1} games/s · incomplete {results.Games.Count(g => g.Truncated)}";
        var matrix = new List<string>();
        for (int a = 0; a < results.Config.Bots.Length; a++) for (int b = a + 1; b < results.Config.Bots.Length; b++)
        {
            var first = results.Config.Bots[a]; var second = results.Config.Bots[b];
            var matches = results.Games.Where(g => g.Result is not null && g.Seats.Contains(first) && g.Seats.Contains(second)).ToArray();
            int wins = matches.Count(g => g.Result!.FinishingOrder.IndexOf(g.Seats.IndexOf(first)) < g.Result.FinishingOrder.IndexOf(g.Seats.IndexOf(second)));
            matrix.Add($"{first.Key} / {second.Key}: {wins}–{matches.Length - wins}");
        }
        HeadToHeadText.Text = "Head-to-head finishing comparisons: " + string.Join("   ·   ", matrix);
        WinChart.Children.Clear();
        double width = Math.Max(500, WinChart.ActualWidth), slot = width / statistics.Length;
        var line = new System.Windows.Shapes.Polyline { Stroke = Brushes.White, StrokeThickness = 2 };
        for (int i = 0; i < statistics.Length; i++)
        {
            var row = statistics[i]; double height = row.WinRate * 85, x = slot * i + 24;
            var bar = new Rectangle { Width = slot - 48, Height = height, Fill = new SolidColorBrush(Color.FromRgb(57, 166, 245)), Opacity = 0.7 };
            Canvas.SetLeft(bar, x); Canvas.SetTop(bar, 100 - height); WinChart.Children.Add(bar);
            line.Points.Add(new Point(x + (slot - 48) / 2, 100 - height));
            var label = new TextBlock { Text = $"{results.Config.Bots[i].Key}  {row.WinRate:P1}", Foreground = Brushes.White };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, 106); WinChart.Children.Add(label);
        }
        WinChart.Children.Add(line);
    }
    private async void ExportClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (results is null) return;
        var dialog = new SaveFileDialog { Filter = "Tournament JSON|*.json", FileName = "ludo-results.json" };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(results, RunnerJson.Options));
        await File.WriteAllTextAsync(System.IO.Path.ChangeExtension(dialog.FileName, ".csv"), Tournaments.Csv(results));
        StatusText.Text = "Exported results, replay logs, and CSV.";
    });
    private void LoadReplay(MatchRecord record)
    { if (busy) return; Pause(); replay = new(record); log.Clear(); Tabs.SelectedIndex = 0; Render(); }
    private void ReplayLongestClick(object sender, RoutedEventArgs e)
    { if (results?.Games.OrderByDescending(g => g.Actions.Length).FirstOrDefault() is { } record) LoadReplay(record); }
    private void ReplayUpsetClick(object sender, RoutedEventArgs e)
    {
        if (results is null) return;
        var statistics = Tournaments.Statistics(results);
        var record = results.Games.Where(g => g.Result is not null).OrderBy(g => statistics[results.Config.Bots.IndexOf(g.Seats[g.Result!.FinishingOrder[0]]!)].WinRate).FirstOrDefault();
        if (record is not null) LoadReplay(record);
    }
    private async void OpenReplayClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Match replay JSON|*.json" };
        if (dialog.ShowDialog() == true) LoadReplay(JsonSerializer.Deserialize<MatchRecord>(await File.ReadAllTextAsync(dialog.FileName), RunnerJson.Options) ?? throw new FormatException("Invalid replay."));
    });
    private async void SaveReplayClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var record = replay?.Record ?? session?.Snapshot(); if (record is null) return;
        var dialog = new SaveFileDialog { Filter = "Match replay JSON|*.json", FileName = "ludo-match.json" };
        if (dialog.ShowDialog() == true) await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(record, RunnerJson.Options));
    });
    private async void TuneClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (batchCancellation is not null) return;
        var dialog = new SaveFileDialog { Filter = "Experimental weights JSON|*.json", FileName = "v3-experimental-weights.json" };
        if (dialog.ShowDialog() != true) return;
        Pause(); batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            uint seed = uint.Parse(SeedBox.Text); var rng = new SeededRng(seed); var best = new EvaluationWeights(); double bestRate = -1;
            for (int round = 0; round < 4; round++)
            {
                double Perturb(double value) => value * (0.7 + rng.NextInt(601) / 1000.0);
                var weights = round == 0 ? best : best with { Progress = Perturb(best.Progress), Safety = Perturb(best.Safety),
                    Capture = Perturb(best.Capture), Risk = Perturb(best.Risk), Stack = Perturb(best.Stack), Home = Perturb(best.Home), Launch = Perturb(best.Launch) };
                BatchStatus.Text = $"Tuning round {round + 1}/4 · 100 self-play games";
                var run = await Tournaments.RunAsync(new([new("v3", weights), new("v2")], Rules() with { PlayerCount = 2 }, 100,
                    SeededRng.Derive(seed, 10000), int.Parse(ParallelBox.Text), Budget()), () => new ProcessBotExecutor(ProcessBotExecutor.DefaultHostPath), cancellationToken: batchCancellation.Token);
                double rate = Tournaments.Statistics(run)[0].WinRate;
                if (rate > bestRate) { bestRate = rate; best = weights; }
                BatchProgress.Value = (round + 1) / 4.0;
            }
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(new BotSpec("v3", best), RunnerJson.Options));
            BatchStatus.Text = $"Experimental weights saved · training win rate {bestRate:P1}. Test on fresh seeds before promotion.";
        }
        finally { batchCancellation.Dispose(); batchCancellation = null; }
    });
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        catch (Exception exception) { Pause(); StatusText.Text = exception.Message; }
    }

    private async void TrainNeuralClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (batchCancellation is not null) return;
        var dialog = new SaveFileDialog { Filter = "Neural bot JSON|*.json", FileName = "my-neural-bot.json" };
        if (dialog.ShowDialog() != true) return;
        var initial = ContinueNetwork.IsChecked == true ? customBot?.Model ?? throw new ArgumentException("Load a neural bot configuration first, or turn off Continue loaded network.") : null;
        var config = new TrainingConfig(int.Parse(TrainingGames.Text), int.Parse(WarmupGames.Text), int.Parse(EvaluationEvery.Text),
            int.Parse(EvaluationGames.Text), uint.Parse(SeedBox.Text), double.Parse(LearningRate.Text, CultureInfo.InvariantCulture), Rules().PlayerCount,
            ((BotDefinition)TeacherBox.SelectedItem).Key, OpponentsBox.Text);
        Pause(); batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); TrainButton.IsEnabled = false;
        try
        {
            var progress = new Progress<TrainingProgress>(p => { NeuralProgress.Value = (double)p.Completed / p.Total; NeuralStatus.Text = $"{p.Completed:N0} / {p.Total:N0} · {p.Stage}" + (p.WinRate is { } rate ? $" · win rate {rate:P1}" : ""); });
            var result = await NeuralTrainer.TrainAsync(config, initial, progress, async state =>
            {
                await TrainingFiles.SaveAsync(dialog.FileName, state);
                await Dispatcher.InvokeAsync(() => TrainingHistory.ItemsSource = state.History.Select(p => new { p.Games, WinRate = p.WinRate.ToString("P1"), Wilson95 = $"{p.Lower95:P1}–{p.Upper95:P1}", p.EvaluationGames }).ToArray());
            }, batchCancellation.Token);
            Bot0.SelectedItem = BotRegistry.All.First(b => b.Key == "v7"); customBot = result.Best;
            CustomBotLabel.Text = "Loaded: " + BotRegistry.Label(customBot);
            NeuralStatus.Text = "Training complete. Best checkpoint loaded for Yellow. Run a fresh 500-game gate before promotion. Your exported bot also loads in Arena.";
        }
        finally { batchCancellation.Dispose(); batchCancellation = null; TrainButton.IsEnabled = true; }
    });
}

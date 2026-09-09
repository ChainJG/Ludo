using Ludo.Engine;
using Ludo.Native;
using Ludo.Runner;

namespace Ludo.Runner.Tests;

public class ProcessTests
{
    [Fact]
    public async Task HungBotIsTerminatedAndExecutorRecovers()
    {
        await using var executor = new ProcessBotExecutor(Path.Combine(AppContext.BaseDirectory, "fixture", "Ludo.HostFixture.dll"));
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 1).View, 6);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await executor.ExecuteAsync(new(new("v1"), view, 666), 50, cancellation.Token);
        Assert.Contains("watchdog", result.Error);
        var next = await executor.ExecuteAsync(new(new("v1"), view, 1), 50, cancellation.Token);
        Assert.Null(next.Error);
        Assert.Equal(new Move(0), next.Choice!.Move);
    }
    [Fact]
    public async Task CrashedProcessDoesNotPoisonNextRequest()
    {
        await using var executor = new ProcessBotExecutor(Path.Combine(AppContext.BaseDirectory, "fixture", "Ludo.HostFixture.dll"));
        var view = GameEngine.ResolveRoll(GameEngine.CreateGame(new(), 1).View, 6);
        await Assert.ThrowsAsync<IOException>(() => executor.ExecuteAsync(new(new("v1"), view, 999), 50, default).AsTask());
        var next = await executor.ExecuteAsync(new(new("v1"), view, 1), 50, default);
        Assert.NotNull(next.Choice);
    }
}

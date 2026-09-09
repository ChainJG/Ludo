using System.Diagnostics;
using System.Text.Json;
using Ludo.Engine;
using Ludo.Runner;

namespace Ludo.Native;

public sealed class ProcessBotExecutor(string hostPath) : IBotExecutor
{
    private Process? process;
    private Task<string>? errors;
    private readonly SemaphoreSlim gate = new(1, 1);

    public static string DefaultHostPath => Path.Combine(AppContext.BaseDirectory, "bot-host", "Ludo.BotHost.dll");

    public async ValueTask<BotExecution> ExecuteAsync(BotRequest request, double budgetMilliseconds, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ExecuteCore(request, budgetMilliseconds, cancellationToken); }
        finally { gate.Release(); }
    }

    private async ValueTask<BotExecution> ExecuteCore(BotRequest request, double budgetMilliseconds, CancellationToken cancellationToken)
    {
        await EnsureStarted(cancellationToken);
        try
        {
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, RunnerJson.Options).AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            string? started = await process.StandardOutput.ReadLineAsync(startup.Token);
            if (started != "started") throw new IOException("The bot worker failed before starting its decision.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Execution time is measured inside the warmed worker, excluding IPC. A watchdog also kills hung code.
            deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(250, budgetMilliseconds * 4)));
            string? response = await process.StandardOutput.ReadLineAsync(deadline.Token);
            return JsonSerializer.Deserialize<BotExecution>(response ?? throw new IOException("Bot worker exited."), RunnerJson.Options)
                ?? throw new IOException("Invalid bot response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await Stop();
            return new(null, budgetMilliseconds + 1, "Bot exceeded the execution watchdog and its process was terminated.");
        }
        catch
        {
            await Stop();
            throw;
        }
    }

    private async Task EnsureStarted(CancellationToken cancellationToken)
    {
        if (process is { HasExited: false }) return;
        await Stop();
        if (!File.Exists(hostPath)) throw new FileNotFoundException("Build the bot host before running matches.", hostPath);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(hostPath);
        process = Process.Start(start) ?? throw new IOException("Could not start the bot worker.");
        errors = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if (await process.StandardOutput.ReadLineAsync(timeout.Token) != "ready")
                throw new IOException("Bot worker initialization failed.");
        }
        catch { await Stop(); throw; }
    }

    private async ValueTask Stop()
    {
        if (process is null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        if (errors is not null) { try { await errors; } catch (OperationCanceledException) { } }
        process.Dispose(); process = null; errors = null;
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { await Stop(); }
        finally { gate.Release(); }
    }
}

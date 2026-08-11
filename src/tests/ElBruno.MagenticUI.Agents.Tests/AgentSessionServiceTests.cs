using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Orchestrator;
using ElBruno.MagenticUI.App;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class AgentSessionServiceTests
{
    [Fact]
    public async Task CancelTask_SetsCancellingState_BeforeReturningToIdle()
    {
        // Arrange
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddSingleton<IAgentOrchestrator>(new GatedCancellationOrchestrator(cancellationObserved));
        services.AddSingleton(new UserProxyAgent());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        using var provider = services.BuildServiceProvider();
        var session = new AgentSessionService(provider, configuration);

        // Act
        await session.StartTaskAsync("Run a long task");
        var running = await WaitForStatusAsync(session, AgentTaskStatus.Running, TimeSpan.FromSeconds(10));
        Assert.True(running, $"Expected Running but observed {session.Status}.");

        session.CancelTask();

        // Assert
        Assert.Equal(AgentTaskStatus.Cancelling, session.Status);
        cancellationObserved.SetResult();
        var completed = await WaitForStatusAsync(session, AgentTaskStatus.Idle, TimeSpan.FromSeconds(30));
        Assert.True(completed, $"Expected Idle but observed {session.Status} (error: {session.LastError ?? "none"}).");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task StartTaskAsync_StopsWithTimeout_WhenConfigured()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IAgentOrchestrator, TimeoutOrchestrator>();
        services.AddSingleton(new UserProxyAgent());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalLLMs:TaskTimeoutSeconds"] = "1"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        using var provider = services.BuildServiceProvider();
        var session = new AgentSessionService(provider, configuration);

        // Act
        await session.StartTaskAsync("Run a long task");

        // Assert
        var completed = await WaitForStatusAsync(session, AgentTaskStatus.Error, TimeSpan.FromSeconds(30));

        Assert.True(completed, $"Expected Error but observed {session.Status}.");
        Assert.Equal(AgentTaskStatus.Error, session.Status);
        Assert.Contains("timed out", session.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        await session.DisposeAsync();
    }

    private static async Task<bool> WaitForStatusAsync(
        AgentSessionService session,
        AgentTaskStatus expected,
        TimeSpan timeout)
    {
        // Polls asynchronously instead of blocking a thread-pool thread, which otherwise
        // starves the background orchestrator task when the whole suite runs in parallel.
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (session.Status == expected)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return session.Status == expected;
    }

    private sealed class GatedCancellationOrchestrator(TaskCompletionSource releaseAfterCancellation) : IAgentOrchestrator
    {
        public async Task RunAsync(
            TaskRequest request,
            IProgress<AgentMessage> progress,
            CancellationToken ct = default)
        {
            progress.Report(new AgentMessage("Orchestrator", "system", request.Prompt, 0, DateTimeOffset.UtcNow));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                // Keep the run alive until the test has observed the Cancelling state,
                // so the assertion cannot race the transition back to Idle. Awaiting
                // (rather than blocking) keeps the thread pool free while other tests run.
                await releaseAfterCancellation.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                throw;
            }
        }
    }

    private sealed class TimeoutOrchestrator : IAgentOrchestrator
    {
        public async Task RunAsync(
            TaskRequest request,
            IProgress<AgentMessage> progress,
            CancellationToken ct = default)
        {
            progress.Report(new AgentMessage("Orchestrator", "system", request.Prompt, 0, DateTimeOffset.UtcNow));
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }
}

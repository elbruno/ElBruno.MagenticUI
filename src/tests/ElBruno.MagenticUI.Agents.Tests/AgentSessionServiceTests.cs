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
        using var cancellationObserved = new ManualResetEventSlim(false);
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
        var running = SpinWait.SpinUntil(() => session.Status == AgentTaskStatus.Running, TimeSpan.FromSeconds(2));
        Assert.True(running);

        session.CancelTask();

        // Assert
        Assert.Equal(AgentTaskStatus.Cancelling, session.Status);
        cancellationObserved.Set();
        var completed = SpinWait.SpinUntil(() => session.Status == AgentTaskStatus.Idle, TimeSpan.FromSeconds(5));
        Assert.True(completed);
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
        var completed = SpinWait.SpinUntil(
            () => session.Status is AgentTaskStatus.Error,
            TimeSpan.FromSeconds(5));

        Assert.True(completed);
        Assert.Equal(AgentTaskStatus.Error, session.Status);
        Assert.Contains("timed out", session.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        await session.DisposeAsync();
    }

    private sealed class GatedCancellationOrchestrator(ManualResetEventSlim releaseAfterCancellation) : IAgentOrchestrator
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
                // so the assertion cannot race the transition back to Idle.
                releaseAfterCancellation.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
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

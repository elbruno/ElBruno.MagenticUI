using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Orchestrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ElBruno.MagenticUI.App;

public enum AgentTaskStatus { Idle, Running, Cancelling, WaitingForInput, Done, Error }

public sealed class AgentSessionService : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private UserProxyAgent? _userProxy;
    private readonly IConfiguration _configuration;

    private readonly List<AgentMessage> _messages = [];
    private readonly object _messagesLock = new();
    private CancellationTokenSource? _cts;

    public AgentTaskStatus Status { get; private set; } = AgentTaskStatus.Idle;
    public string? PendingQuestion { get; private set; }
    public string? LastError { get; private set; }

    public IReadOnlyList<AgentMessage> Messages
    {
        get { lock (_messagesLock) { return [.._messages]; } }
    }

    public event Func<Task>? OnChanged;

    public AgentSessionService(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public Task StartTaskAsync(string prompt, string? workingDir = null)
    {
        if (Status is AgentTaskStatus.Running or AgentTaskStatus.WaitingForInput)
            return Task.CompletedTask;

        lock (_messagesLock) { _messages.Clear(); }
        Status = AgentTaskStatus.Running;
        PendingQuestion = null;
        LastError = null;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var request = new TaskRequest(Guid.NewGuid().ToString("N"), prompt, workingDir);
        var progress = new Progress<AgentMessage>(msg =>
        {
            lock (_messagesLock)
            {
                StoreMessage(msg);
            }
            if (msg.Role == "input_request")
            {
                Status = AgentTaskStatus.WaitingForInput;
                PendingQuestion = msg.Text;
            }
            _ = NotifyChanged();
        });

        var ct = _cts.Token;
        var timeoutSeconds = Math.Max(0, _configuration.GetValue("LocalLLMs:TaskTimeoutSeconds", 0));
        _ = Task.Run(async () =>
        {
            using var timeoutCts = timeoutSeconds > 0
                ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                : null;
            using var linkedCts = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                var orchestrator = _serviceProvider.GetRequiredService<IAgentOrchestrator>();
                _userProxy ??= _serviceProvider.GetRequiredService<UserProxyAgent>();
                await orchestrator.RunAsync(request, progress, linkedCts.Token);
                Status = AgentTaskStatus.Done;
            }
            catch (OperationCanceledException)
            {
                if (timeoutCts?.IsCancellationRequested == true)
                {
                    LastError = $"Task timed out after {timeoutSeconds} seconds.";
                    Status = AgentTaskStatus.Error;
                }
                else
                {
                    LastError = null;
                    Status = AgentTaskStatus.Idle;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Status = AgentTaskStatus.Error;
            }
            finally
            {
                PendingQuestion = null;
                await NotifyChanged();
            }
        }, ct);

        return NotifyChanged();
    }

    public Task RespondToInputAsync(string response)
    {
        if (Status != AgentTaskStatus.WaitingForInput) return Task.CompletedTask;
        Status = AgentTaskStatus.Running;
        PendingQuestion = null;
        _userProxy?.SetResponse(response);
        return NotifyChanged();
    }

    public void CancelTask()
    {
        if (Status is not (AgentTaskStatus.Running or AgentTaskStatus.WaitingForInput))
            return;

        Status = AgentTaskStatus.Cancelling;
        _cts?.Cancel();
        _ = NotifyChanged();
    }

    private void StoreMessage(AgentMessage msg)
    {
        if (msg.Role.EndsWith("_stream", StringComparison.Ordinal))
        {
            var existingIndex = _messages.FindLastIndex(existing =>
                existing.AgentName == msg.AgentName &&
                existing.Round == msg.Round &&
                existing.Role == msg.Role);

            if (existingIndex >= 0)
            {
                _messages[existingIndex] = msg;
                return;
            }
        }
        else
        {
            _messages.RemoveAll(existing =>
                existing.AgentName == msg.AgentName &&
                existing.Round == msg.Round &&
                existing.Role.EndsWith("_stream", StringComparison.Ordinal));
        }

        _messages.Add(msg);
    }

    private Task NotifyChanged()
    {
        var handler = OnChanged;
        return handler is not null ? handler() : Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is { } cts)
        {
            await cts.CancelAsync();
            cts.Dispose();
            _cts = null;
        }
    }
}

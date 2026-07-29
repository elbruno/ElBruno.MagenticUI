using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Orchestrator;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.MagenticUI.App;

public enum AgentTaskStatus { Idle, Running, WaitingForInput, Done, Error }

public sealed class AgentSessionService : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private MagenticUIOrchestrator? _orchestrator;
    private UserProxyAgent? _userProxy;

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

    public AgentSessionService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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
            lock (_messagesLock) { _messages.Add(msg); }
            if (msg.Role == "input_request")
            {
                Status = AgentTaskStatus.WaitingForInput;
                PendingQuestion = msg.Text;
            }
            _ = NotifyChanged();
        });

        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var orchestrator = _orchestrator ??= _serviceProvider.GetRequiredService<MagenticUIOrchestrator>();
                _userProxy ??= _serviceProvider.GetRequiredService<UserProxyAgent>();
                await orchestrator.RunAsync(request, progress, ct);
                Status = AgentTaskStatus.Done;
            }
            catch (OperationCanceledException)
            {
                Status = AgentTaskStatus.Idle;
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

    public void CancelTask() => _cts?.Cancel();

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

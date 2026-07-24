using ElBruno.MagenticUI.Agents.Models;

namespace ElBruno.MagenticUI.Agents.Agents;

public sealed class UserProxyAgent
{
    private const string AgentName = "UserProxy";
    private TaskCompletionSource<string>? _pending;

    public async Task<string> ExecuteAsync(
        string clarificationRequest,
        IProgress<AgentMessage> progress,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        _pending = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var reg = ct.Register(() => _pending.TrySetCanceled(ct));

        progress.Report(new AgentMessage(
            AgentName, "input_request", clarificationRequest, Round: 0, DateTimeOffset.UtcNow));

        try
        {
            return await _pending.Task;
        }
        catch (OperationCanceledException)
        {
            return "[Cancelled]";
        }
    }

    public void SetResponse(string response) =>
        _pending?.TrySetResult(response);
}

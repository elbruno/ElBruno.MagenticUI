using ElBruno.MagenticUI.Agents.Models;

namespace ElBruno.MagenticUI.Agents.Orchestrator;

public interface IAgentOrchestrator
{
    Task RunAsync(
        TaskRequest request,
        IProgress<AgentMessage> progress,
        CancellationToken ct = default);
}

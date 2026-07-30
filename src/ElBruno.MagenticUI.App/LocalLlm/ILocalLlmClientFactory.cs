using ElBruno.LocalLLMs;
using Microsoft.Extensions.AI;

namespace ElBruno.MagenticUI.App.LocalLlm;

public interface ILocalLlmClientFactory
{
    IChatClient CreateOrchestratorChatClient();
    Task<LocalVisionChatClient> CreateComputerUseChatClientAsync(CancellationToken cancellationToken = default);
}

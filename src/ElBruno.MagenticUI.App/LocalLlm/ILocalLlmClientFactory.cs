using ElBruno.LocalLLMs;
using Microsoft.Extensions.AI;

namespace ElBruno.MagenticUI.App.LocalLlm;

public interface ILocalLlmClientFactory
{
    IChatClient CreateOrchestratorChatClient();
    LocalVisionChatClient CreateComputerUseChatClient();
}

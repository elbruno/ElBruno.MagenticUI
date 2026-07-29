using ElBruno.LocalLLMs;
using ElBruno.MagenticUI.App.ModelDownloadProgress;
using ElBruno.MagenticUI.App.ModelSettings;
using Microsoft.Extensions.AI;

namespace ElBruno.MagenticUI.App.LocalLlm;

public sealed class LocalLlmClientFactory : ILocalLlmClientFactory
{
    private readonly IModelSettingsService _modelSettingsService;
    private readonly IModelDownloadProgressStateService _progressStateService;
    private readonly ILoggerFactory _loggerFactory;

    public LocalLlmClientFactory(
        IModelSettingsService modelSettingsService,
        IModelDownloadProgressStateService progressStateService,
        ILoggerFactory loggerFactory)
    {
        _modelSettingsService = modelSettingsService;
        _progressStateService = progressStateService;
        _loggerFactory = loggerFactory;
    }

    public IChatClient CreateOrchestratorChatClient()
        => CreateClient(
            ModelRole.Orchestrator,
            (options, progress, cancellationToken) => LocalChatClient
                .CreateAsync(options, progress, _loggerFactory, cancellationToken)
                .GetAwaiter()
                .GetResult());

    public LocalVisionChatClient CreateComputerUseChatClient()
        => CreateClient(
            ModelRole.ComputerUse,
            (options, progress, cancellationToken) => LocalVisionChatClient
                .CreateAsync(options, progress, cancellationToken)
                .GetAwaiter()
                .GetResult());

    private TClient CreateClient<TClient>(
        ModelRole role,
        Func<LocalLLMsOptions, IProgress<ElBruno.LocalLLMs.ModelDownloadProgress>, CancellationToken, TClient> createClient)
    {
        var modelEntry = _modelSettingsService.GetModelEntry(role);
        _progressStateService.Initialize(modelEntry.Role, modelEntry.ModelId);

        var options = _modelSettingsService.BuildLocalLlmOptions(role);
        var logger = _loggerFactory.CreateLogger<LocalLlmClientFactory>();

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var client = createClient(
                    options,
                    _progressStateService.CreateProgressReporter(modelEntry.Role, modelEntry.ModelId),
                    CancellationToken.None);

                _progressStateService.MarkCompleted(modelEntry.Role, modelEntry.ModelId);
                return client;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientOnnxFailure(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(
                    "Model load attempt {Attempt}/{Max} failed for {Role} ({ModelId}): {Message}. Retrying in {Delay}s…",
                    attempt, maxAttempts, role, modelEntry.ModelId, ex.Message, delay.TotalSeconds);
                Thread.Sleep(delay);
            }
            catch (Exception ex)
            {
                _progressStateService.MarkFailed(modelEntry.Role, modelEntry.ModelId, ex.Message);
                throw;
            }
        }

        // Should be unreachable — loop always returns or throws
        throw new InvalidOperationException($"Model load for {role} exhausted all retry attempts.");
    }

    private static bool IsTransientOnnxFailure(Exception ex)
        => ex.Message.Contains("bad allocation", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("OnnxRuntimeGenAI", StringComparison.OrdinalIgnoreCase)
        || ex is OutOfMemoryException;
}

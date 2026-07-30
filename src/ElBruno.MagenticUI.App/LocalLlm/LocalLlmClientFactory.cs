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
    private readonly SemaphoreSlim _computerUseClientLock = new(1, 1);
    private LocalVisionChatClient? _computerUseClient;

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
        => CreateClientAsync(
            ModelRole.Orchestrator,
            (options, progress, cancellationToken) => LocalChatClient
                .CreateAsync(options, progress, _loggerFactory, cancellationToken),
            CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public async Task<LocalVisionChatClient> CreateComputerUseChatClientAsync(CancellationToken cancellationToken = default)
    {
        if (_computerUseClient is not null)
            return _computerUseClient;

        await _computerUseClientLock.WaitAsync(cancellationToken);
        try
        {
            if (_computerUseClient is not null)
                return _computerUseClient;

            _computerUseClient = await CreateClientAsync(
                ModelRole.ComputerUse,
                (options, progress, ct) => LocalVisionChatClient.CreateAsync(options, progress, ct),
                cancellationToken);

            return _computerUseClient;
        }
        finally
        {
            _computerUseClientLock.Release();
        }
    }

    private async Task<TClient> CreateClientAsync<TClient>(
        ModelRole role,
        Func<LocalLLMsOptions, IProgress<ElBruno.LocalLLMs.ModelDownloadProgress>, CancellationToken, Task<TClient>> createClient,
        CancellationToken cancellationToken)
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
                var client = await createClient(
                    options,
                    _progressStateService.CreateProgressReporter(modelEntry.Role, modelEntry.ModelId),
                    cancellationToken);

                _progressStateService.MarkCompleted(modelEntry.Role, modelEntry.ModelId);
                return client;
            }
            catch (OperationCanceledException)
            {
                _progressStateService.MarkFailed(modelEntry.Role, modelEntry.ModelId, "Initialization cancelled.");
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientOnnxFailure(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(
                    "Model load attempt {Attempt}/{Max} failed for {Role} ({ModelId}): {Message}. Retrying in {Delay}s…",
                    attempt, maxAttempts, role, modelEntry.ModelId, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
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

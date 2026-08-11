using ElBruno.LocalLLMs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.Agents.Configuration;

/// <summary>
/// Registers the optional Fara vision client without replacing the text IChatClient.
/// </summary>
public static class FaraVisionServiceExtensions
{
    public const string ServiceKey = "fara";

    public static IServiceCollection AddFaraVisionLLM(
        this IServiceCollection services,
        FaraVisionOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var modelPath = NormalizePath(configuration.ModelPath);
        var cacheDirectory = NormalizePath(configuration.CacheDirectory);
        var requestedProvider = ParseExecutionProvider(configuration.ExecutionProvider);

        // ElBruno.LocalLLMs 0.20.11 preflights accelerated providers and falls back safely, so
        // the plan is only used to explain the choice in the UI before the first prediction.
        var providerPlan = ExecutionProviderPlanner.Plan(requestedProvider, cacheDirectory);

        var options = new LocalLLMsOptions
        {
            Model = KnownModels.Fara15_9B,
            ModelPath = modelPath,
            CacheDirectory = cacheDirectory,
            EnsureModelDownloaded = configuration.EnsureModelDownloaded,
            MaxSequenceLength = configuration.MaxSequenceLength,
            Temperature = configuration.Temperature,
            TopP = configuration.TopP,
            GpuDeviceId = configuration.GpuDeviceId,
            ExecutionProvider = providerPlan.Effective
        };

        services.AddKeyedSingleton<LocalLLMsOptions>(ServiceKey, options);
        services.AddKeyedSingleton(ServiceKey, configuration);
        services.AddKeyedSingleton(ServiceKey, providerPlan);
        services.AddKeyedSingleton<LocalVisionChatClient>(ServiceKey, (sp, _) =>
            new LocalVisionChatClient(
                sp.GetRequiredKeyedService<LocalLLMsOptions>(ServiceKey),
                sp.GetService<ILoggerFactory>()));
        services.AddKeyedSingleton<IChatClient>(ServiceKey, (sp, _) =>
            sp.GetRequiredKeyedService<LocalVisionChatClient>(ServiceKey));

        return services;
    }

    /// <summary>
    /// Treats empty/whitespace configuration values as "not set". An empty string is not
    /// equivalent to <see langword="null"/> for <see cref="LocalLLMsOptions.CacheDirectory"/>:
    /// the downloader combines it with the model id, producing a *relative* cache directory
    /// next to the running app, so the multi-gigabyte model is re-downloaded on every request
    /// instead of being found in the shared local cache.
    /// </summary>
    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static ExecutionProvider ParseExecutionProvider(string value) =>
        Enum.TryParse<ExecutionProvider>(value, ignoreCase: true, out var provider)
            ? provider
            : ExecutionProvider.Auto;
}

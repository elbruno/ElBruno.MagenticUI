using ElBruno.LocalLLMs;
using ElBruno.LocalLLMs.Diagnostics;

namespace ElBruno.MagenticUI.Agents.Configuration;

/// <summary>
/// The execution provider the app asked for, the one it will actually use, and a
/// human-readable explanation of the difference.
/// </summary>
/// <param name="Requested">Provider parsed from configuration.</param>
/// <param name="Effective">Provider handed to ElBruno.LocalLLMs.</param>
/// <param name="Fallback"><see langword="true"/> when the requested provider was unusable.</param>
/// <param name="Detail">Explanation suitable for display in the UI.</param>
public sealed record ExecutionProviderPlan(
    ExecutionProvider Requested,
    ExecutionProvider Effective,
    bool Fallback,
    string Detail);

/// <summary>
/// Chooses the execution provider for the Fara vision client using the preflight
/// diagnostics published by ElBruno.LocalLLMs.
/// </summary>
/// <remarks>
/// ElBruno.LocalLLMs 0.20.11 preflights every accelerated provider and degrades to a
/// working one instead of letting the native load fail, so the app no longer probes for
/// CUDA/cuDNN itself. It still reads the diagnostics up front so the UI can explain why
/// a GPU is or is not being used before the first (multi-minute) prediction is started.
/// </remarks>
public static class ExecutionProviderPlanner
{
    public static ExecutionProviderPlan Plan(ExecutionProvider requested, string? cacheDirectory)
    {
        EnvironmentDiagnostics diagnostics;
        try
        {
            diagnostics = LocalChatClient.DiagnoseEnvironment(cacheDirectory);
        }
        catch (Exception ex)
        {
            // Diagnostics are advisory. Never let them stop the app from starting.
            return new ExecutionProviderPlan(
                requested,
                requested,
                Fallback: false,
                $"Execution provider diagnostics are unavailable ({ex.Message}). Using {requested}.");
        }

        return Plan(requested, diagnostics);
    }

    internal static ExecutionProviderPlan Plan(ExecutionProvider requested, EnvironmentDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (requested is ExecutionProvider.Auto)
        {
            var resolved = diagnostics.AutoResolvedExecutionProviderKnown
                ? diagnostics.AutoResolvedExecutionProvider
                : ExecutionProvider.Auto;

            var autoDetail = diagnostics.AutoResolvedExecutionProviderKnown
                ? $"Auto-selected the {resolved} execution provider. " +
                  (diagnostics.AutoResolvedExecutionDetails ?? string.Empty).Trim()
                : "Auto execution provider selection is resolved by ElBruno.LocalLLMs at model load time.";

            return new ExecutionProviderPlan(requested, resolved, Fallback: false, autoDetail.Trim());
        }

        var diagnostic = Find(diagnostics, requested);

        if (diagnostic is null)
        {
            return new ExecutionProviderPlan(
                requested,
                requested,
                Fallback: false,
                $"No preflight diagnostic was published for {requested}; using it as configured.");
        }

        if (diagnostic.Status is not ExecutionProviderDiagnosticStatus.Unavailable)
        {
            var okDetail = Describe(diagnostic)
                ?? $"The {requested} execution provider passed preflight checks.";
            return new ExecutionProviderPlan(requested, requested, Fallback: false, okDetail);
        }

        // Fall back to Auto rather than a hard-coded CPU: the library preflights the whole
        // candidate list and picks the best provider that actually loads.
        var reason = Describe(diagnostic) ?? "the provider failed its preflight check";
        return new ExecutionProviderPlan(
            requested,
            ExecutionProvider.Auto,
            Fallback: true,
            $"The {requested} execution provider is unavailable, so provider selection fell back to Auto. {reason}");
    }

    private static ExecutionProviderDiagnostic? Find(
        EnvironmentDiagnostics diagnostics,
        ExecutionProvider provider) =>
        diagnostics.ProviderDiagnostics?.FirstOrDefault(d => d.Provider == provider);

    private static string? Describe(ExecutionProviderDiagnostic diagnostic)
    {
        var parts = new[] { diagnostic.Reason, diagnostic.Suggestion }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        var text = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

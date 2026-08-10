using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.Agents.Configuration;

/// <summary>
/// Result of probing the machine for the native CUDA/cuDNN libraries that the
/// ONNX Runtime CUDA execution provider loads at runtime.
/// </summary>
/// <param name="Available">True when a directory containing every required library was found.</param>
/// <param name="Directory">The directory that was added to the native search path, when found.</param>
/// <param name="MissingLibraries">Libraries that could not be located, when <paramref name="Available"/> is false.</param>
/// <param name="Detail">Human-readable explanation suitable for logs and diagnostics UI.</param>
public sealed record CudaRuntimeStatus(
    bool Available,
    string? Directory,
    IReadOnlyList<string> MissingLibraries,
    string Detail);

/// <summary>
/// Locates the CUDA 13 / cuDNN 9 native libraries required by the ONNX Runtime 1.28 CUDA
/// execution provider and makes them resolvable by the OS loader.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Microsoft.ML.OnnxRuntime.Gpu</c> package ships <c>onnxruntime_providers_cuda.dll</c>
/// but deliberately does not redistribute the NVIDIA CUDA runtime or cuDNN. Those must already
/// exist on the machine (CUDA Toolkit, cuDNN, or another product that bundles them).
/// </para>
/// <para>
/// ONNX Runtime 1.28 — the version used by ONNX Runtime GenAI 0.15.x — links against the
/// <b>CUDA 13</b> runtime (<c>cublasLt64_13.dll</c>), which in turn needs an NVIDIA driver of
/// r580 or newer. A CUDA 12 installation is not sufficient: the provider DLL fails to load and
/// ONNX Runtime GenAI crashes the process with an access violation instead of reporting an
/// error, so the provider must never be requested unless a complete CUDA 13 set is present.
/// </para>
/// </remarks>
public static class CudaRuntimeResolver
{
    /// <summary>Native libraries the CUDA execution provider needs beyond the NVIDIA driver.</summary>
    private static readonly string[] RequiredLibraries =
    [
        "cudart64_13.dll",
        "cublas64_13.dll",
        "cublasLt64_13.dll",
        "cudnn64_9.dll"
    ];

    private static readonly object SyncRoot = new();
    private static CudaRuntimeStatus? _cached;

    /// <summary>
    /// Ensures the CUDA native dependencies are resolvable by the OS loader, probing the
    /// configured directory first and then well-known install locations.
    /// </summary>
    /// <param name="configuredDirectory">
    /// Optional explicit directory (from configuration). Takes precedence over probing.
    /// </param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The resolution result. Never throws — callers fall back to CPU.</returns>
    public static CudaRuntimeStatus EnsureAvailable(string? configuredDirectory, ILogger? logger = null)
    {
        lock (SyncRoot)
        {
            if (_cached is not null)
                return _cached;

            _cached = Resolve(configuredDirectory, logger);
            return _cached;
        }
    }

    private static CudaRuntimeStatus Resolve(string? configuredDirectory, ILogger? logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Linux the loader uses LD_LIBRARY_PATH, which cannot be set usefully from
            // inside the process. Assume the environment is configured correctly.
            return new CudaRuntimeStatus(true, null, [], "Non-Windows platform; relying on the system loader configuration.");
        }

        foreach (var candidate in EnumerateCandidateDirectories(configuredDirectory))
        {
            var missing = RequiredLibraries
                .Where(lib => !File.Exists(Path.Combine(candidate, lib)))
                .ToArray();

            if (missing.Length > 0)
                continue;

            if (!TryAddToNativeSearchPath(candidate, logger))
                continue;

            logger?.LogInformation("CUDA native dependencies resolved from {Directory}.", candidate);
            return new CudaRuntimeStatus(true, candidate, [], $"CUDA runtime libraries loaded from '{candidate}'.");
        }

        var detail =
            "CUDA runtime libraries were not found. ONNX Runtime 1.28's CUDA provider requires " +
            $"{string.Join(", ", RequiredLibraries)} (CUDA 13 + cuDNN 9, NVIDIA driver r580 or newer). " +
            "Install the NVIDIA CUDA 13 Toolkit and cuDNN 9, or set " +
            "'LocalLLMs:FaraVision:CudaDependencyPath' to a directory that contains them. " +
            "Inference will run on CPU, which is slow for a 9B vision model.";

        logger?.LogWarning("{Detail}", detail);
        return new CudaRuntimeStatus(false, null, RequiredLibraries, detail);
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            yield return Path.GetFullPath(configuredDirectory.Trim());

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Products that redistribute a complete ONNX Runtime CUDA dependency set.
        if (!string.IsNullOrEmpty(userProfile))
        {
            var aitkRoot = Path.Combine(userProfile, ".aitk", "bin", "libonnxruntime_cuda_windows");
            if (Directory.Exists(aitkRoot))
            {
                // Prefer the highest versioned folder.
                foreach (var dir in Directory.EnumerateDirectories(aitkRoot).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                    yield return dir;
            }
        }

        if (!string.IsNullOrEmpty(localAppData))
        {
            var ollamaRoot = Path.Combine(localAppData, "Programs", "Ollama", "lib", "ollama");
            foreach (var name in new[] { "cuda_v13", "cuda_v12" })
            {
                var dir = Path.Combine(ollamaRoot, name);
                if (Directory.Exists(dir))
                    yield return dir;
            }
        }

        // Standard NVIDIA CUDA Toolkit installs.
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrWhiteSpace(cudaPath))
            yield return Path.Combine(cudaPath, "bin");

        var toolkitRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA GPU Computing Toolkit", "CUDA");
        if (Directory.Exists(toolkitRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(toolkitRoot).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                yield return Path.Combine(dir, "bin");
        }
    }

    /// <summary>
    /// Registers the directory with the OS loader. <c>AddDllDirectory</c> affects native
    /// dependencies resolved by <c>onnxruntime_providers_cuda.dll</c>; PATH is also updated
    /// because ONNX Runtime probes it for the provider's transitive dependencies.
    /// </summary>
    private static bool TryAddToNativeSearchPath(string directory, ILogger? logger)
    {
        try
        {
            if (!Directory.Exists(directory))
                return false;

            SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs);
            if (AddDllDirectory(directory) == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger?.LogDebug("AddDllDirectory failed for {Directory} (Win32 error {Error}).", directory, error);
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Split(Path.PathSeparator).Any(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
                Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + path);

            return true;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to add {Directory} to the native search path.", directory);
            return false;
        }
    }

    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}

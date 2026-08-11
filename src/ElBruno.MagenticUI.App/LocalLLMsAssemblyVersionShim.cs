using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace ElBruno.MagenticUI.App;

/// <summary>
/// Temporary shim for a packaging defect in <c>ElBruno.LocalLLMs</c> 0.20.11.
/// </summary>
/// <remarks>
/// The 0.20.11 NuGet package ships <c>ElBruno.LocalLLMs.dll</c> stamped with
/// <c>AssemblyVersion 0.20.9.0</c>, while <c>ElBruno.LocalLLMs.BlazorComponents</c> and
/// <c>ElBruno.LocalLLMs.Rag</c> 0.20.11 were compiled against <c>0.20.11.0</c>. .NET rolls
/// assembly references *forward* but never backward, so the reference fails to bind and the
/// host dies at startup with:
/// <code>
/// System.IO.FileNotFoundException: Could not load file or assembly
/// 'ElBruno.LocalLLMs, Version=0.20.11.0, Culture=neutral, PublicKeyToken=null'
/// </code>
/// The resolver below satisfies the request with the copy that is actually in the output
/// folder. Delete this file once the upstream package stamps a matching assembly version —
/// tracked in elbruno/ElBruno.LocalLLMs.
/// </remarks>
internal static class LocalLLMsAssemblyVersionShim
{
    private const string AssemblyName = "ElBruno.LocalLLMs";

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Must run before Main is JIT-compiled, otherwise the failing bind happens first.
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName requested)
    {
        if (!string.Equals(requested.Name, AssemblyName, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = Path.Combine(AppContext.BaseDirectory, $"{AssemblyName}.dll");
        if (!File.Exists(path))
            return null;

        // Already loaded (under its real version) if a previous resolve succeeded.
        var loaded = context.Assemblies.FirstOrDefault(
            a => string.Equals(a.GetName().Name, AssemblyName, StringComparison.OrdinalIgnoreCase));

        return loaded ?? context.LoadFromAssemblyPath(path);
    }
}

using ElBruno.LocalLLMs;
using ElBruno.LocalLLMs.Diagnostics;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Orchestrator;
using ElBruno.MagenticUI.Agents.Tools;
using ElBruno.MagenticUI.App;
using ElBruno.MagenticUI.App.Components;
using Microsoft.Extensions.AI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

const string genAiActivitySourceName = "ElBruno.MagenticUI.GenAI";
const string computerUseActivitySourceName = "ElBruno.MagenticUI.ComputerUse";

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(
        genAiActivitySourceName,
        computerUseActivitySourceName,
        LocalLLMsInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(LocalLLMsInstrumentation.MeterName));

// ── Local LLM (ONNX via ElBruno.LocalLLMs → IChatClient) ──────────────────
var executionProvider = builder.Configuration.GetValue(
    "LocalLLMs:ExecutionProvider",
    ExecutionProvider.Cpu);
var cacheDirectory = builder.Configuration["LocalLLMs:CacheDirectory"];
var captureTelemetryContent = builder.Environment.IsDevelopment();

LocalLLMsOptions BuildModelOptions(string sectionKey, string fallbackModelPathKey, string fallbackModelNameKey, string defaultModelId)
{
    var options = new LocalLLMsOptions
    {
        ExecutionProvider = executionProvider,
        CaptureTelemetryContent = captureTelemetryContent
    };

    var modelPath = builder.Configuration[$"{sectionKey}:ModelPath"]
        ?? builder.Configuration[fallbackModelPathKey];
    if (!string.IsNullOrWhiteSpace(modelPath))
    {
        options.ModelPath = modelPath;
    }
    else
    {
        var modelName = builder.Configuration[$"{sectionKey}:ModelName"]
            ?? builder.Configuration[fallbackModelNameKey]
            ?? defaultModelId;
        options.Model = KnownModels.FindById(modelName)
            ?? throw new InvalidOperationException($"Unknown LocalLLMs model '{modelName}'.");
        options.EnsureModelDownloaded = true;

        if (!string.IsNullOrWhiteSpace(cacheDirectory))
            options.CacheDirectory = cacheDirectory;
    }

    return options;
}

var orchestratorOptions = BuildModelOptions(
    sectionKey: "LocalLLMs:Models:Orchestrator",
    fallbackModelPathKey: "LocalLLMs:ModelPath",
    fallbackModelNameKey: "LocalLLMs:ModelName",
    defaultModelId: KnownModels.MagenticBrain.Id);

var computerUseOptions = BuildModelOptions(
    sectionKey: "LocalLLMs:Models:ComputerUse",
    fallbackModelPathKey: "LocalLLMs:ComputerModelPath",
    fallbackModelNameKey: "LocalLLMs:ComputerModelName",
    defaultModelId: KnownModels.Fara15_9B.Id);

builder.Services
    .AddChatClient(sp => new LocalChatClient(
        orchestratorOptions,
        sp.GetRequiredService<ILoggerFactory>()))
    .UseOpenTelemetry(
        sourceName: genAiActivitySourceName,
        configure: telemetry => telemetry.EnableSensitiveData = builder.Environment.IsDevelopment());

builder.Services.AddSingleton(sp => new LocalVisionChatClient(
    computerUseOptions,
    sp.GetRequiredService<ILoggerFactory>()));

// ── Tools ─────────────────────────────────────────────────────────────────
var configuredWorkDir = builder.Configuration["LocalLLMs:WorkingDirectory"];
var workDir = string.IsNullOrWhiteSpace(configuredWorkDir)
    ? Path.Combine(Path.GetTempPath(), "magentic-sandbox")
    : configuredWorkDir;
Directory.CreateDirectory(workDir);
builder.Services.AddSingleton(new FileSurferTool(workDir));

builder.Services.AddHttpClient("webfetcher");
builder.Services.AddSingleton<WebFetchTool>(sp =>
    new WebFetchTool(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("webfetcher"),
        markdownConverter: null));

builder.Services.AddSingleton<CodeExecutorTool>();
builder.Services.AddSingleton<ComputerUseTool>(sp => new ComputerUseTool(
    sp.GetRequiredService<LocalVisionChatClient>(),
    workDir,
    sp.GetService<ILogger<ComputerUseTool>>()));

// ── Agents + Orchestrator (Scoped per Blazor circuit) ──────────────────────
builder.Services.AddScoped<UserProxyAgent>();
builder.Services.AddScoped<MagenticUIOrchestrator>(sp => new MagenticUIOrchestrator(
    orchestratorClient: sp.GetRequiredService<IChatClient>(),
    fileSurfer: sp.GetRequiredService<FileSurferTool>(),
    webFetcher: sp.GetRequiredService<WebFetchTool>(),
    coder: sp.GetRequiredService<CodeExecutorTool>(),
    computerUse: sp.GetRequiredService<ComputerUseTool>(),
    userProxy: sp.GetRequiredService<UserProxyAgent>(),
    maxRounds: builder.Configuration.GetValue("LocalLLMs:MaxRounds", 15),
    logger: sp.GetService<ILogger<MagenticUIOrchestrator>>()));
builder.Services.AddScoped<AgentSessionService>();

// ── Blazor ─────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

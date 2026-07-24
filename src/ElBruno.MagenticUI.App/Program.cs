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

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(
        genAiActivitySourceName,
        LocalLLMsInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(LocalLLMsInstrumentation.MeterName));

// ── Local LLM (ONNX via ElBruno.LocalLLMs → IChatClient) ──────────────────
var localLlmOptions = new LocalLLMsOptions
{
    ExecutionProvider = builder.Configuration.GetValue(
        "LocalLLMs:ExecutionProvider",
        ExecutionProvider.Cpu),
    CaptureTelemetryContent = builder.Environment.IsDevelopment()
};

var modelPath = builder.Configuration["LocalLLMs:ModelPath"];
if (!string.IsNullOrWhiteSpace(modelPath))
{
    localLlmOptions.ModelPath = modelPath;
}
else
{
    var modelName = builder.Configuration["LocalLLMs:ModelName"]
        ?? KnownModels.Phi35MiniInstruct.Id;
    localLlmOptions.Model = KnownModels.FindById(modelName)
        ?? throw new InvalidOperationException($"Unknown LocalLLMs model '{modelName}'.");
    localLlmOptions.EnsureModelDownloaded = true;

    var cacheDirectory = builder.Configuration["LocalLLMs:CacheDirectory"];
    if (!string.IsNullOrWhiteSpace(cacheDirectory))
        localLlmOptions.CacheDirectory = cacheDirectory;
}

builder.Services
    .AddChatClient(sp => new LocalChatClient(
        localLlmOptions,
        sp.GetRequiredService<ILoggerFactory>()))
    .UseOpenTelemetry(
        sourceName: genAiActivitySourceName,
        configure: telemetry => telemetry.EnableSensitiveData = builder.Environment.IsDevelopment());

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

// ── Agents + Orchestrator (Scoped per Blazor circuit) ──────────────────────
builder.Services.AddScoped<UserProxyAgent>();
builder.Services.AddScoped<MagenticUIOrchestrator>(sp => new MagenticUIOrchestrator(
    orchestratorClient: sp.GetRequiredService<IChatClient>(),
    fileSurfer: sp.GetRequiredService<FileSurferTool>(),
    webFetcher: sp.GetRequiredService<WebFetchTool>(),
    coder: sp.GetRequiredService<CodeExecutorTool>(),
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

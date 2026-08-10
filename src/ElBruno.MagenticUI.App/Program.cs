using ElBruno.LocalLLMs;
using ElBruno.LocalLLMs.Diagnostics;
using ElBruno.LocalLLMs.BlazorComponents.Extensions;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Configuration;
using ElBruno.MagenticUI.Agents.Orchestrator;
using ElBruno.MagenticUI.Agents.Tools;
using ElBruno.MagenticUI.App;
using ElBruno.MagenticUI.App.Configuration;
using ElBruno.MagenticUI.App.LocalLlm;
using ElBruno.MagenticUI.App.ModelDownloadProgress;
using ElBruno.MagenticUI.App.ModelSettings;
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
builder.Services.AddSingleton<IPathSafetyService, PathSafetyService>();
builder.Services.AddSingleton<IModelFolderLauncher, ModelFolderLauncher>();
builder.Services.AddSingleton<IModelSettingsService, ModelSettingsService>();
builder.Services.AddSingleton<IModelStatusService, ModelStatusService>();
builder.Services.AddSingleton<IModelDownloadProgressStateService, ModelDownloadProgressStateService>();
builder.Services.AddSingleton<ILocalLlmClientFactory, LocalLlmClientFactory>();
builder.Services.AddSingleton<IAppRuntimeSettingsService, AppRuntimeSettingsService>();

builder.Services
    .AddChatClient(sp => sp.GetRequiredService<ILocalLlmClientFactory>().CreateOrchestratorChatClient())
    .UseOpenTelemetry(
        sourceName: genAiActivitySourceName,
        configure: telemetry => telemetry.EnableSensitiveData = builder.Environment.IsDevelopment());

var faraVisionOptions = new FaraVisionOptions();
builder.Configuration.GetSection(FaraVisionOptions.SectionName).Bind(faraVisionOptions);
builder.Services.AddFaraVisionLLM(faraVisionOptions);

// ── Blazor components for model management (Settings page) ────────────────
builder.Services.AddLocalLLMsBlazorComponents();

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
    cancellationToken => sp.GetRequiredService<ILocalLlmClientFactory>().CreateComputerUseChatClientAsync(cancellationToken),
    workDir,
    sp.GetService<ILogger<ComputerUseTool>>()));

// ── Agents + Orchestrator (Scoped per Blazor circuit) ──────────────────────
builder.Services.AddScoped<UserProxyAgent>();
builder.Services.AddTransient<IAgentOrchestrator>(sp => new MagenticUIOrchestrator(
    orchestratorClient: sp.GetRequiredService<IChatClient>(),
    fileSurfer: sp.GetRequiredService<FileSurferTool>(),
    webFetcher: sp.GetRequiredService<WebFetchTool>(),
    coder: sp.GetRequiredService<CodeExecutorTool>(),
    computerUse: sp.GetRequiredService<ComputerUseTool>(),
    userProxy: sp.GetRequiredService<UserProxyAgent>(),
    maxRounds: builder.Configuration.GetValue("LocalLLMs:MaxRounds", 15),
    maxOutputTokens: builder.Configuration.GetValue("LocalLLMs:MaxOutputTokens", 256),
    logger: sp.GetService<ILogger<MagenticUIOrchestrator>>()));
builder.Services.AddScoped<AgentSessionService>();
builder.Services.AddSingleton<FaraActionParser>();
builder.Services.AddScoped<IScreenshotPredictionService, FaraScreenshotPredictionService>();

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
app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

using ElBruno.LocalLLMs;
using ElBruno.LocalLLMs.Diagnostics;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Orchestrator;
using ElBruno.MagenticUI.Agents.Tools;
using ElBruno.MagenticUI.App;
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
builder.Services.AddSingleton<IModelDownloadProgressStateService, ModelDownloadProgressStateService>();
builder.Services.AddSingleton<ILocalLlmClientFactory, LocalLlmClientFactory>();

builder.Services
    .AddChatClient(sp => sp.GetRequiredService<ILocalLlmClientFactory>().CreateOrchestratorChatClient())
    .UseOpenTelemetry(
        sourceName: genAiActivitySourceName,
        configure: telemetry => telemetry.EnableSensitiveData = builder.Environment.IsDevelopment());

builder.Services.AddSingleton(sp => sp.GetRequiredService<ILocalLlmClientFactory>().CreateComputerUseChatClient());

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

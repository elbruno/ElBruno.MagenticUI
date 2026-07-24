using ElBruno.LocalLLMs;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Orchestrator;
using ElBruno.MagenticUI.Agents.Tools;
using ElBruno.MagenticUI.App;
using ElBruno.MagenticUI.App.Components;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Local LLM (ONNX via ElBruno.LocalLLMs → IChatClient) ──────────────────
builder.Services.AddLocalLLMs(options =>
{
    var modelPath = builder.Configuration["LocalLLMs:ModelPath"];
    if (!string.IsNullOrWhiteSpace(modelPath))
    {
        // Explicit pre-downloaded model path
        options.ModelPath = modelPath;
    }
    else
    {
        // Auto-download mode: model is downloaded to the cache dir on first use
        options.EnsureModelDownloaded = true;
        options.Model = ElBruno.LocalLLMs.KnownModels.Phi35MiniInstruct; // fast default
        var cacheDir = builder.Configuration["LocalLLMs:CacheDirectory"];
        if (!string.IsNullOrWhiteSpace(cacheDir))
            options.CacheDirectory = cacheDir;
    }
});

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

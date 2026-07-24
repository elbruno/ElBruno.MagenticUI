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
    options.ModelPath = builder.Configuration["LocalLLMs:ModelPath"] ?? string.Empty;
});

// ── Tools ─────────────────────────────────────────────────────────────────
var workDir = builder.Configuration["LocalLLMs:WorkingDirectory"]
    ?? Path.Combine(Path.GetTempPath(), "magentic-sandbox");
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

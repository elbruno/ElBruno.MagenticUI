# ElBruno.MagenticUI — Copilot Instructions

## What this repo is

**ElBruno.MagenticUI** is a standalone .NET 8 application that ports [microsoft/magentic-ui](https://github.com/microsoft/magentic-ui) to the .NET ecosystem using **Blazor Server** for the frontend and **ElBruno.LocalLLMs** for local ONNX model inference.

It is NOT a NuGet library. It is a **runnable web application** that lets users interact with a multi-agent system powered by local LLMs.

---

## Product goals

1. **Local-first**: all inference runs on-device via ONNX Runtime GenAI. No cloud API keys required.
2. **Human-in-the-loop**: the orchestrator pauses and asks the user for clarification when needed. The user responds through the Blazor UI.
3. **Blazor Server only**: no React, no Node.js, no npm. All real-time UI is handled by Blazor's built-in SignalR circuit. Zero JavaScript frameworks.
4. **Port fidelity**: implement the same agent roles as magentic-ui — Orchestrator, FileSurfer, WebFetcher, Coder, UserProxy — with the same round-based OmniAgent loop.
5. **WSL2 code execution**: Python code runs inside a WSL2 sandbox with a 30-second timeout. Graceful fallback if WSL2 is unavailable.

---

## Architecture

```
ElBruno.MagenticUI.App          ← Blazor Server host (ASP.NET Core)
  └── ElBruno.MagenticUI.Agents ← Orchestrator, agents, tools (no Blazor/SignalR refs)
        └── ElBruno.LocalLLMs   ← ONNX inference (NuGet or ProjectReference)
```

**Key constraint:** `ElBruno.MagenticUI.Agents` must NOT reference `Microsoft.AspNetCore.*` or `Microsoft.AspNetCore.SignalR.*`. It communicates with the host only through `IProgress<AgentMessage>` and `CancellationToken`.

---

## Repository structure conventions

```
ElBruno.MagenticUI/
├── ElBruno.MagenticUI.slnx       ← XML solution (always .slnx, never .sln)
├── Directory.Build.props          ← Shared MSBuild properties for all projects
├── global.json                    ← SDK 8.0.0 + rollForward latestMajor
├── .gitignore
├── README.md
├── LICENSE
├── docs/                          ← All documentation except README and LICENSE
├── images/                        ← Images (nuget_logo.png etc — not needed here but reserved)
├── src/
│   ├── ElBruno.MagenticUI.App/       ← Blazor Server application
│   ├── ElBruno.MagenticUI.Agents/    ← Agents library
│   └── tests/
│       └── ElBruno.MagenticUI.Agents.Tests/  ← xUnit tests
└── .github/workflows/
    └── build.yml                  ← CI: restore, build, test on ubuntu-latest
```

### Documentation placement rule
- Documentation must live in `docs/` or inside feature-local folders when it is code-coupled.
- The repository root must only contain `README.md` and `LICENSE` as documentation files.

---

## Coding conventions

### Language & framework
- C# 12+, `LangVersion: latest`
- `Nullable: enable` — all nullable warnings treated as errors
- `ImplicitUsings: enable`
- Target `net8.0` for the App and Agents projects (single target — no multi-targeting for apps)
- Test projects target `net8.0` only

### Naming
- Agents: `{Role}Agent.cs` — e.g. `FileSurferAgent.cs`, `UserProxyAgent.cs`
- Tools: `{Action}Tool.cs` — e.g. `CodeExecutorTool.cs`, `WebFetchTool.cs`
- Models (DTOs): `AgentMessage.cs`, `TaskRequest.cs`, `CodeExecutionResult.cs`
- Blazor pages: `Pages/{Name}.razor`
- Blazor components: `Components/{Name}.razor`

### DI & configuration
- Register everything via `IServiceCollection` extension methods
- Read model path from `appsettings.json` key `LocalLLMs:ModelPath`
- Never hardcode paths or API keys

### Agent communication pattern
```csharp
// Agents receive and report via these two parameters — never direct SignalR
Task<string> ExecuteAsync(string input, IProgress<AgentMessage> progress, CancellationToken ct)
```

### Human-in-the-loop pattern
```csharp
// UserProxyAgent blocks on a TCS until SignalR delivers the response
private TaskCompletionSource<string>? _pending;

public async Task<string> ExecuteAsync(string question, IProgress<AgentMessage> progress, CancellationToken ct)
{
    _pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var reg = ct.Register(() => _pending.TrySetCanceled(ct));
    progress.Report(new AgentMessage("UserProxy", "input_request", question, round: 0, DateTimeOffset.UtcNow));
    return await _pending.Task;
}

public void SetResponse(string response) => _pending?.TrySetResult(response);
```

### WSL2 code execution pattern
- Python only (allowlist: `"python"`, `"python3"`)
- Use `Process.Start("wsl", ["--", "python3", "-c", code])` on Windows
- 30-second timeout via `CancellationTokenSource`
- Check `IsWslAvailable()` first; return a clear error if WSL2 is not found — never throw

### Blazor real-time pattern
- Use `InvokeAsync(StateHasChanged)` from background threads
- Bind agent messages to a `List<AgentMessage>` rendered in a `@foreach`
- Use `@implements IAsyncDisposable` on pages that hold SignalR or agent state
- Task input: a `<textarea>` bound to a string, submitted via button

### Tests
- Framework: xUnit + coverlet
- One class per file, one file per subject
- `[Fact]` for simple cases, `[Theory, InlineData(...)]` for parameterized
- Arrange / Act / Assert with section comments
- Do NOT launch real WSL2 or real ONNX models in tests — use mocks/fakes
- Mark tests that require external resources with `[Fact(Skip = "Requires WSL2")]`
- Target: `net8.0` only

---

## What to build next (Phase 3C)

These are the immediate work items for a Squad team to pick up:

### P1 — Port agents library
Copy and adapt from `ElBruno.LocalLLMs/src/samples/MagenticUIServer/MagenticUIServer.Agents/`:
- `MagenticUIOrchestrator.cs` — OmniAgent round-based loop
- `UserProxyAgent.cs` — TCS human-in-loop pause
- `FileSurferAgent.cs` + `FileSurferTool.cs`
- `WebFetcherAgent.cs` + `WebFetchTool.cs`
- `CoderAgent.cs` + `CodeExecutorTool.cs` (WSL2)
- `MarkItDownTool.cs`
- All model records: `AgentMessage`, `TaskRequest`, `CodeExecutionResult`, `AgentSession`

Remove `AgentsPlaceholder.cs` once the first real file is added.

### P2 — Blazor real-time task panel
Replace `Pages/Home.razor` (Blazor default) with a real task panel:
- Text area for task input + Submit/Cancel buttons
- Live agent message feed (streamed via `IProgress<AgentMessage>`)
- Human-in-the-loop input box (appears when `role == "input_request"`)
- Status badge (idle / running / waiting-for-input / done / error)
- Code block rendering for `role == "code_output"`

### P3 — Wire App ↔ Agents
- `AgentSessionService` scoped service: holds `UserProxyAgent` + `CancellationTokenSource` per circuit
- Blazor page calls `StartTaskAsync(request)` and subscribes to `IProgress<AgentMessage>` via an event/callback
- `RespondToInputAsync(sessionId, response)` called when the user submits the input box
- Session cleanup on circuit disconnect (`IAsyncDisposable`)

### P4 — Configuration UI
- Simple settings page: model path picker, max rounds, timeout
- Persisted to `appsettings.json` or user secrets
- Validation: show error if model path doesn't exist

### P5 — Tests
- Unit tests for all agents and tools (mock WSL2, mock HTTP)
- Integration smoke test: orchestrator with a stub `LocalChatClient`

---

## What NOT to do

- Do NOT add npm, Vite, webpack, or any JavaScript build step
- Do NOT reference `Microsoft.AspNetCore.*` from `ElBruno.MagenticUI.Agents`
- Do NOT add a `.sln` file — use `.slnx` only
- Do NOT add multi-targeting to App or Agents projects (apps target one TFM)
- Do NOT store model files in the repo (they are multi-GB ONNX files)
- Do NOT add a NuGet publish workflow — this is an app, not a library
- Do NOT add authentication or user accounts in v1

---

## Reference material

- Original Python implementation: https://github.com/microsoft/magentic-ui
- MagenticBrain model: https://huggingface.co/microsoft/MagenticBrain
- Fara1.5-9B model: https://huggingface.co/microsoft/Fara1.5-9B
- ElBruno.LocalLLMs (inference engine): https://github.com/elbruno/ElBruno.LocalLLMs
- Phase 3A/3B implementation (to port from): `ElBruno.LocalLLMs/src/samples/MagenticUIServer/`
- Architecture decisions: `ElBruno.LocalLLMs/docs/magentic-ui-dotnet.md`

---

## Author

**Bruno Capuano (ElBruno)**
- Blog: https://elbruno.com
- YouTube: https://youtube.com/@inthelabs
- LinkedIn: https://linkedin.com/in/inthelabs

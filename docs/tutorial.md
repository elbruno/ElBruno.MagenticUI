# ElBruno.MagenticUI — Getting Started Tutorial

This guide walks you through two end-to-end scenarios after you launch the app with
`aspire start`.

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| .NET 10 SDK | https://dotnet.microsoft.com/download |
| Aspire CLI | `dotnet tool install -g Aspire.Cli` |
| Windows x64 | Required for CPU/DirectML ONNX inference |
| ~4 GB free disk space | For the default Phi-3.5-mini model download |

> **GPU / DirectML optional.** The app auto-falls-back to CPU if DirectML or CUDA is
> unavailable. Inference on CPU is slower but fully functional.

---

## Launch the app

```powershell
# From the repo root
aspire start
```

Aspire prints two URLs:

```
Dashboard: https://localhost:17175/login?t=<token>
App:       https://localhost:7127
```

Open the **App URL** (`https://localhost:7127`) in your browser. The MagenticUI panel
appears immediately.

**First run only:** If `LocalLLMs:ModelPath` is empty in `appsettings.json`, the app
auto-downloads `phi-3.5-mini-instruct` (~2.4 GB) to
`%LOCALAPPDATA%\ElBruno\LocalLLMs\models\` the first time you submit a task. This takes
a few minutes depending on your connection; progress is logged to the Aspire dashboard.

---

## Aspire Dashboard

The dashboard at `https://localhost:17175` gives you:

- **Traces** — every agent round shows as a span tree
- **Logs** — real-time structured logs from all services
- **Metrics** — request counts, latencies, health

Use it to inspect what each agent is doing internally while your task runs.

---

## Scenario 1 — Summarise a Web Page

**Goal:** Ask the orchestrator to fetch a URL and return a concise summary.

### Steps

1. Open `https://localhost:7127`
2. In the **task input** box, type:
   ```
   Please fetch the page at https://elbruno.com and give me a 3-sentence summary of
   what it is about.
   ```
3. Click **Start Task** (or press **Ctrl + Enter**)
4. Watch the live message feed:
   - 🔵 **Orchestrator** — decides to call `WebFetcher_FetchUrl`
   - 🟢 **WebFetcher** — fetches the URL and returns HTML/Markdown
   - 🔵 **Orchestrator** — synthesises the summary
   - 🟣 **Submit** message — task complete

5. The final answer appears in the feed labelled `submit`.

### What to expect

Depending on model speed (CPU ~30-60 s per round, DirectML ~5-10 s), the task
completes in 2-4 rounds. The feed updates in real time.

### Try variations

- `Summarise https://github.com/microsoft/magentic-ui/blob/main/README.md`
- `What is the latest news on https://news.ycombinator.com ? Give me the top 5 stories.`

---

## Scenario 2 — Analyse Files in a Sandbox Directory

**Goal:** Have the orchestrator list, read, and reason about files you place in the
working directory.

### Steps

1. Create a sandbox folder and drop some text files in it:
   ```powershell
   $sandbox = "$env:TEMP\magentic-sandbox"
   New-Item $sandbox -ItemType Directory -Force
   "Invoice #1001 - Amount: $250.00 - Client: Contoso" | Set-Content "$sandbox\invoice1.txt"
   "Invoice #1002 - Amount: $175.50 - Client: Fabrikam" | Set-Content "$sandbox\invoice2.txt"
   "Invoice #1003 - Amount: $420.00 - Client: Contoso" | Set-Content "$sandbox\invoice3.txt"
   ```

2. Open `https://localhost:7127`
3. Type this task:
   ```
   List all .txt files in the working directory, read them, and tell me:
   - Total amount across all invoices
   - Which client has the highest total, and what that client total is
   ```
4. Click **Start Task**
5. Watch the feed:
   - 🔵 **Orchestrator** — calls `FileSurfer_ListDirectory`
   - 🟠 **FileSurfer** — returns `invoice1.txt`, `invoice2.txt`, `invoice3.txt`
   - 🔵 **Orchestrator** — calls `FileSurfer_ReadFile` for each
   - 🟠 **FileSurfer** — returns file contents
   - 🔵 **Orchestrator** — calculates totals, calls `Submit`

6. Answer: total $845.50, Contoso has $670.00.

### What to expect

FileSurfer is sandboxed to `LocalLLMs:WorkingDirectory` (defaults to
`%TEMP%\magentic-sandbox`). It cannot read outside that directory.

### Try variations

- Drop a CSV or JSON file and ask for specific data extraction
- Ask the orchestrator to write a summary file back: `Save the total as summary.txt`
  (uses `FileSurfer_WriteFile`)

---

## Human-in-the-Loop

When the orchestrator needs clarification it pauses and shows a **yellow input card**
in the feed. Type your answer and click **Send**. The task resumes immediately.

Example prompt that triggers clarification:
```
Analyse the files and save a report, but first ask me what format I prefer (CSV or JSON).
```

---

## Configuring a Different Model

Edit `src/ElBruno.MagenticUI.App/appsettings.json`:

```json
"LocalLLMs": {
  "ModelPath": "C:\\Models\\magentic-brain\\cpu\\cpu-int4-awq-block-128",
  "WorkingDirectory": "C:\\MyTaskSandbox",
  "MaxRounds": 20
}
```

| Setting | Description |
|---------|-------------|
| `ModelPath` | Explicit path to an extracted ONNX model folder. If empty, auto-download is used. |
| `ModelName` | Model ID for models with native ONNX artifacts (default: `phi-3.5-mini-instruct`). |
| `CacheDirectory` | Override the auto-download cache (default: `%LOCALAPPDATA%\ElBruno\LocalLLMs\models`). |
| `ExecutionProvider` | ONNX execution provider (`Cpu`, `Cuda`, `DirectML`, or `Auto`). Default: `Cpu`. |
| `WorkingDirectory` | Sandbox directory for FileSurfer file operations. |
| `MaxRounds` | Maximum agent rounds before the orchestrator stops (default: 15). |

`microsoft/MagenticBrain` is the recommended model for this agentic workflow, but it
does not publish native ONNX artifacts. Convert it with ONNX Runtime GenAI's model
builder, then set `ModelPath` to the conversion output. `microsoft/Fara1.5-9B` also
requires conversion and is intended for vision-and-action workflows rather than this
text-only orchestrator.

Restart `aspire start` after changing the config.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `DllNotFoundException: onnxruntime-genai` | Make sure `Microsoft.ML.OnnxRuntimeGenAI` is a direct reference in the `.csproj` — see [#native-dll-note](#native-dll-note) |
| Inference stalls at "Loading model…" | Large model on slow CPU; wait or switch to a smaller model (`phi-3.5-mini-instruct`) |
| WebFetcher returns empty | The target site may block headless requests; try a different URL |
| Task keeps asking clarifying questions | Raise `MaxRounds` or rephrase the task to be more specific |

### Native DLL Note

`ElBruno.LocalLLMs` depends on `Microsoft.ML.OnnxRuntimeGenAI`. Due to a known issue
where that package's `buildTransitive/` folder omits Windows targets, the native
`onnxruntime-genai.dll` is not automatically copied unless you reference the package
directly. The `ElBruno.MagenticUI.App.csproj` already has this reference; if you create
a new project using `ElBruno.LocalLLMs`, add:

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI" Version="0.14.1" />
```

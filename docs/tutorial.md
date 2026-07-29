# ElBruno.MagenticUI — Getting Started Tutorial

This guide walks you through three end-to-end scenarios after you launch the app with
`aspire start`.

---

## Prerequisites

| Requirement | Notes |
|-------------|-------|
| .NET 10 SDK | https://dotnet.microsoft.com/download |
| Aspire CLI | `dotnet tool install -g Aspire.Cli` |
| Windows x64 | Required for CPU/DirectML ONNX inference |
| ~4+ GB free disk space | Depends on the selected model download |

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

**First run only:** If model paths are empty in `appsettings.json`
(`LocalLLMs:Models:Orchestrator:ModelPath` and `LocalLLMs:Models:ComputerUse:ModelPath`),
the app auto-downloads configured model IDs to
`%LOCALAPPDATA%\ElBruno\LocalLLMs\models\` the first time you submit a task. This takes
a few minutes depending on your connection and model size; progress is logged to the
Aspire dashboard.

---

## Tasks + Settings pages

Use the left navigation menu:

- **Tasks** — submit prompts, review agent output, and see real-time model download
  progress while a task is running.
- **Settings** — manage local model storage and per-role model status.

In **Settings**, each model card shows:

- **Present/Missing** badge for local availability
- **Download phase** (`Idle`, `Downloading`, `Completed`, `Failed`)
- **Effective model path** and status text
- **Open folder** button to open the model location in your OS file explorer
- **Delete model** button with **Confirm delete** / **Cancel** safety prompt

Delete is blocked while a model is actively downloading.

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

During first-run model fetches, the **Tasks** page also shows:

- active per-model progress bars with file/byte progress
- short completion/failure notices when downloads finish

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

## Scenario 3 — Analyse an Image with Fara Computer-Use

**Goal:** Use `Coder_ExecuteCode` to download an image, then use
`Computer_DescribeImage` (Fara model) to analyze it.

### Steps

1. Open `https://localhost:7127`
2. In the **task input** box, paste:
   ```
   Use Coder_ExecuteCode to download https://raw.githubusercontent.com/elbruno/ElBruno.MagenticUI/master/images/magentic_releases.png into the working directory as magentic_releases.png.
   Then call Computer_DescribeImage with:
   - relativePath: magentic_releases.png
   - prompt: "Summarize what this image shows in 3 bullets and mention any timeline insight."
   Return only the final answer.
   ```
3. Click **Start Task**
4. Watch the feed:
   - 🔵 **Orchestrator** plans the steps
   - 💻 **Coder** runs Python to download the image
   - 🖱️ **Computer** analyzes the image through the Fara computer-use model
   - 🔵 **Orchestrator** formats the findings and submits the answer

### What to expect

You should get a visual summary grounded in the screenshot content, produced by the
computer-use model instead of manual PNG header parsing.

---

## Additional Guides

- [Human-in-the-Loop](./tutorial-human-in-the-loop.md)
- [Configuring a Different Model](./tutorial-model-configuration.md)
- [Troubleshooting](./tutorial-troubleshooting.md)
- [Native DLL Note](./tutorial-native-dll-note.md)

# ElBruno.MagenticUI — Configuring a Different Model

The app now includes a **Settings** page (`/settings`) focused on model management:

1. Verify each role (`Orchestrator`, `ComputerUse`) has the expected model and path.
2. Check if the model is downloaded (`Present`) or missing.
3. Track real-time download state (`Idle`, `Downloading`, `Completed`, `Failed`).
4. Use **Download model** to fetch a missing model immediately (without starting a task).
5. Use **Open folder** to open the local model location.
6. Use **Delete model** for per-model cleanup, then **Confirm delete** when prompted.

> Deletion is intentionally blocked while a model download is in progress.

Edit `src/ElBruno.MagenticUI.App/appsettings.json`:

```json
"LocalLLMs": {
  "Models": {
    "Orchestrator": {
      "ModelPath": "C:\\Models\\magentic-brain\\cpu\\cpu-int4-awq-block-128",
      "ModelName": "magentic-brain"
    },
    "ComputerUse": {
      "ModelPath": "C:\\Models\\fara\\cpu\\cpu-int4-awq-block-128",
      "ModelName": "fara1.5-9b"
    }
  },
  "WorkingDirectory": "C:\\MyTaskSandbox",
  "MaxRounds": 20
}
```

| Setting | Description |
|---------|-------------|
| `Models:Orchestrator:*` | MagenticBrain reasoning/delegation model configuration. |
| `Models:ComputerUse:*` | Fara computer-use/vision model configuration. Loaded lazily only when a computer-use tool is invoked. |
| `CacheDirectory` | Override the auto-download cache (default: `%LOCALAPPDATA%\\ElBruno\\LocalLLMs\\models`). |
| `ExecutionProvider` | ONNX execution provider (`Cpu`, `Cuda`, `DirectML`, or `Auto`). Default: `Auto` (falls back to CPU when GPU providers are unavailable). |
| `WorkingDirectory` | Sandbox directory for FileSurfer file operations. |
| `MaxRounds` | Maximum agent rounds before the orchestrator stops (default: 15). |
| `MaxOutputTokens` | Maximum tokens generated per orchestrator response (default: 256). Lower values can improve responsiveness. |
| `TaskTimeoutSeconds` | Overall task timeout for a session (0 disables the timeout). |

`microsoft/Fara1.5-9B` is a vision-and-action model and may require a dedicated vision flow
depending on your scenario. After changing model settings, restart Aspire:

```powershell
aspire stop
aspire start
```

## How to check downloaded models

- Open **Settings** and look for the **Present** badge per role.
- Review the **Effective model path** and click **Open folder** to inspect files.
- While a download is running, you can watch progress in both:
  - **Settings** (per-card download status/progress)
  - **Tasks** (live top-panel progress while executing tasks)

## Runtime behavior notes

- **Computer-use model is on-demand.** The `ComputerUse` model is not loaded during every task start.
  It is initialized only when tools like `Computer_DescribeImage` are actually called.
- **Cancellation is explicit.** When you cancel a run from the Tasks page, status transitions to
  `Cancelling…` before settling to `Idle` or `Error`.
- **Token cap tuning.** If outputs are too long or slow, reduce `MaxOutputTokens`; if outputs are
  too brief, increase it incrementally.

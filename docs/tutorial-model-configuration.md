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
| `Models:ComputerUse:*` | Fara computer-use/vision model configuration. |
| `CacheDirectory` | Override the auto-download cache (default: `%LOCALAPPDATA%\\ElBruno\\LocalLLMs\\models`). |
| `ExecutionProvider` | ONNX execution provider (`Cpu`, `Cuda`, `DirectML`, or `Auto`). Default: `Cpu`. |
| `WorkingDirectory` | Sandbox directory for FileSurfer file operations. |
| `MaxRounds` | Maximum agent rounds before the orchestrator stops (default: 15). |

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

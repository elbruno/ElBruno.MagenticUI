# ElBruno.MagenticUI — Configuring a Different Model

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

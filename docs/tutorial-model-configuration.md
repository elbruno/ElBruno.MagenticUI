# ElBruno.MagenticUI — Configuring a Different Model

Edit `src/ElBruno.MagenticUI.App/appsettings.json`:

```json
"LocalLLMs": {
  "ModelPath": "C:\\Models\\magentic-brain\\cpu\\cpu-int4-awq-block-128",
  "ModelName": "magentic-brain",
  "WorkingDirectory": "C:\\MyTaskSandbox",
  "MaxRounds": 20
}
```

| Setting | Description |
|---------|-------------|
| `ModelPath` | Explicit path to an extracted ONNX model folder. If empty, auto-download is used. |
| `ModelName` | Model ID used when `ModelPath` is empty (default: `magentic-brain`). |
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

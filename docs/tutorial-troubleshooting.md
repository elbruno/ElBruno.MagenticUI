# ElBruno.MagenticUI — Troubleshooting

| Symptom | Fix |
|---------|-----|
| `DllNotFoundException: onnxruntime-genai` | Ensure `Microsoft.ML.OnnxRuntimeGenAI` is a direct reference in the `.csproj` — see [Native DLL Note](./tutorial-native-dll-note.md) |
| Inference stalls at "Loading model..." | Large model on slow CPU; wait, or temporarily switch to a smaller model |
| WebFetcher returns empty | The target site may block headless requests; try a different URL |
| Task keeps asking clarifying questions | Raise `MaxRounds` or rephrase the task to be more specific |
| App runs on CPU even with `ExecutionProvider: Auto` | `Auto` selects the best available provider. If DirectML/CUDA runtime dependencies are missing, fallback to CPU is expected. Install/verify GPU provider prerequisites, then restart the app. |
| Task stays in `Cancelling…` for a while | Cancellation is cooperative. The current model/tool call must reach a cancellation boundary before the status can finalize. |
| Scenario 1 feels slow on first run | First-run orchestrator model warm-up and download can take time. The computer-use model is lazy-loaded and should not block non-vision scenarios. |
| Assistant output appears in chunks | Streaming updates are coalesced into incremental `assistant_stream` messages for readability; this is expected behavior. |
| `Coder_ExecuteCode` times out in Scenario 3 image download | The default code execution timeout is 30 seconds. Pre-place the image file in `LocalLLMs:WorkingDirectory` (for example `%TEMP%\magentic-sandbox`) and rerun the task with direct `Computer_DescribeImage`. |
| `Computer_DescribeImage` fails with `CausalConvWithState ... not a registered function/op` | The local ONNX Runtime GenAI build/model combination is incompatible for this computer-use model. Use the troubleshooting/model configuration guides to align model/runtime versions, then restart and retry Scenario 3. |

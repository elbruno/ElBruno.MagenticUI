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
| Scenario appears stuck before first tool call (`No agent messages yet`) | This can be model warm-up on CPU after provider fallback (`DirectML`/`Cuda` unavailable). In our logs, orchestrator model load took ~29s before round activity started. Wait for warm-up to complete, then rerun for faster subsequent responses. |
| Scenario stays `Running…` and `Agent Messages` remains `0` for minutes while logs stop after provider fallback | The run is likely blocked before the first orchestrator round (model initialization path). Restart the app host, retry the task, and check whether any post-load orchestrator entries appear. If not, capture logs and keep the run classified as blocked pre-orchestration. |
| Assistant output appears in chunks | Streaming updates are coalesced into incremental `assistant_stream` messages for readability; this is expected behavior. |
| Scenario 4 (price comparison) ends with `Reached maximum rounds (15) without a final answer.` | The orchestrator could not converge to a final `submit` within round limits. Try simplifying prompt scope (single URL first), verify web fetch accessibility from the host environment, or temporarily increase max rounds to inspect deeper tool-call progression. |
| Scenario 5 (file organization) stays in `Running…` with only initial `Task received` message | This indicates the run did not progress into FileSurfer tool calls. Cancel the task, retry with a narrower step-by-step prompt (for example list-only first), and confirm working directory files are present before rerunning. |
| `Coder_ExecuteCode` times out in Scenario 3 image download | The default code execution timeout is 30 seconds. Pre-place the image file in `LocalLLMs:WorkingDirectory` (for example `%TEMP%\magentic-sandbox`) and rerun the task with direct `Computer_DescribeImage`. |
| `Computer_DescribeImage` fails with `CausalConvWithState ... not a registered function/op` | The local ONNX Runtime GenAI build/model combination is incompatible for this computer-use model. Use the troubleshooting/model configuration guides to align model/runtime versions, then restart and retry Scenario 3. |
| `dotnet test`/`dotnet build` fails with `MSB3027`/`MSB3021` locking `ElBruno.MagenticUI.Agents.dll` in `ElBruno.MagenticUI.App\bin` | A stale app process is still holding binaries. Stop the app host, then terminate any leftover `ElBruno.MagenticUI.App` process by PID, and rerun tests. Prefer `--no-build` test runs only when you intentionally reuse already-built binaries. |

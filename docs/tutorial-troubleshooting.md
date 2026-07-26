# ElBruno.MagenticUI — Troubleshooting

| Symptom | Fix |
|---------|-----|
| `DllNotFoundException: onnxruntime-genai` | Ensure `Microsoft.ML.OnnxRuntimeGenAI` is a direct reference in the `.csproj` — see [Native DLL Note](./tutorial-native-dll-note.md) |
| Inference stalls at "Loading model..." | Large model on slow CPU; wait, or temporarily switch to a smaller model |
| WebFetcher returns empty | The target site may block headless requests; try a different URL |
| Task keeps asking clarifying questions | Raise `MaxRounds` or rephrase the task to be more specific |

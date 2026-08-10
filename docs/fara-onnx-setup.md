# Fara1.5-9B local ONNX setup

The app uses `ElBruno.LocalLLMs` 0.20.9, whose `LocalVisionChatClient` supports
auto-download for `KnownModels.Fara15_9B` when `EnsureModelDownloaded` is true.
The model is downloaded from `elbruno/Fara1.5-9B-onnx` into the LocalLLMs cache on
first prediction. A local converted multimodal ONNX directory remains supported as
an explicit `ModelPath` override.

1. Convert `microsoft/Fara1.5-9B` with the multimodal conversion workflow described
   in the [LocalLLMs Fara conversion guide](https://github.com/elbruno/ElBruno.LocalLLMs/blob/main/docs/onnx-conversion-fara.md).
2. Validate that the output contains the decoder, vision, embedding, processor, and
   model configuration files required by the vision runtime.
3. Set the extracted directory in
   `src/ElBruno.MagenticUI.App/appsettings.json`:

```json
"LocalLLMs": {
  "FaraVision": {
    "ModelPath": "C:\\Models\\Fara1.5-9B"
  }
}
```

Restart with `aspire start` after changing the setting. If the path is missing, the
Fara page reports this setup requirement directly; the text model continues using its
existing auto-download behavior.

# Fara execution providers (GPU vs CPU)

This page explains how MagenticUI chooses the ONNX Runtime execution provider for the
Fara1.5-9B vision model, why it may run on CPU, and what is required to enable GPU
acceleration.

> Requires `ElBruno.LocalLLMs` **0.20.11+**, which added execution-provider preflight and
> safe fallback ([#45](https://github.com/elbruno/ElBruno.LocalLLMs/issues/45)), correct
> multimodal `max_length` handling ([#44](https://github.com/elbruno/ElBruno.LocalLLMs/issues/44)),
> and the `EnvironmentDashboard` provider panel plus open-folder action
> ([#46](https://github.com/elbruno/ElBruno.LocalLLMs/issues/46)).

## Configuration

`src/ElBruno.MagenticUI.App/appsettings.json`:

```jsonc
"LocalLLMs": {
  "FaraVision": {
    "ExecutionProvider": "Auto",        // Auto | Cpu | Cuda | DirectML
    "MaxOutputTokens": 128,
    "PredictionTimeoutSeconds": 600
  }
}
```

`ExecutionProvider` expresses *intent*. At startup, `ExecutionProviderPlanner` calls
`LocalChatClient.DiagnoseEnvironment(cacheDirectory)` and reads the per-provider preflight
results:

- **`Auto`** — the library resolves the best provider that actually loads. The planner reports
  the resolved provider (`AutoResolvedExecutionProvider`) so the UI can show it before the first
  prediction.
- **A specific provider that passed preflight** — used as configured.
- **A specific provider reported `Unavailable`** — the request degrades to `Auto`, and the
  diagnostic's `Reason` / `Suggestion` are surfaced in the UI banner explaining why.
- **`Unknown`** — left alone; the library preflights again at load time and degrades safely.

`Auto` is the recommended setting. It was previously unsafe (see below), but 0.20.11 preflights
each candidate before handing it to ONNX Runtime.

The Fara page shows the effective provider in a banner. The **Local models** page
(`/local-models`) renders `EnvironmentDashboard` with `ShowOpenFolderButton`, which lists every
provider's readiness and opens the model cache folder.

## Why CUDA is usually unavailable today

`ElBruno.LocalLLMs` requires `Microsoft.ML.OnnxRuntimeGenAI` **0.15.1+**, which is built on
**ONNX Runtime 1.28**. The ORT 1.28 CUDA execution provider links against the **CUDA 13**
runtime — not CUDA 12 — and CUDA 13 requires an NVIDIA driver of **r580 or newer**.

Concretely, `onnxruntime_providers_cuda.dll` needs these to be resolvable by the OS loader:

| Library | Comes from |
| --- | --- |
| `cudart64_13.dll` | CUDA 13 runtime |
| `cublas64_13.dll` | CUDA 13 runtime |
| `cublasLt64_13.dll` | CUDA 13 runtime |
| `cudnn64_9.dll` (+ `cudnn_*64_9.dll`) | cuDNN 9 |

The `Microsoft.ML.OnnxRuntime.Gpu.Windows` package ships the provider DLL but deliberately
**does not** redistribute the CUDA runtime or cuDNN, so they must be installed separately
(CUDA Toolkit 13 + cuDNN 9, or the `nvidia-*-cu13` Python wheels).

Historically a CUDA **12** installation was not merely slower — it was fatal. ONNX Runtime GenAI
did not handle a failed provider load: the process died with an access violation (`0xC0000005`)
inside `OgaCreateModelFromConfig`, before any weights were read. In a Blazor Server host that
killed the web app mid-request, and the browser simply waited until the prediction timeout
fired, with nothing in the logs.

`ElBruno.LocalLLMs` 0.20.11 preflights providers and reports them as `Unavailable` instead of
letting the native load crash, so a mismatched CUDA stack now degrades cleanly to CPU.

### DirectML is not an option

`Microsoft.ML.OnnxRuntimeGenAI.DirectML` stops at version **0.14.1**; there is no 0.15.x
release, so it cannot be combined with `ElBruno.LocalLLMs`' 0.15.1+ requirement. Mixing the two
would place two incompatible native `onnxruntime-genai.dll` builds in the output folder.

## Enabling GPU

1. Install an NVIDIA driver **r580+**.
2. Install the **CUDA 13** runtime and **cuDNN 9**.
3. Add `Microsoft.ML.OnnxRuntimeGenAI.Cuda` to `ElBruno.MagenticUI.App.csproj` (replacing the
   CPU `Microsoft.ML.OnnxRuntimeGenAI` reference) — see the comment in that file.
4. Leave `ExecutionProvider` at `Auto`, or set it to `Cuda` to make the intent explicit.
5. Open `/local-models` and confirm the Environment panel reports CUDA as available.

## CPU performance expectations

Fara1.5-9B is a 9-billion-parameter multimodal model. On CPU, a single screenshot prediction
takes **minutes** rather than seconds, which is why `PredictionTimeoutSeconds` defaults to 600.

`MaxOutputTokens` (default 128) caps generation. Before 0.20.11 it had to be left unset,
because the library derived ONNX Runtime's `max_length` from the text prompt only and ignored
the thousands of vision tokens, so any value failed with
`input_ids size (N) exceeds max length`. That is fixed, and capping output materially shortens
CPU predictions — a prediction is a single short JSON action, so generating up to
`MaxSequenceLength` (4096) was pure waste and also caused the model to repeat itself.

## Cache directory

Leave `CacheDirectory` blank to use the shared `ElBruno.LocalLLMs` cache. Blank values are
normalized to `null` by `FaraVisionServiceExtensions`: an empty string is *not* equivalent to
unset, because the downloader combines it with the model id and produces a **relative** cache
path next to the running app, which re-downloads the ~10 GB model on every request.

## Known upstream issues in ElBruno.LocalLLMs 0.20.11

Two defects were found in 0.20.11 during validation. Both are filed upstream with suggested
fixes, and both workarounds should be **deleted** once a fixed package ships.

### elbruno/ElBruno.LocalLLMs#49 - assembly version mismatch (worked around)

The 0.20.11 core package ships `ElBruno.LocalLLMs.dll` stamped `AssemblyVersion 0.20.9.0`,
while `ElBruno.LocalLLMs.BlazorComponents` 0.20.11 references `0.20.11.0`. .NET only rolls
assembly references *forward*, so the app fails at startup with a misleading
`FileNotFoundException: ElBruno.LocalLLMs, Version=0.20.11.0` even though the DLL is present.

Workaround: `src/ElBruno.MagenticUI.App/LocalLLMsAssemblyVersionShim.cs` registers an
`AssemblyLoadContext.Default.Resolving` handler from a `[ModuleInitializer]`. The module
initializer placement is required - code inside `Main` runs too late, because JIT-ing
`Program.<Main>$` already triggers the failing bind.

### elbruno/ElBruno.LocalLLMs#51 - vision predictions fail (no workaround)

Every prediction that includes an image fails with:

```
max_length (2147483647) cannot be greater than model context_length (32768)
```

`OnnxVisionModel.ResolveVisionInputTokenCount` creates a probe generator with
`max_length = int.MaxValue`, which ONNX Runtime GenAI rejects. The check exists in both
GenAI 0.14.1 and 0.15.1, so it cannot be avoided by pinning an older runtime, and there is no
public option to skip the probe. The real generation path is fine - only the probe is broken.

`FaraScreenshotPredictionService` maps this native error to a clear message pointing at the
issue instead of surfacing the raw text. Remove that mapping when the fix ships.

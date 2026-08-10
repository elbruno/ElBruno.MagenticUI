# Fara execution providers (GPU vs CPU)

This page explains how MagenticUI chooses the ONNX Runtime execution provider for the
Fara1.5-9B vision model, why it may run on CPU, and what is required to enable GPU
acceleration.

## Configuration

`src/ElBruno.MagenticUI.App/appsettings.json`:

```jsonc
"LocalLLMs": {
  "FaraVision": {
    "ExecutionProvider": "Cuda",        // Auto | Cpu | Cuda | DirectML
    "CudaDependencyPath": "",           // optional explicit CUDA/cuDNN directory
    "PredictionTimeoutSeconds": 600
  }
}
```

`ExecutionProvider` expresses *intent*. At startup `CudaRuntimeResolver` probes the machine
for a usable CUDA runtime; if one is not found, `FaraVisionServiceExtensions` downgrades the
request to `Cpu`. The Fara page shows the effective provider in a banner, and the reason when
it falls back.

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

A CUDA **12** installation is not merely slower — it is fatal. ONNX Runtime GenAI does not
handle a failed provider load: the process dies with an access violation (`0xC0000005`) inside
`OgaCreateModelFromConfig`, before any weights are read. In a Blazor Server host that kills the
web app mid-request and the browser simply waits until the prediction timeout fires, with no
error in the logs. This is why the app never requests CUDA unless a complete CUDA 13 set is
found, and never uses `ExecutionProvider.Auto` (whose candidate list includes CUDA).

Tracked upstream in [elbruno/ElBruno.LocalLLMs#45](https://github.com/elbruno/ElBruno.LocalLLMs/issues/45).

### DirectML is not an option

`Microsoft.ML.OnnxRuntimeGenAI.DirectML` stops at version **0.14.1**; there is no 0.15.x
release, so it cannot be combined with `ElBruno.LocalLLMs`' 0.15.1+ requirement. Mixing the two
would place two incompatible native `onnxruntime-genai.dll` builds in the output folder.

## Enabling GPU

1. Install an NVIDIA driver **r580+**.
2. Install the **CUDA 13** runtime and **cuDNN 9**.
3. Add `Microsoft.ML.OnnxRuntimeGenAI.Cuda` to `ElBruno.MagenticUI.App.csproj` (replacing the
   CPU `Microsoft.ML.OnnxRuntimeGenAI` reference) — see the comment in that file.
4. Leave `ExecutionProvider` set to `Cuda`. If the libraries live outside the standard
   locations, point `CudaDependencyPath` at the directory that contains them.

`CudaRuntimeResolver` probes, in order: `CudaDependencyPath`, AI Toolkit's
`%USERPROFILE%\.aitk\bin\libonnxruntime_cuda_windows\<version>`, Ollama's `cuda_v13`/`cuda_v12`
folders, `%CUDA_PATH%\bin`, and the CUDA Toolkit install directories. When a directory contains
every required library it is registered with the OS loader (`AddDllDirectory` plus the
in-process `PATH`).

## CPU performance expectations

Fara1.5-9B is a 9-billion-parameter multimodal model. On CPU, a single screenshot prediction
takes **several minutes** (~250 s for a 2378x1211 screenshot on a 2026-era server CPU), which is
why `PredictionTimeoutSeconds` defaults to 600.

Note also that `MaxOutputTokens` is intentionally **not** set on vision requests: the library
computes ONNX Runtime's `max_length` from the text prompt only, ignoring the thousands of
vision tokens, so any value fails with `input_ids size (N) exceeds max length`. Tracked in
[elbruno/ElBruno.LocalLLMs#44](https://github.com/elbruno/ElBruno.LocalLLMs/issues/44). Once
that is fixed, restoring a token cap will also shorten CPU prediction times considerably.

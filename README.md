# ElBruno.MagenticUI

[![CI Build](https://img.shields.io/github/actions/workflow/status/elbruno/ElBruno.MagenticUI/build.yml?branch=main&label=CI%20Build)](https://github.com/elbruno/ElBruno.MagenticUI/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## About

C# local-first multi-agent web app using ONNX Runtime, compatible with Microsoft.Extensions.AI through [ElBruno.LocalLLMs](https://github.com/elbruno/ElBruno.LocalLLMs).

This project is a Blazor Server port of [microsoft/magentic-ui](https://github.com/microsoft/magentic-ui), designed for on-device inference and human-in-the-loop orchestration workflows.

## User manual

For setup, walkthrough scenarios, and troubleshooting, use:

- **[`docs/tutorial.md`](docs/tutorial.md)**

## Current architecture

```text
src/
├── ElBruno.MagenticUI.AppHost             # Aspire AppHost/orchestration
├── ElBruno.MagenticUI.App                 # Blazor Server UI/host
├── ElBruno.MagenticUI.Agents              # Orchestrator, agents, tools
├── ElBruno.MagenticUI.ServiceDefaults     # Aspire service defaults
├── scripts/                               # Optional repo scripts (when needed)
└── tests/
    └── ElBruno.MagenticUI.Agents.Tests    # xUnit tests
```

Repository rule: source code, tests, and scripts must live under `src/`.

- Runnable web app (not a NuGet library)
- Blazor Server UI (no React/Node/npm pipeline)
- Multi-agent orchestration with human-in-the-loop pauses
- Code execution tool uses WSL2 when available

## Model startup behavior

`Program.cs` configures `AddLocalLLMs` with two startup modes:

1. **Explicit model path** (`LocalLLMs:ModelPath` set): use that local ONNX folder.
2. **Auto-download fallback** (`LocalLLMs:ModelPath` empty): enable `EnsureModelDownloaded = true` and download the default model (`phi-3.5-mini-instruct`) on first use.

Optional `LocalLLMs:CacheDirectory` can override where auto-downloaded models are stored.

## Quick start

### Prerequisites

- .NET SDK compatible with this repo's current TFM (`net10.0`)
- WSL2 + Python 3 (for code execution tool support)

### Run

```bash
aspire start
```

### Build and test

```bash
dotnet build ElBruno.MagenticUI.slnx -v minimal
dotnet test ElBruno.MagenticUI.slnx -v minimal
```

## License

MIT — see [LICENSE](LICENSE)

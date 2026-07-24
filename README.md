# ElBruno.MagenticUI

[![CI Build](https://github.com/elbruno/ElBruno.MagenticUI/actions/workflows/build.yml/badge.svg)](https://github.com/elbruno/ElBruno.MagenticUI/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Blazor Server port of [microsoft/magentic-ui](https://github.com/microsoft/magentic-ui), running local-first with ONNX models through [ElBruno.LocalLLMs](https://github.com/elbruno/ElBruno.LocalLLMs).

## What this repo is

- **Type:** Runnable .NET 8 web app (not a NuGet library)
- **Frontend:** Blazor Server (no React, no Node.js, no npm)
- **Inference:** Local ONNX via `ElBruno.LocalLLMs` NuGet package
- **Flow:** Multi-agent orchestration with human-in-the-loop pauses

## Architecture

```text
src/
├── ElBruno.MagenticUI.App        # Blazor Server host
├── ElBruno.MagenticUI.Agents     # Orchestrator, agents, tools
└── tests/
    └── ElBruno.MagenticUI.Agents.Tests
```

## Quick start

### Prerequisites

- .NET 8 SDK
- WSL2 + Python 3 (for code execution tooling)
- A local ONNX model supported by ElBruno.LocalLLMs

### Run

```bash
cd src/ElBruno.MagenticUI.App
dotnet run
```

### Build and test

```bash
dotnet restore ElBruno.MagenticUI.slnx
dotnet build ElBruno.MagenticUI.slnx
dotnet test ElBruno.MagenticUI.slnx --framework net8.0
```

## Documentation policy

- All project documentation must live under `docs/` or feature-local folders (for example `src/<project>/` when tightly coupled to code).
- At repository root, only `README.md` and `LICENSE` are allowed as documentation files.

See [`docs/`](docs/) for documentation index.

## License

MIT — see [LICENSE](LICENSE)

# ElBruno.MagenticUI

[![CI Build](https://github.com/elbruno/ElBruno.MagenticUI/actions/workflows/build.yml/badge.svg)](https://github.com/elbruno/ElBruno.MagenticUI/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## A Blazor Server port of microsoft/magentic-ui for local LLMs 🤖

A real-time multi-agent web UI built with ASP.NET Core Blazor Server and SignalR, powered by [ElBruno.LocalLLMs](https://github.com/elbruno/ElBruno.LocalLLMs) for local ONNX model inference. Inspired by [microsoft/magentic-ui](https://github.com/microsoft/magentic-ui).

## Features

- 🔄 Real-time agent execution via Blazor Server (no JavaScript framework needed)
- 🤝 Human-in-the-loop: pause orchestration for user clarification
- 🐍 WSL2 code execution sandbox (Python)
- 🌐 Web content fetching with Markdown conversion
- 📁 Sandboxed file operations
- 🧠 Powered by local ONNX models (MagenticBrain, Fara1.5-9B, Qwen3, and more)

## Architecture

```
ElBruno.MagenticUI.App  (Blazor Server, real-time UI)
  └── ElBruno.MagenticUI.Agents  (orchestrator, agents, tools)
        └── ElBruno.LocalLLMs   (ONNX inference)
```

## Getting Started

### Prerequisites
- .NET 8 SDK or later
- WSL2 + Python 3 (for code execution)
- An ONNX model supported by ElBruno.LocalLLMs (e.g. MagenticBrain)

### Run
```bash
cd src/ElBruno.MagenticUI.App
dotnet run
```

Open https://localhost:5001 in your browser.

## Building from Source

```bash
dotnet restore ElBruno.MagenticUI.slnx
dotnet build ElBruno.MagenticUI.slnx
dotnet test ElBruno.MagenticUI.slnx --framework net8.0
```

## Roadmap
- [ ] Port MagenticUIOrchestrator + agents from ElBruno.LocalLLMs samples
- [ ] Blazor real-time task panel (replaces React frontend)
- [ ] Screenshot approval UI
- [ ] Multi-model support picker

## Documentation
See the [`docs/`](docs/) folder for architecture and agent porting guides.

## Author
**Bruno Capuano (ElBruno)**
- Blog: https://elbruno.com
- YouTube: https://youtube.com/@inthelabs
- LinkedIn: https://linkedin.com/in/inthelabs
- Twitter: https://twitter.com/inthelabs
- Podcast: https://inthelabs.dev

## License
MIT — see [LICENSE](LICENSE)

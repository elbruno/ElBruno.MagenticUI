# Project Context

- **Owner:** Copilot
- **Project:** ElBruno.MagenticUI
- **Stack:** .NET 10, Blazor Server, ElBruno.LocalLLMs, xUnit
- **Created:** 2026-07-24T09:58:05.134-04:00

## Learnings

- Tests must avoid real WSL2 and real ONNX model inference; use fakes/mocks for coverage.
- 2026-07-29T16:10:53.7840230-04:00: Browser E2E reproduced the startup stall twice: no agent messages or web fetch, and Cancel left backend state Running for more than 43.8 seconds despite a healthy circuit. Regression coverage should use fake loaders plus bounded repeated startup and cancellation checks.

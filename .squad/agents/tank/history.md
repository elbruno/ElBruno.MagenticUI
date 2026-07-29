# Project Context

- **Owner:** Copilot
- **Project:** ElBruno.MagenticUI
- **Stack:** .NET 10, Blazor Server, ElBruno.LocalLLMs, xUnit
- **Created:** 2026-07-24T09:58:05.134-04:00

## Learnings

- Local path `ProjectReference` to ElBruno.LocalLLMs is being replaced by NuGet package version 0.20.0.
- 2026-07-29T16:10:53.7840230-04:00: A stalled task had a healthy Aspire resource but blocked before orchestrator `RunAsync` while unused Fara initialization/download began. An unbounded synchronous `CancellationToken.None` path was identified; the exact low-level blocking mechanism remains unproven.

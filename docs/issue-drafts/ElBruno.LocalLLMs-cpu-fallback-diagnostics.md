# Draft issue for `elbruno/ElBruno.LocalLLMs`

## Status

Filed: https://github.com/elbruno/ElBruno.LocalLLMs/issues/36

## Proposed title
`ExecutionProvider=Auto falls back to CPU without clear surfaced provider/fallback diagnostics`

## Repository
`https://github.com/elbruno/ElBruno.LocalLLMs`

## Summary
When using `ExecutionProvider: Auto`, model initialization/run can fall back to CPU if DirectML/CUDA runtime dependencies are unavailable. This behavior is expected, but it is currently hard to determine **which provider was actually selected** and **why fallback occurred** from the app-level integration.

A clearer diagnostics surface in `ElBruno.LocalLLMs` would improve troubleshooting and user expectations.

## Why this matters
- Users assume GPU acceleration is active when `Auto` is configured.
- In practice, local environments can miss provider prerequisites (DirectML/CUDA/cuDNN/runtime bits), resulting in CPU fallback.
- Without explicit selected-provider + fallback-reason visibility, teams spend time inferring behavior from latency and logs.

## Environment
- OS: Windows
- App stack: .NET 10 + Blazor Server + Aspire + `ElBruno.LocalLLMs`
- Package version observed: `ElBruno.LocalLLMs` `0.20.4`
- Configuration used:
  - `LocalLLMs:ExecutionProvider = Auto`

## Observed behavior
- Tasks run correctly, but execution appears CPU-bound.
- No strongly surfaced runtime signal from integration layer indicating:
  1. selected provider (CPU/DirectML/CUDA)
  2. fallback reason if GPU provider is unavailable

## Expected behavior
A stable, consumable diagnostics signal from `ElBruno.LocalLLMs` that reports:
1. requested provider mode (`Auto`, `Cpu`, `DirectML`, `Cuda`)
2. provider selected at runtime
3. fallback path and reason (if any)
4. optionally, lightweight capability check result before expensive model init

## Suggested improvements
1. **Provider selection diagnostics API**
   - Expose selected provider and fallback reason in a structured object.
   - Make it available immediately after client creation/init.

2. **Optional preflight capability check**
   - Return a result indicating which providers are available and why unavailable ones failed.

3. **Event/log hooks**
   - Add explicit events/log entries for:
     - attempted provider list
     - provider selected
     - fallback reason

4. **Optional strict mode**
   - If configured (e.g., `RequireGpu=true`), fail fast instead of silently falling back.

## Minimal repro shape
1. Configure `ExecutionProvider = Auto`.
2. Run on Windows environment without full DirectML/CUDA prerequisites.
3. Initialize local chat/vision client and run inference.
4. Observe inference succeeds but effective provider is not clearly surfaced to integration callers.

## Notes
We already implemented app-level docs clarifying that `Auto` may fallback to CPU. This issue requests stronger **library-level diagnostics** so applications can present clear runtime status to end users.

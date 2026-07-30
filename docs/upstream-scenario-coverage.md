# Upstream Scenario Coverage (Magentic-UI Alignment)

This document maps upstream Magentic-UI/MagenticLite demo scenarios to this
`.NET` port (`ElBruno.MagenticUI`) and defines what we should cover next.

## Scope and sources

Primary upstream baseline:

- `microsoft/magentic-ui` `main` (MagenticLite 0.2)

Historical reference (not parity target):

- `microsoft/magentic-ui` `magentic-ui-0.1`

Evidence URLs:

- Main README: <https://github.com/microsoft/magentic-ui/blob/main/README.md>
- Legacy 0.1 branch (README content and demos):
  <https://github.com/microsoft/magentic-ui/tree/magentic-ui-0.1>
- MagenticLite research release blog:
  <https://www.microsoft.com/en-us/research/blog/magenticlite-magenticbrain-fara1-5-an-agentic-experience-optimized-for-small-models/>

## Embedded browser status (upstream)

Upstream demos are browser-centric and include a live browser experience:

- `main` README states MagenticLite works across browser and local files.
- Research blog describes updated browser/chat views and a `Live Browser`
  component in the architecture.
- `0.1` README demos and feature list describe web automation, co-tasking,
  and user intervention during browser actions.

For this repository, browser-task parity means we should cover browser workflows in
our scenario set (with local-first guardrails and reproducible targets).

## Upstream scenario inventory

### MagenticLite 0.2 (`main`) demos

| Upstream demo | Task type | Notes |
|---|---|---|
| Fill expense forms | Browser form workflow | Human-in-the-loop sensitive action pattern |
| Find prices for recipe ingredients | Browser research/comparison | Multi-site fetch and synthesis |
| Find and book a restaurant | Browser navigation + booking | Often credentialed/irreversible steps |
| Organize local files | Local filesystem workflow | Strong overlap with FileSurfer category |

### Magentic-UI 0.1 demos (historical)

| Upstream demo | Task type | Notes |
|---|---|---|
| Pizza ordering web automation | Browser transactional flow | Irreversible action risk |
| Airbnb price analysis (MCP) | MCP + browser/data task | Requires MCP setup and deterministic target |
| Star monitoring long-running task | Monitoring/long-running automation | Multi-turn persistence over time |

## Local coverage matrix

Current local scenarios are documented in `docs/tutorial.md`:

- Scenario 1: summarize a web page
- Scenario 2: analyze files in sandbox
- Scenario 3: analyze image with computer-use model

| Upstream scenario | Current local status | Closest local scenario | Gap summary |
|---|---|---|---|
| Fill expense forms | **Partially covered** | Scenario 3 (computer-use), Scenario 1 (web) | Browser form-filling workflow is not yet a dedicated validated tutorial scenario |
| Find prices for recipe ingredients | **Partially covered** | Scenario 1 | We cover basic web summarization, not structured multi-source price comparison |
| Find and book a restaurant | **Missing** | None (closest: Scenario 1) | Booking-style browser flow not yet modeled/tested |
| Organize local files | **Partially covered** | Scenario 2 | We read/compute over files; we do not yet cover richer "organize" actions as an end-to-end story |
| Pizza ordering automation (0.1) | **Missing (legacy reference)** | None | Legacy transactional web scenario; defer unless explicit 0.1 parity requested |
| Airbnb MCP analysis (0.1) | **Missing (legacy reference)** | None | Requires MCP tutorial track and deterministic backend setup |
| Star monitoring long-running (0.1) | **Missing (legacy reference)** | None | Requires explicit long-running/monitoring workflow and pause/resume guidance |

## Prioritized scenario additions

### P1 (add first: reproducible and local-first compatible)

1. **Browser price comparison task**
   - Prompt asks for ingredient price comparison across selected public pages.
   - Expected output: normalized table + recommendation summary.
2. **File organization scenario**
   - Extend Scenario 2 class with move/rename/summary output in working directory.
   - Expected output: deterministic file tree and generated summary file.

### Current execution status

As of `2026-07-30`, P1 scenario execution plans are documented in:

- [Scenario Playbooks (P1)](./tutorial-scenario-playbooks.md)

Status:

- Browser price comparison: **Validation attempted (2026-07-30); hit max rounds without final answer**
- File organization workflow: **Validation attempted (2026-07-30); required cancellation before final answer**
- File organization workflow (re-validation after timeout hardening):
   **still blocked before first orchestrator round in latest observed run**

Engineering state:

- Timeout hardening and fallback regressions added in orchestrator tests
- Agent suite green after changes: **64/64 passed**

Evidence artifacts:

- `images/tutorial-scenario4-price-compare-running.png`
- `images/tutorial-scenario4-price-compare-stuck.png`
- `images/tutorial-scenario4-price-compare-done.png`
- `images/tutorial-scenario5-file-organize-running.png`
- `images/tutorial-scenario5-file-organize-stuck.png`
- `images/tutorial-scenario5-file-organize-done.png`

### P2 (after P1 stability)

1. **Form-filling style browser workflow**
   - Prefer sandbox/demo forms site with no real account dependency.
2. **Booking-like navigation workflow**
   - Use deterministic mock/sandbox target, avoid irreversible real submissions.

### P3 (defer / opt-in)

1. **Credentialed transactional flows**
   - Requires strict guardrails and explicit user-provided test accounts.
2. **Legacy 0.1 parity scenarios (MCP Airbnb, star monitoring)**
   - Keep as optional roadmap unless requested.

## Acceptance criteria template for new scenarios

For each new scenario, define and verify:

1. **Prompt** (exact text in tutorial)
2. **Expected tool path** (e.g., WebFetcher / FileSurfer / Computer / Coder)
3. **Completion criteria** (final `submit` shape and deterministic checks)
4. **Evidence artifacts** (screenshot file in `images/`)
5. **Known failure signatures** (add cross-link to troubleshooting)

## Implementation notes for this repo

- Keep scenario parity as **category parity first** (browser research, forms,
  booking-like navigation, file organization), not strict one-to-one clone of
  upstream demo names.
- Use deterministic/sandbox-friendly targets where possible to keep tutorial runs
  reproducible.
- Track current computer-use runtime limitation in Scenario 3:
  `CausalConvWithState ... not a registered function/op` (see
  `docs/tutorial-troubleshooting.md`).

# Scenario Playbooks (Phase P1)

This guide turns the P1 backlog into reproducible, testable runs.

- Coverage source: [Upstream Scenario Coverage](./upstream-scenario-coverage.md)
- Base runtime tutorial: [Getting Started Tutorial](./tutorial.md)
- Failure handling: [Troubleshooting](./tutorial-troubleshooting.md)

---

## Playbook A — Browser Price Comparison (Ingredients)

**Parity target:** Upstream category _Find prices for recipe ingredients_

### Prompt (copy/paste)

Use this exact prompt in the **Tasks** page:

```text
Compare prices for these ingredients using the provided pages only:
- olive oil
- parmesan cheese
- spaghetti

URLs to use:
1) https://www.walmart.com/search?q=olive+oil
2) https://www.walmart.com/search?q=parmesan+cheese
3) https://www.walmart.com/search?q=spaghetti

Requirements:
- For each ingredient, provide one representative price visible on the page.
- Return a markdown table with columns: Ingredient, Price, Source URL.
- Add a short recommendation: "lowest observed total" basket note.
- If any page cannot be fetched, explicitly mark that row as "unavailable".
Return only the final answer.
```

### Expected tool path

Typical successful run:

1. `WebFetcher_FetchUrl` for each URL (or equivalent fetch calls)
2. Orchestrator synthesis of extracted price text
3. Final `submit`

### Completion criteria

A run is considered **Done** when all of the following are true:

- Status badge ends at **Done ✓**
- Final `submit` includes a 3-row markdown table:
  - `Ingredient`
  - `Price`
  - `Source URL`
- Each row has either:
  - price-like value (contains currency symbol/amount), or
  - explicit `unavailable`
- A one-sentence recommendation is present

### Screenshot checkpoints

Capture and store these screenshots in `images/`:

- `tutorial-scenario4-price-compare-running.png`
  - Feed shows at least one `WebFetcher_FetchUrl`
- `tutorial-scenario4-price-compare-done.png`
  - Final `submit` table visible and status is **Done ✓**

### Validation result (2026-07-30)

Observed behavior in this environment:

- Run started and remained in **Running…** with no tool-call messages for most of the run.
- Final observed system outcome was:
  - `Reached maximum rounds (15) without a final answer.`
- We captured reproducible evidence:
  - `tutorial-scenario4-price-compare-running.png`
  - `tutorial-scenario4-price-compare-stuck.png`
  - `tutorial-scenario4-price-compare-done.png` (terminal state after run end)

Status: **Partially validated (execution path issue; no final answer returned)**.

---

## Playbook B — File Organization + Summary Output

**Parity target:** Upstream category _Organize local files_

### Setup

Prepare deterministic sandbox files:

```powershell
$sandbox = "$env:TEMP\magentic-sandbox"
New-Item $sandbox -ItemType Directory -Force | Out-Null

"Q1 notes" | Set-Content "$sandbox\notes_q1.txt"
"Q2 notes" | Set-Content "$sandbox\notes_q2.txt"
"Invoice #1001 - Amount: $250.00 - Client: Contoso" | Set-Content "$sandbox\invoice_1001.txt"
"Invoice #1002 - Amount: $175.50 - Client: Fabrikam" | Set-Content "$sandbox\invoice_1002.txt"
```

### Prompt (copy/paste)

```text
In the working directory:
1) Create folders named notes and invoices if they do not exist.
2) Move *.txt files starting with notes_ into notes/.
3) Move *.txt files starting with invoice_ into invoices/.
4) Read the moved files and create summary.txt in the root working directory with:
   - file counts per folder
   - invoice total amount
   - list of note filenames
5) Show the final directory tree in the answer.
Return only the final answer.
```

### Expected tool path

Typical successful run includes a subset of:

- `FileSurfer_ListDirectory`
- `FileSurfer_CreateDirectory` (or write-call that creates folders)
- `FileSurfer_MoveFile` / `FileSurfer_RenameFile`
- `FileSurfer_ReadFile`
- `FileSurfer_WriteFile` for `summary.txt`
- final `submit`

### Completion criteria

A run is considered **Done** when all checks pass:

- Status badge ends at **Done ✓**
- Final answer contains:
  - `notes/` and `invoices/` folder presence
  - `summary.txt` in root working directory
  - invoice total amount = `$425.50` for setup above
- `summary.txt` includes:
  - file counts per folder
  - note filenames (`notes_q1.txt`, `notes_q2.txt`)

### Screenshot checkpoints

Capture and store these screenshots in `images/`:

- `tutorial-scenario5-file-organize-running.png`
  - Feed shows file operations (`ListDirectory`, `ReadFile`, write/move calls)
- `tutorial-scenario5-file-organize-done.png`
  - Final `submit` and **Done ✓**

### Validation result (2026-07-30)

Observed behavior in this environment:

- Run started and remained in **Running…** with only the initial
  `Orchestrator/system: Task received` message.
- No file-operation tool messages were emitted during the observed run window.
- Run required manual cancellation and ended with:
  - `Task cancelled.`
- Evidence captured:
  - `tutorial-scenario5-file-organize-running.png`
  - `tutorial-scenario5-file-organize-stuck.png`
  - `tutorial-scenario5-file-organize-done.png` (terminal state after cancellation)

Status: **Partially validated (execution path issue; cancelled before final answer)**.

### Re-validation update (post-timeout hardening, 2026-07-30)

After adding orchestrator streaming/non-streaming timeout guards and file-operation
tool coverage improvements, we reran Scenario 5.

Observed behavior in this environment:

- Task entered **Running…** state but still showed `Agent Messages: 0`.
- App logs reached provider fallback (`DirectML` → `Cuda` → `Cpu`) and then
  emitted no further orchestration messages in the observed window.
- Because no first orchestrator message was emitted, the new response-timeout
  fallback path was not exercised in this run.

Status: **Still partially validated (blocked before first orchestrator round)**.

Engineering verification completed:

- Agent test suite now passes with timeout hardening and new regression tests:
  `64 passed, 0 failed`.

---

## Recording results in docs

After each validated run:

1. Add screenshot references to `docs/tutorial.md` (new scenario sections)
2. If failures occurred, add signatures/fixes to `docs/tutorial-troubleshooting.md`
3. Update status in `docs/upstream-scenario-coverage.md` from **Partial/Missing** to
   **Covered** where appropriate

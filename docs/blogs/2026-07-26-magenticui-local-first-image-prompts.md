# Image assets for `2026-07-26-magenticui-local-first-dotnet.md`

This post now uses fewer images for a cleaner educational flow.

## A) Generated with `t2i`

Output file: `magenticui-architecture-comic-small.png`  
Source generated file: `magenticui-local-first-comic-01-architecture.png`

Generation settings:
- Tool: `t2i`
- Provider: `foundry-mai25`
- Size: `1024x1024`
- Steps: `30`

Prompt:

```text
Comic-book style technical illustration, split scene with three layers from top to bottom: Blazor Server UI, Agents orchestration layer, and local ONNX inference engine, arrows flowing between layers, developer at laptop, vibrant colors, clean line art, modern, no text, no logos, no watermark
```

Resize applied after generation:
- Source size: `1024x1024`
- Final size: `640x640`

## B) Reused reference image (provided by user)

Output file: `magenticlite-magenticbrain-fara-process.png`

Source:
- User-provided attachment from Microsoft Research blog:  
  `https://www.microsoft.com/en-us/research/blog/magenticlite-magenticbrain-fara1-5-an-agentic-experience-optimized-for-small-models/`

Resize applied:
- Source size: `1556x875`
- Final size: `960x540`

## C) Runtime screenshots captured while app was running

These are real screenshots (not generated) captured with `playwright-cli` after starting Aspire.

Aspire start:

```bash
aspire start
```

App screenshot output file: `magenticui-app-running.png`  
Dashboard screenshot output file: `aspire-dashboard-genai-trace.png`

Automation commands used:

```bash
playwright-cli open http://localhost:5258
playwright-cli resize 1600 1000
playwright-cli fill "textarea.task-input" "List three key architecture points of this app."
playwright-cli click "button:has-text('Start Task')"
playwright-cli run-code "async page => { await page.waitForTimeout(12000); }"
playwright-cli screenshot --filename=docs/blogs/magenticui-app-running.png
playwright-cli goto "https://localhost:17175/login?t=<token>"
playwright-cli run-code "async page => { await page.waitForTimeout(3000); const traces = page.getByText('Traces', { exact: false }); if (await traces.count()) { await traces.first().click(); await page.waitForTimeout(5000); } }"
playwright-cli screenshot --filename=docs/blogs/aspire-dashboard-genai-trace.png
playwright-cli close
```

Final resize applied:
- `magenticui-app-running.png`: resized to `1200x750`
- `aspire-dashboard-genai-trace.png`: resized to `1200x750`

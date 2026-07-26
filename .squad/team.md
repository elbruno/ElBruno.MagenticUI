# Squad Team

> ElBruno.MagenticUI

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Morpheus | Lead Architect | .squad/agents/morpheus/charter.md | 🏗️ Lead |
| Neo | Agents/Core Dev | .squad/agents/neo/charter.md | 🔧 Backend |
| Trinity | Blazor App Dev | .squad/agents/trinity/charter.md | ⚛️ Frontend |
| Tank | Runtime/Integration Dev | .squad/agents/tank/charter.md | ⚙️ Platform |
| Switch | Test Engineer | .squad/agents/switch/charter.md | 🧪 QA |
| Dozer | Docs & Onboarding Experience | .squad/agents/dozer/charter.md | 📝 Docs |
| Mouse | Content & Social Storyteller | .squad/agents/mouse/charter.md | 📝 DevRel |
| Scribe | Session Logger | .squad/agents/scribe/charter.md | 📋 Scribe |
| Ralph | Work Monitor | .squad/agents/ralph/charter.md | 🔄 Ralph |
| Rai | RAI Reviewer | .squad/agents/rai/charter.md | 🛡️ RAI |
| Fact Checker | Fact Checker | .squad/agents/fact-checker/charter.md | 🔍 Verifier |


## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage (adding missing tests, fixing flaky tests)
- Lint/format fixes and code style cleanup
- Dependency updates and version bumps
- Small isolated features with clear specs
- Boilerplate/scaffolding generation
- Documentation fixes and README updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear specs and acceptance criteria
- Refactoring with existing test coverage
- API endpoint additions following established patterns
- Migration scripts with well-defined schemas

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Multi-system integration requiring coordination
- Ambiguous requirements needing clarification
- Security-critical changes (auth, encryption, access control)
- Performance-critical paths requiring benchmarking
- Changes requiring cross-team discussion

## Project Context

- **Project:** ElBruno.MagenticUI
- **Owner:** Copilot
- **Created:** 2026-07-24
- **Stack:** .NET 10, Blazor Server, ElBruno.LocalLLMs (ONNX Runtime GenAI), xUnit
- **Scope:** Phase 3C P1-P5, porting from C:\src\ElBruno.LocalLLMs\src\samples\MagenticUIServer\

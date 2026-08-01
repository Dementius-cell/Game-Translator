# Domain Docs

How engineering skills should consume this repo's domain documentation and local instruction files when exploring the codebase.

## Before exploring, read these

- `docs/00-project-constitution.md`
- `docs/01-technical-specification.md`
- `docs/02-architecture.md`
- `docs/03-technical-risks-and-decisions.md`
- `docs/04-implementation-roadmap.md`
- `docs/06-sprint-development-plan.md`
- `docs/07-definition-of-done-quality-gates.md`
- `docs/05-ai-development-rules.md`
- `docs/evidence-artifacts.md` when the task cites generated screenshots, scorecards, debug reports, local harnesses, or built binaries
- `docs/adr/README.md` when the task touches architecture or previously accepted decisions
- `docs/governance/change-approval-required.md` when the task may require explicit project-owner approval

If a future `CONTEXT.md` is added, read it before implementation work and use its vocabulary for issue titles, test names, refactor proposals, and design notes.
This is an optional future-file rule, not a current required file in the repository.

## Local instruction structure

This repo uses a small `AGENTS.md` hierarchy. Read the root file first, then the child instruction files that match the path you will touch.

```text
/
|-- AGENTS.md
|-- README.md
|-- docs/
|   |-- AGENTS.md
|   |-- README.md
|   |-- 00-project-constitution.md
|   |-- 01-technical-specification.md
|   |-- 02-architecture.md
|   |-- 03-technical-risks-and-decisions.md
|   |-- 04-implementation-roadmap.md
|   |-- 05-ai-development-rules.md
|   |-- 06-sprint-development-plan.md
|   |-- 07-definition-of-done-quality-gates.md
|   |-- adr/
|   |-- agents/
|   |-- ai/
|   |-- governance/
|   `-- prompts/
|-- src/
|   |-- AGENTS.md
|   |-- GameTranslator.Application/
|   |   `-- AGENTS.md
|   |-- GameTranslator.Domain/
|   |   `-- AGENTS.md
|   |-- GameTranslator.Infrastructure/
|   |   `-- AGENTS.md
|   `-- GameTranslator.UI/
|       `-- AGENTS.md
`-- tests/
    |-- AGENTS.md
    `-- GameTranslator.Tests/
        `-- Calibration/
            `-- AGENTS.md
```

## Use project vocabulary

When output names a project concept, prefer terms already used in the technical specification, architecture document, roadmap, sprint plan, and ADRs. Do not invent alternate names for established concepts such as OCR zones, overlay, translation cache, game profiles, Windows Graphics Capture, Clean Architecture, MVVM, or Windows Credential Manager.

## Flag ADR conflicts

If output contradicts an existing ADR, surface it explicitly rather than silently overriding it:

> Contradicts `docs/adr/README.md` ADR-00X, but may be worth reopening because...

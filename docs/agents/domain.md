# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- `docs/00-project-constitution.md`
- `docs/01-technical-specification.md`
- `docs/02-architecture.md`
- `docs/03-technical-risks-and-decisions.md`
- `docs/04-implementation-roadmap.md`
- `docs/06-sprint-development-plan.md`
- `docs/07-definition-of-done-quality-gates.md`
- `docs/05-ai-development-rules.md`
- `docs/adr/README.md` when the task touches architecture or previously accepted decisions
- `docs/governance/change-approval-required.md` when the task may require explicit project-owner approval

If a future `CONTEXT.md` is added, read it before implementation work and use its vocabulary for issue titles, test names, refactor proposals, and design notes.

## File structure

This is currently a single-context repo:

```text
/
|-- AGENTS.md
|-- README.md
|-- docs/
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
```

## Use project vocabulary

When output names a project concept, prefer terms already used in the technical specification, architecture document, roadmap, sprint plan, and ADRs. Do not invent alternate names for established concepts such as OCR zones, overlay, translation cache, game profiles, Windows Graphics Capture, Clean Architecture, MVVM, or Windows Credential Manager.

## Flag ADR conflicts

If output contradicts an existing ADR, surface it explicitly rather than silently overriding it:

> Contradicts `docs/adr/README.md` ADR-00X, but may be worth reopening because...

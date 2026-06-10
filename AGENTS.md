# AGENTS.md

These instructions apply to all AI-agent work in this repository.

## Required Reading

Before changing code or documentation, read the source-of-truth documents in this order:

1. `docs/00-project-constitution.md`
2. `docs/01-technical-specification.md`
3. `docs/02-architecture.md`
4. `docs/03-technical-risks-and-decisions.md`
5. `docs/04-implementation-roadmap.md`
6. `docs/06-sprint-development-plan.md`
7. `docs/07-definition-of-done-quality-gates.md`
8. `docs/05-ai-development-rules.md`

For architecture changes, also read `docs/adr/README.md`.
For restricted changes, read `docs/governance/change-approval-required.md` and obtain explicit project-owner approval before proceeding.

## Current Stage

- Roadmap stage: Stage 0, project preparation.
- Sprint: Sprint 0, project initialization.
- Target result: an empty WPF application that compiles and runs without OCR, translation, screen capture, or overlay functionality.

## Non-Negotiable Constraints

- Use C# and WPF.
- Follow Clean Architecture and MVVM.
- Keep `Domain` independent from UI, infrastructure, and external frameworks.
- Do not create direct UI-to-Infrastructure dependencies.
- Do not inject into game processes.
- Do not read or modify game memory.
- Do not bypass anti-cheat systems.
- Do not use DLL injection.
- Store secrets only through Windows Credential Manager.

## Work Rules

- Follow the roadmap and sprint order unless the project owner explicitly approves a change.
- Keep changes minimal and tied to the current sprint.
- Add or update tests for new behavior according to the quality gates.
- Report changed files, created files, validation performed, discovered risks, remaining work, and the next step after each task.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `Dementius-cell/Game-Translator`. See `docs/agents/issue-tracker.md`.

### Triage labels

The repository uses the standard mattpocock/skills triage label vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository with project-wide documentation under `docs/` and ADRs under `docs/adr/`. See `docs/agents/domain.md`.

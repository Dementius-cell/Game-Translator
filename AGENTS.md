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

## Local Instruction Files

This repository uses nested `AGENTS.md` files for area-specific rules. Before changing files in a scoped area, read the root instructions first, then the matching child instruction files:

- For changes under `tests/**`, also read `tests/AGENTS.md`.
- For calibration or golden-reference work under `tests/GameTranslator.Tests/Calibration/**`, also read `tests/GameTranslator.Tests/Calibration/AGENTS.md`.

Child instruction files extend these root rules within their directory scope. Calibration tests may use approved fixture data and generated evidence, but a passing calibration test is evidence only and must not be treated as production behavior without explicit approval.

## Project Status Source

Do not treat this file as the current sprint/status source. The project has progressed beyond initial scaffolding; use GitHub Issues, current handoff docs, and the roadmap/sprint documents to determine active work before editing. If these disagree, report the conflict before proceeding.

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
- When posting or editing multi-line GitHub issue/PR comments, do not pass Markdown through inline shell strings or `gh ... --body` in PowerShell. Write the comment body to a UTF-8 file and use `--body-file`, or send JSON from a file through the GitHub API, then verify the published body with `gh issue view --json comments` or `gh api` before reporting success.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `Dementius-cell/Game-Translator`. See `docs/agents/issue-tracker.md`.

### Triage labels

The repository uses the standard mattpocock/skills triage label vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository with project-wide documentation under `docs/` and ADRs under `docs/adr/`. See `docs/agents/domain.md`.

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
For restricted or decision-level changes, read `docs/governance/change-approval-required.md`. Hard-stop changes and new decision records require explicit project-owner approval; implementation within an accepted ADR is normal delivery.

## Local Instruction Files

This repository uses nested `AGENTS.md` files for area-specific rules. Before changing files in a scoped area, read the root instructions first, then the matching child instruction files:

- For changes under `tests/**`, also read `tests/AGENTS.md`.
- For calibration or golden-reference work under `tests/GameTranslator.Tests/Calibration/**`, also read `tests/GameTranslator.Tests/Calibration/AGENTS.md`.

Child instruction files extend these root rules within their directory scope. Calibration tests may use approved fixture data and generated evidence, but a passing calibration test is evidence only and must not be treated as production behavior without explicit approval.

## Project Status Source

Do not treat this file as the current sprint/status source. The project has progressed beyond initial scaffolding; use GitHub Issues and their explicit dependency graph as the current delivery source. The roadmap and sprint documents preserve scope and historical context. If current Issue dependencies and a task request disagree, report the conflict before proceeding.

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

- Follow the current GitHub Issue dependency graph. The historical roadmap does not block independent fixes, tests, documentation, or compatible improvements.
- Keep changes minimal and tied to the current sprint.
- Add or update tests for changed behavior according to the applicable quality gates, and record justified N/A gates in the final report when relevant.
- When adding documentation references to generated screenshots, scorecards, debug reports, harnesses, or built binaries, label whether the path is tracked, generated/reproducible, local-only, ignored output, or a build output. Follow `docs/evidence-artifacts.md`.
- Report changed files, created files, validation performed, discovered risks, remaining work, and the next step after each task.
- When posting or editing multi-line GitHub issue/PR comments, do not pass Markdown through inline shell strings or `gh ... --body` in PowerShell. Write the comment body to a UTF-8 file and use `--body-file`, or send JSON from a file through the GitHub API, then verify the published body with `gh issue view --json comments` or `gh api` before reporting success.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `Dementius-cell/Game-Translator`. See `docs/agents/issue-tracker.md`.

### Triage labels

The repository uses the standard mattpocock/skills triage label vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository with project-wide documentation under `docs/` and ADRs under `docs/adr/`. See `docs/agents/domain.md`.

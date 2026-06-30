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

- Roadmap stage: Sprint 26 hardening for beta readiness.
- Sprint: Sprint 26 (#28), with #32 active for vertical CJK overlay placement.
- Target result: stabilize the existing Capture -> OCR -> Grouping -> Translation -> Overlay pipeline for beta use, especially vertical Chinese/Japanese manga-style text placement.
- Do not start Sprint 27 / #29 / #30 work until Sprint 26 is explicitly closed or confirmed by the project owner.

## Current Sprint 26 Contract

- For vertical CJK, use Tesseract OCR only.
- Keep three responsibilities separate:
  - text detection: which OCR blocks are treated as real text;
  - semantic grouping: which OCR blocks are joined for translation;
  - overlay placement: where translated text and masks are drawn.
- Translation may use semantic groups. Masking may use raw OCR blocks within accepted semantic groups. Do not assume one translated item must equal one OCR block.
- Overlay placement should prefer the original bubble/frame/label region and should not drift onto faces, clothing, or UI chrome.
- Diagnostics exports must not include secrets and should include enough grouping, mask, and overlay geometry to debug placement.

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
- Experimental web translators (`GoogleWeb`, `BingWeb`, `YandexWeb`, `WebAuto`) are beta-only and must not become release defaults without explicit approval.
- Do not add scraping or private endpoint behavior without an architecture decision and project-owner approval.

## Work Rules

- Follow the roadmap and sprint order unless the project owner explicitly approves a change.
- Keep changes minimal and tied to the current sprint.
- Add or update tests for new behavior according to the quality gates.
- For SDKs, APIs, CLI tools, and libraries, use Context7 first. If Microsoft WinRT/OCR docs are not available there, fall back only to official Microsoft Learn.
- Report changed files, created files, validation performed, discovered risks, remaining work, and the next step after each task.
- When posting or editing multi-line GitHub issue/PR comments, do not pass Markdown through inline shell strings or `gh ... --body` in PowerShell. Write the comment body to a UTF-8 file and use `--body-file`, or send JSON from a file through the GitHub API, then verify the published body with `gh issue view --json comments` or `gh api` before reporting success.

## Local AGENTS.md / DOX-Style Instructions

This repo uses a lightweight DOX-style instruction tree: a nested `AGENTS.md` file applies to files below its directory and may add local invariants, vocabulary, and documentation rules. Root rules still apply everywhere. Prefer adding local `AGENTS.md` files for stable areas such as pipeline, overlay, OCR infrastructure, UI, and tests rather than overloading this root file.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `Dementius-cell/Game-Translator`. See `docs/agents/issue-tracker.md`.

### Triage labels

The repository uses the standard mattpocock/skills triage label vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repository with project-wide documentation under `docs/` and ADRs under `docs/adr/`. See `docs/agents/domain.md`.

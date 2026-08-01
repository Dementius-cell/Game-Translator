# docs/AGENTS.md

These instructions apply to documentation under `docs/**`.

## Purpose

Keep project documentation concise, current, and operational for humans and AI agents working on Game Translator.

## Ownership

- Source-of-truth project documents live at the top level of `docs/`.
- ADRs live under `docs/adr/`.
- Governance rules live under `docs/governance/`.
- Agent-facing helper docs live under `docs/agents/`.
- Prompts, AI notes, and supporting references stay in their existing scoped folders.

## Local Contracts

- Preserve the source priority defined by the root `AGENTS.md` and `docs/00-project-constitution.md`.
- Do not rewrite an ACCEPTED ADR to change its decision. Create a new ADR that supersedes the old one when a decision changes.
- Read `docs/governance/change-approval-required.md` before changes that may be Hard Stop or Decision Record work.
- Label generated screenshots, scorecards, debug reports, harnesses, and build outputs according to `docs/evidence-artifacts.md`.
- Keep GitHub Issues and their explicit dependency graph as the current delivery source; roadmap and sprint documents are historical scope context unless an issue says otherwise.

## Work Guidance

- Document durable contracts, current workflow, and stable boundaries rather than diary notes.
- Prefer short direct bullets and existing project vocabulary: OCR zones, overlay, translation cache, game profiles, Windows Graphics Capture, Clean Architecture, MVVM, and Windows Credential Manager.
- Remove stale or contradictory text instead of adding historical explanations around it.
- Keep examples and artifact references reproducible, tracked, or clearly labeled as local-only/generated/build output.

## Verification

- For Markdown changes, run `tools/check-docs-mini.ps1` when the changed files contain links, inline code, artifact references, or issue/PR comment guidance.
- For docs-only edits, code build/test gates are normally N/A unless the documentation change modifies generated code, scripts, samples, or build instructions.

## Child Instruction Index

No nested documentation `AGENTS.md` files are currently defined below this directory.

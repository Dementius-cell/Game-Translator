# Game-Translator Documentation

This folder contains the source-of-truth documents for Game-Translator. Read them before changing architecture, implementation order, technology choices, OCR behavior, translation providers, overlay behavior, cache format, profile format, or security-sensitive logic.

## User and Developer Guides

- [User guide](user-guide.md): installation, first launch, OCR packs, providers, cache, hotkeys, and diagnostics.
- [Domain module](../src/GameTranslator.Domain/README.md)
- [Application module](../src/GameTranslator.Application/README.md)
- [Infrastructure module](../src/GameTranslator.Infrastructure/README.md)
- [UI module](../src/GameTranslator.UI/README.md)

## Reading Order

1. [Project Constitution](00-project-constitution.md)
2. [Technical Specification](01-technical-specification.md)
3. [Architecture](02-architecture.md)
4. [Technical Risks and Decisions](03-technical-risks-and-decisions.md)
5. [Implementation Roadmap](04-implementation-roadmap.md)
6. [Sprint Development Plan](06-sprint-development-plan.md)
7. [Definition of Done + Quality Gates](07-definition-of-done-quality-gates.md)
8. [AI Development Rules](05-ai-development-rules.md)

## Governance

- [Architecture Decision Records](adr/README.md)
- [Change Approval Required](governance/change-approval-required.md)
- [Evidence Artifact Policy](evidence-artifacts.md)
- [Tesseract Local Language Data](tesseract-local-data.md)

## AI-Agent Materials

- [Master Prompt](prompts/master-prompt.md)
- [Agent Startup Manifest](ai/agent-startup-manifest.md)

## Smoke Checks

- [Calibration and Smoke Workflow](testing/calibration-and-smoke-workflow.md)
- [Sprint 8 overlay click-through smoke](smoke/sprint-08-overlay-click-through.md)
- [Sprint 9 overlay positioning smoke](smoke/sprint-09-overlay-positioning.md)

## Project Status Source

This README is an index, not the live sprint/status source. Use [GitHub Issues](https://github.com/Dementius-cell/Game-Translator/issues), [Local unpublished worktree status](agents/local-unpublished-worktree-status.md), and [OCR/overlay work status](agents/ocr-overlay-work-status.md), then cross-check against the roadmap and sprint plan. Files under `docs/handoff/` are dated historical records and are not current instructions.

## Evidence Artifacts

Documentation may cite screenshots, scorecards, debug reports, local harnesses, and built binaries. Before treating an inline path as a required file in a clean checkout, check [Evidence Artifact Policy](evidence-artifacts.md) for the artifact category and availability rules.

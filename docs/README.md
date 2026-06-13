# Game-Translator Documentation

This folder contains the source-of-truth documents for Game-Translator. Read them before changing architecture, implementation order, technology choices, OCR behavior, translation providers, overlay behavior, cache format, profile format, or security-sensitive logic.

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

## AI-Agent Materials

- [Master Prompt](prompts/master-prompt.md)
- [Agent Startup Manifest](ai/agent-startup-manifest.md)

## Smoke Checks

- [Sprint 8 overlay click-through smoke](smoke/sprint-08-overlay-click-through.md)

## Current Project State

- Current roadmap stage: Stage 4, Overlay MVP.
- Current sprint: Sprint 8, render a click-through overlay window.
- Required implementation result: transparent always-on-top WPF overlay with test text, show/hide controls, click-through behavior, and no translation pipeline yet.

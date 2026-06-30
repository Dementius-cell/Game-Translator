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
- [Sprint 9 overlay positioning smoke](smoke/sprint-09-overlay-positioning.md)
- [Sprint 26 experimental web translators smoke](smoke/sprint-26-experimental-web-translators.md)

## Design Notes

- [Vertical CJK overlay placement](design/vertical-cjk-overlay-placement.md)
- [Golden reference calibration sandbox](design/golden-reference-calibration.md)

## Current Project State

- Current roadmap stage: Sprint 26 hardening for beta readiness.
- Current sprint: Sprint 26 (#28), with #32 active for vertical CJK overlay placement.
- Required implementation result: stabilize the existing Capture -> OCR -> Grouping -> Translation -> Overlay pipeline for beta use.
- Do not start Sprint 27 / #29 / #30 work until Sprint 26 is explicitly closed or confirmed by the project owner.
- Current placement priority: vertical Chinese/Japanese manga text should translate as semantic groups, mask only accepted source text, and place translated overlays inside the original bubble/frame/label whenever possible.

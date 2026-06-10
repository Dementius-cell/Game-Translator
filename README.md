# Game-Translator

Game-Translator is a planned Windows 11 desktop application for real-time OCR and translation overlay in games.

The project is currently at Sprint 0: repository and documentation initialization. The implementation must follow the project constitution, architecture decisions, roadmap, quality gates, and AI development rules stored in `docs/`.

## Documentation

Start here:

- [Documentation index](docs/README.md)
- [Project Constitution](docs/00-project-constitution.md)
- [Technical Specification](docs/01-technical-specification.md)
- [Architecture](docs/02-architecture.md)
- [Implementation Roadmap](docs/04-implementation-roadmap.md)
- [Sprint Plan](docs/06-sprint-development-plan.md)
- [Definition of Done + Quality Gates](docs/07-definition-of-done-quality-gates.md)
- [AI Development Rules](docs/05-ai-development-rules.md)

Governance and AI-agent materials:

- [Architecture Decision Records](docs/adr/README.md)
- [Change Approval Required](docs/governance/change-approval-required.md)
- [Master Prompt](docs/prompts/master-prompt.md)
- [Agent Startup Manifest](docs/ai/agent-startup-manifest.md)

## Required Direction

- Language: C#
- UI: WPF
- Architecture: Clean Architecture + MVVM
- Capture: Windows Graphics Capture
- OCR: Windows OCR first, Tesseract for vertical Japanese/Chinese text
- Translation cache: SQLite
- Secrets: Windows Credential Manager

The application must not inject into game processes, read game memory, bypass anti-cheat systems, or use DLL injection.

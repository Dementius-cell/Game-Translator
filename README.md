# Game-Translator

Game-Translator is a Windows 11 desktop application for real-time screen OCR and translation overlays in games, comics, and other bounded on-screen regions. The solution contains four production modules (`Domain`, `Application`, `Infrastructure`, and `UI`) plus one verification project (`GameTranslator.Tests`).

The current normal pipeline is: manual OCR zone → GPU Paddle text detection → bounded writing-system grouping → Tesseract crop recognition → the explicitly selected translator → per-region overlay. Windows OCR and Tesseract remain supported OCR engines; the detector worker is an Infrastructure adapter, not a fifth product module.

For installation, first-run setup, OCR language packs, providers, cache, hotkeys, and diagnostics, see the [user guide](docs/user-guide.md).

## Reproducible live-Paddle build

The source repository deliberately excludes third-party game/manga screenshots, local OCR/translation reports, release candidates, Paddle model files, and Tesseract language data. It does contain a small tracked deterministic calibration set; generated evidence and owner-provided source material remain local-only under ignored `artifacts/`, `work/`, `outputs/`, application-data, and `tessdata/` paths unless the owner explicitly promotes a specific artifact.

To assemble the current ADR-030 live-Paddle package class, use a Windows x64 host with:

- .NET SDK 9;
- CPython 3.12.10 x64, installed from the official Python release;
- an NVIDIA GPU and driver that make `paddlepaddle-gpu` available;
- network access for the first bootstrap only.

From a clean clone, run:

```powershell
.\tools\bootstrap-paddle-runtime.ps1 `
  -PythonRuntimeRoot "$env:LocalAppData\Programs\Python\Python312"

.\tools\verify-paddle-runtime.ps1 `
  -RuntimeRoot .\work\paddle-runtime-win-x64

.\tools\build-track-d-opt-in-release.ps1 `
  -BootstrapRuntimeRoot .\work\paddle-runtime-win-x64 `
  -ValidateRuntimeOnly

dotnet restore GameTranslator.sln -r win-x64
dotnet build GameTranslator.sln -c Release --no-restore
dotnet test GameTranslator.sln -c Release --no-build --no-restore

.\tools\build-track-d-opt-in-release.ps1 `
  -BootstrapRuntimeRoot .\work\paddle-runtime-win-x64 `
  -TesseractLanguagePacks eng,jpn,jpn_vert,chi_sim,chi_sim_vert,tha `
  -SelfContained `
  -ReleaseName v0.1.0-local-paddle
```

The bootstrap pins CPython/Paddle package versions (including the official Paddle CUDA 12.9 wheel index) and verifies the PP-OCRv6 detector and six Tesseract packs by SHA-256. The first run downloads only their official distributions. The package build requires the exact locked language-pack set and verifies every pack before copying it; if the GPU runtime is unavailable or a language pack is missing, unexpected or changed, the script fails rather than silently producing an incomplete package.

Current publication boundary: the unpacked self-contained r44 is the newest locally verified source-equivalent portable and includes the welcome-tour spotlight, startup-safety, header-action, and OCR-guidance fixes. It has no transfer archive or signature and has not been uploaded to GitHub Releases, so it remains a local release candidate rather than a published release.

## Documentation

Start here:

- [Documentation index](docs/README.md)
- [User guide](docs/user-guide.md)
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

Developer module guides:

- [Domain](src/GameTranslator.Domain/README.md)
- [Application](src/GameTranslator.Application/README.md)
- [Infrastructure](src/GameTranslator.Infrastructure/README.md)
- [UI](src/GameTranslator.UI/README.md)

## Required Direction

- Language: C#
- UI: WPF
- Architecture: Clean Architecture + MVVM
- Capture: Windows Graphics Capture
- OCR: GPU Paddle text detector → bounded grouping → Tesseract crop recognition; Windows OCR and Tesseract remain supported engines
- Translation cache: SQLite
- Secrets: Windows Credential Manager

The application must not inject into game processes, read game memory, bypass anti-cheat systems, or use DLL injection.

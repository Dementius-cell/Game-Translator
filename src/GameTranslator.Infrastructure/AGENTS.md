# GameTranslator.Infrastructure AGENTS.md

These instructions apply to `src/GameTranslator.Infrastructure/**`.

## Purpose

Keep concrete external integrations isolated behind Application contracts.

## Ownership

- Windows Graphics Capture and Direct3D capture helpers.
- Windows OCR, Tesseract OCR, and OCR language pack adapters.
- SQLite translation cache persistence.
- JSON profile and settings persistence.
- Windows Credential Manager secret storage.
- Google, Azure, Yandex, diagnostic web translation providers, and update providers.

## Local Contracts

- Infrastructure may depend on Application and Domain, but must not depend on UI.
- Implement Application interfaces without changing their public semantics unless the matching Application contract is intentionally changed and tested.
- Do not remove Windows OCR or Tesseract support, and do not make the product depend on only one OCR engine.
- Store secrets only through Windows Credential Manager or an approved protected fallback; redact provider credentials from logs, exceptions, diagnostics, and artifacts.
- `WebAuto` and web translator providers are diagnostic/experimental paths and must not become silent production defaults.
- Do not add process injection, hooks, game memory access, drivers, anti-cheat bypass, or reverse engineering.

## Work Guidance

- Keep platform-specific exception handling close to the adapter and return recoverable Application-level failures where the contract supports it.
- Keep file paths, SQLite schema use, Tesseract data paths, and Windows language-pack behavior observable through tests or diagnostics without exposing secrets.
- Prefer adapter-level tests with fakes or temporary local storage over live network/API calls.

## Verification

- For infrastructure changes, run focused Infrastructure tests plus architecture dependency tests.
- For cache persistence, profile JSON, credentials, OCR engines, capture, or provider changes, run the relevant smoke/application tests that exercise the adapter through its Application contract.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

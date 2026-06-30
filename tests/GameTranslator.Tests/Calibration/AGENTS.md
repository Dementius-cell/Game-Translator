# Calibration Test Instructions

These instructions apply to `tests/GameTranslator.Tests/Calibration/**`.

## Purpose

Calibration tests are an offline experimental sandbox for golden-reference OCR, grouping, translation-request, mask, overlay, and diagnostics hypotheses. They may inject approved fixture data to study candidate rules before changing production behavior.

## Allowed In Calibration Tests

- Use generated images, static screenshots, or hand-authored fixture manifests.
- Bypass individual runtime stages by injecting approved OCR blocks, approved translations, semantic groups, mask bounds, overlay bounds, or forbidden regions.
- Compare candidate OCR presets, grouping hypotheses, reading-order rules, semantic helpers, and overlay placement parameters.
- Keep helper interfaces and fixture models inside the test project unless a production change is approved later.

## Still Prohibited

- Do not add game memory reads/writes, process hooks, DLL injection, drivers, or anti-cheat bypass.
- Do not store or export secrets in fixture JSON, diagnostics, logs, or snapshots.
- Do not make external AI, OCR, image-understanding, or translation services required for deterministic CI tests.
- Do not treat a passing calibration test as a production rule change by itself.

## Promotion Rule

A calibration result can only become application behavior after human visual verification. If the promotion changes OCR interfaces, overlay rules, profile schema, translator behavior, diagnostics schema, or architecture, follow `docs/governance/change-approval-required.md` before changing production code.

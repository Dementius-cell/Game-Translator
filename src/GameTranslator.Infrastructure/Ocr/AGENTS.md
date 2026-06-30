# OCR Infrastructure Instructions

These instructions apply to `src/GameTranslator.Infrastructure/Ocr/**`.

## Ownership

This directory contains concrete OCR engine implementations and OCR-specific infrastructure. Keep OCR abstractions and pipeline decisions in Application/Domain as already designed.

## Vertical CJK

- Vertical Chinese/Japanese text must use Tesseract.
- Do not route vertical CJK through Windows OCR.
- Do not change `IOcrEngine` or OCR result contracts without checking `docs/governance/change-approval-required.md`.
- Any Tesseract language mapping or page segmentation change must have focused tests.

## Safety

- Never add game memory reads/writes, DLL injection, hooks, drivers, anti-cheat bypass, or process memory access.
- OCR must operate only on captured image data supplied by the approved capture pipeline.

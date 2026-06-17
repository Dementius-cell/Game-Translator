# Sprint 9 Overlay Positioning Smoke

## Goal

Confirm that overlay text can be positioned from OCR text block bounding boxes.

## Steps

1. Start `GameTranslator.UI.exe` in an interactive Windows desktop session.
2. Create or select a profile with at least one OCR zone around visible text.
3. Set the source language to the visible text language.
4. Click `Refresh preview`.
5. Click `Recognize OCR`.
6. Click `Show test overlay`.
7. Confirm that overlay text appears inside the recognized source text bounds instead of only at the fixed Sprint 8 smoke location.
8. Move or change the OCR source text, then click `Recognize OCR` again while the overlay is still visible.
9. Confirm that overlay text updates to the latest OCR text blocks and remains click-through.

## Expected Result

- Overlay text positions include the OCR zone screen offset.
- Overlay text positions remain aligned when Windows display scaling is not 100%.
- Overlay text bounds are derived from OCR bounding box dimensions.
- Preview text scales inside OCR bounding boxes with only a small readability floor for tiny OCR lines.
- Re-running OCR while overlay is visible updates the overlay snapshot.
- The overlay still remains transparent outside text items and click-through.
- No profile, provider, settings, or credential secrets appear in debug output.

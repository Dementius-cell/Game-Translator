# Vertical CJK Overlay Placement

Status: active Sprint 26 design note.

Scope: manga-style vertical Chinese/Japanese text in OCR zones, especially speech bubbles, frame labels, and vertical character/name cards.

## Problem

Tesseract vertical OCR can return many small or noisy blocks: real glyphs, UI chrome, halftone texture, digits from labels, and accidental CJK-looking shapes from faces or clothing. A one-to-one mapping from OCR block to translation overlay is not good enough for manga pages:

- translating individual glyphs produces nonsense;
- masking every OCR-like block creates black bars on non-text art;
- anchoring translated text to a single glyph can move the overlay out of the original bubble or label.

## Current Contract

The pipeline intentionally separates three decisions:

1. Text detection: decide which raw OCR blocks are eligible text candidates.
2. Semantic grouping: combine eligible raw OCR blocks into translation units.
3. Overlay placement: draw translated text and masks in screen space.

These outputs are related but not identical:

- `SourceOcrResult`: raw OCR output from the selected OCR engine.
- `MaskSourceResult`: raw OCR blocks accepted for masking.
- `TranslationSourceResult`: semantic blocks sent to translation/cache.
- `OverlaySnapshot.TextItems`: translated text items derived from `TranslationSourceResult`.
- `OverlaySnapshot.MaskItems`: masks derived from `MaskSourceResult`.

Do not assume a translated item must have exactly one source OCR block. For vertical CJK, one translated item may correspond to multiple raw OCR blocks.

## Vertical CJK Grouping Rules

For vertical CJK `NearbyBlocks` mode:

- Keep Tesseract as the OCR engine.
- Prefer right-to-left column order, then top-to-bottom order inside each column.
- Group neighboring blocks in the same column when their X centers/overlap are compatible and the vertical gap is small.
- Group adjacent columns only when their vertical overlap suggests they belong to the same bubble or label.
- Do not let wide horizontal OCR noise bridge separate vertical groups.
- Reject semantic groups that are too small to carry meaning.
- Reject semantic groups whose surrounding background looks like halftone/body texture instead of a light speech bubble or label.

The current heuristic is intentionally conservative: missing a doubtful group is better than drawing translated text over a face, body, browser UI, or unrelated art.

## Overlay Placement Rules

Translated text should be anchored to the semantic group bounds, not to a random glyph. For vertical CJK, translated text is displayed as readable horizontal text expanded from the source center.

Masks should hide raw source glyphs inside accepted semantic groups. Masks should not be created for OCR noise that was rejected before or during semantic grouping.

The target visual behavior is similar to Google Lens:

- translation stays inside or centered on the original bubble/frame/label;
- original glyphs are hidden enough to be unreadable;
- unrelated art is not covered by black bars;
- multiple bubbles/labels remain separate translation units.

## Diagnostics Requirements

A useful diagnostics export for this area should include:

- OCR request: zone, language, engine, orientation, preprocessing.
- Raw OCR blocks with text and bounds.
- Filtered mask source blocks with text and bounds.
- Translation source blocks with grouped text and bounds.
- Overlay text items with translated text, source bounds, and screen bounds.
- Mask items with source text, source bounds, and mask bounds.
- Text/mask intersections.
- Semantic group diagnostics with group id, grouped source text, frame/screen bounds, mask source indexes, raw source indexes, text item index, and selected overlay anchor.

Diagnostics must not include API keys, credentials, or other secrets.

## Known Limits

This is still heuristic OCR geometry, not semantic image understanding. It can fail when:

- the OCR zone includes browser UI or app chrome;
- text is printed on dark or textured art instead of a white bubble;
- Tesseract returns merged garbage text that already spans unrelated regions;
- bubbles overlap faces or clothing;
- the capture happens while translation is still pending.

Manual smoke exports remain required before closing #32.

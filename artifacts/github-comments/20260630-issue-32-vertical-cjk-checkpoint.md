#32 working-tree checkpoint, 2026-06-30.

This update is not a pushed commit yet. It records the current local implementation state after the latest manual diagnostics showed partial improvement but still bad bubble/overlay alignment.

User-visible problem being addressed:

- Translated text should land inside the same bubble/frame/label as the original text, closer to Google Lens behavior.
- Vertical CJK OCR blocks must be grouped into meaningful translation units instead of translating isolated glyphs.
- Masks should hide accepted source glyphs without drawing extra black bars over faces, bodies, UI chrome, or unrelated art.
- Overlay text and masks should be coordinated, but one translated group does not have to equal one OCR block.

Engineering direction now documented:

- Treat text detection, semantic grouping, and overlay placement as separate decisions.
- For vertical CJK, translate semantic groups but mask accepted raw OCR blocks inside those groups.
- Anchor translated text to the semantic group / bubble-like source region, not to a random single glyph.

Implemented locally for #32:

- Added `TranslationTextGroupingResult` so grouping can return both:
  - `TranslationSourceResult` for cache/translation;
  - `MaskSourceResult` for raw source masks.
- Updated the pipeline so translation uses grouped semantic text while masks remain based on accepted raw OCR blocks.
- Added `MaskSourceOcrResult` to `TranslationPipelineResult` and preserved it through normal, pending, reused, and timing-replaced pipeline results.
- Added vertical CJK `NearbyBlocks` grouping improvements:
  - right-to-left columns;
  - top-to-bottom reading order inside each column;
  - rejection of obvious OCR noise before translation/masking;
  - prevention of wide horizontal OCR noise bridging separate vertical columns;
  - conservative light-background gating for bubble/label-like regions.
- Updated overlay behavior for vertical source text so translated text uses a readable expanded layout while masks remain tied to source glyph bounds.
- Added diagnostics export details needed for the next manual smoke:
  - `maskSourceOcr`;
  - `overlayGeometry.semanticGroups`;
  - per-group mask source indexes;
  - per-group raw source indexes;
  - per-group translated text item index;
  - per-group selected overlay anchor and screen bounds.

Validation on the current local checkpoint:

- `dotnet build GameTranslator.sln -c Release` passed with 0 warnings / 0 errors.
- `dotnet test GameTranslator.sln -c Release --no-build` passed: `315/315`.
- `git diff --check` passed with only expected CRLF/LF normalization warnings.

Manual validation still needed before closing #32:

- Run the current Release build and export a fresh diagnostics package for the vertical Chinese/Japanese manga case.
- Confirm whether `semanticGroups` match the intended bubbles/labels.
- Confirm whether `maskSourceIndexes` include only real source glyphs.
- Confirm whether `rawSourceIndexes` show that UI chrome/body/halftone noise was excluded.
- Confirm whether each `overlayAnchor` and translated `textBounds` lands inside the intended bubble/frame/label.

Known risk:

The current implementation is still heuristic OCR geometry, not image segmentation or semantic bubble detection. The new diagnostics are meant to make the next correction targeted: OCR block acceptance, semantic grouping, mask selection, or overlay anchor placement, instead of tightening one noise filter blindly.

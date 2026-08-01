# OCR and Overlay Work Status

Last updated: 2026-08-01

Purpose: durable handoff/status note for follow-up work on overlay layout, WPF text measurement, and Tesseract OCR geometry/quality. When the user asks for status in a later chat, read this file after the required `AGENTS.md` chain and source-of-truth docs, then compare it with current GitHub Issues and the working tree.

This file is a coordination note, not a new ADR and not a replacement for GitHub Issues. GitHub Issues and their explicit dependency graph remain the current delivery source. If an issue, accepted ADR, or explicit owner decision conflicts with this file, report the conflict and update this file after the owner decision.

## Current Baseline

- Branch baseline at handoff: `main` contains commit `3381225` (`Refine governance and stabilize cache tests`).
- Checks at handoff: docs mini check clean, Release build clean, Release tests `334/334`.
- Governance model: ADR-017 accepted. Normal implementation inside accepted ADR scope does not require a new owner decision.
- ADR-018 accepted and ADR-016 superseded: expanded translation layout starts from a source-based frame, expands symmetrically around the source center as measured content requires, and reduces font size only after available expansion is exhausted. Vertical start width defaults to about `2x` source width.
- Related issues after 2026-08-01 sync: `#32`, `#37`, `#38`, and `#39` are closed. Future CJK/OCR quality improvements should be tracked in new focused issues rather than reopening this overlay/OCR checkpoint chain.
- Preserve the old calibration/evidence working-tree tail unless the owner explicitly decides otherwise.

## Status Legend

- `Not started`: no implementation in the current tracked plan.
- `Ready`: dependencies are satisfied enough to begin.
- `In progress`: implementation is active and not yet ready for owner verification.
- `In review`: implementation evidence is ready and awaits owner verification.
- `Blocked`: cannot proceed without an upstream task or owner decision.
- `Done`: implemented, verified, and status updated with evidence.

## Dependency Graph

```text
Track A: Overlay
A1 WPF text measurement port
  -> A2 ADR-016 overlay placement
     -> A3 overlay evidence and issue update

Track B: OCR
B1 Tesseract word bounds and confidence
  -> B2 zone-mode PSM and grouping passes
     -> B3 Thai/CJK low-confidence fallback

Parallel entry points: A1 and B1 may proceed in parallel.
Recommended single-agent order: A1 -> A2 -> B1 -> B2 -> B3.
Recommended multi-agent order: A1 and B1 in parallel, then A2 and B2, then B3.
```

## Track A: Overlay and Text Measurement

### A1. WPF text measurement port

Status: Done

Goal:
- Remove character-coefficient text measurement.
- Add an Application-level text measurement port or layout service contract.
- Implement WPF measurement in UI using the WPF text layout engine, preferably `TextFormatter` or equivalent WPF text formatting primitives that match runtime wrapping.

Implemented:
- Added the Application text measurement seam: `IOverlayTextMeasurer`, `OverlayTextMeasurementRequest`, `OverlayTextMeasurement`, and `OverlayTextLineMeasurement`.
- `OverlayPositioningService` now consumes the measurement seam for expanded overlay bounds and vertical font fitting.
- Added `WpfOverlayTextMeasurer` in UI using WPF `TextFormatter` line formatting, and registered it through presentation DI.
- Kept the parameterless `OverlayPositioningService` constructor backed by a legacy measurer only for existing offline/no-UI compatibility callers; production DI uses the WPF measurer.

Why first:
- ADR-016 placement needs real wrapped text dimensions. Implementing placement before real measurement would likely require rework.

Expected impact:
- Application contract addition and UI implementation.
- Existing overlay positioning code should consume measured line/box results instead of estimating character sizes.

Verification:
- Unit tests for deterministic wrapping/measurement decisions using a fake measurement service.
- UI/WPF-focused tests or smoke verification for the real WPF implementation when practical.
- Release build and relevant tests.
- 2026-07-29: focused tests passed, `32/32`: `OverlayPositioningServiceTests`, `OverlayPublicSeamTests`, and `PresentationCompositionTests`.
- 2026-07-29: `dotnet build GameTranslator.sln -c Release --no-restore` passed with `0` warnings and `0` errors.
- 2026-07-29: `dotnet test GameTranslator.sln -c Release --no-build` passed, `337/337`.

### A2. ADR-016/018 overlay placement

Status: Done

Goal:
- Use separate mask bounds and translation text bounds.
- Keep mask tied to OCR semantic/source bounds.
- Initialize vertical-source translation text width at about `2x` source width, clamped to readable min and OCR zone limits.
- Wrap text inside the measured box.
- Start from a minimally padded source-based frame and grow translation bounds symmetrically around the source center when actual measured content does not fit.
- Reduce font size only after available centered expansion is exhausted.
- Handle collisions against already placed translations, not only semantic OCR blocks.
- Surface deterministic clipping/ellipsis as a debug or quality warning when text still cannot fit.

Implemented:
- Vertical expanded text now uses a separate translation text box while the mask remains tied to the OCR semantic/source bounds.
- Vertical-source translation width starts at `2x` source width, with a readable minimum and OCR-zone/maximum-width clamps.
- Vertical expanded text grows around the source center within the OCR zone and only reduces font size after the zone height limit is reached.
- Placement collision handling now treats already placed translation text boxes as obstacles in addition to neighboring OCR semantic bounds.
- Clipped/overlapping placement fallbacks add deterministic `Overlay fit warning:` debug metric lines.

Owner-approved refinement in progress:
- ADR-018 supersedes ADR-016's vertical-height-first policy. The common fit order is now source-based initial frame, measured centered expansion, then font reduction.
- Remove the horizontal right-overflow dampening heuristic that shifted real comic translations left of their source text.
- Add a session-only debug control for the vertical source-width multiplier (`1.0x` through `2.5x`, default `2.0x`); do not persist it in profiles or settings.

2026-07-29 implementation evidence:
- Replaced the old natural-width budget and semantic-right-overflow dampening with source-based initial frames. Horizontal text starts at source width plus `8px`; vertical text starts at `max(source + 8px, source x session multiplier)` with the existing readable-width floor.
- Measured content now expands width and height around the source center at the preferred font size. It reduces font size only after capture-region expansion cannot fit the text; at a capture boundary, the frame uses remaining valid space before reducing the font.
- Added `SessionVerticalSourceWidthMultiplier` to `OverlayPositioningService`, clamped to `1.0x` through `2.5x` and intentionally held only in memory. The WPF debug panel binds a disabled-until-debug slider/text input to it; no profile or `ISettingsService` write occurs.
- Focused placement and view-model tests passed, `91/91`.
- 2026-07-29: Release build passed with `0` warnings and `0` errors; full Release tests passed, `343/343`; `tools/check-docs-mini.ps1` reported `0` markdown link and `0` actionable backtick problems.

Verification:
- Focused placement tests for vertical CJK, long Russian translations, multi-zone independence, and collision handling.
- Evidence images must include legend, scenario numbers, and grouping mode (`menu`, `dialog`, or `comic`) when new evidence is generated.
- Avoid visible UI runs unless explicitly needed; report PID for any explicit UI run.
- 2026-07-29: `dotnet build GameTranslator.sln -c Release --no-restore` passed with `0` warnings and `0` errors.
- 2026-07-29: focused overlay placement tests passed, `24/24`: `OverlayPositioningServiceTests`.
- 2026-07-29: focused overlay/pipeline/composition tests passed, `57/57`: `OverlayPositioningServiceTests`, `OverlayPublicSeamTests`, `PresentationCompositionTests`, and `TranslationPipelineServiceTests`.
- 2026-07-29: `dotnet test GameTranslator.sln -c Release --no-build` passed, `340/340`.
- 2026-07-29: no visible UI run and no new evidence images generated in this step.

### A3. Overlay evidence and issue update

Status: Done

Goal:
- Produce no-UI/headless evidence where possible.
- Update relevant GitHub issue(s) with verified multi-line comments using `--body-file` or file-backed API JSON, then verify the published body.

Verification:
- `tools/check-docs-mini.ps1` if docs/evidence references are changed.
- Issue comment verification through `gh issue view --json comments` or `gh api`.
- 2026-07-29: generated local, untracked headless evidence through `OverlayPositioningService` with `WpfOverlayTextMeasurer` (`TextFormatter`); no UI window was opened. PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\adr016-overlay-placement-2026-07-29\adr016-overlay-placement-evidence.png`; geometry report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\adr016-overlay-placement-2026-07-29\adr016-overlay-placement-evidence.json`.
- 2026-07-29: evidence scenarios are labeled `S1` through `S4` and carry `menu`, `dialog`, or `comic` grouping modes. `S1` proves separate vertical source/mask `36x178` and translation bounds `96x138`; `S2` proves wrapped dialog text without a warning; `S3` leaves a two-pixel gap between adjacent translation bounds; `S4` reaches the 8 px minimum font and emits the expected vertical clipping warning.
- 2026-07-29: focused overlay/public-seam/composition tests passed, `35/35`.
- 2026-07-29: generated additional local, untracked real-page evidence from a user-provided English comic page. PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-comic-english-page-2026-07-29\real-comic-english-overlay-evidence.png`; OCR and geometry report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-comic-english-page-2026-07-29\real-comic-english-ocr-overlay-report.json`.
- 2026-07-29: this evidence used the production `TesseractOcrEngine` and `WpfOverlayTextMeasurer` headlessly. Current full-page single-block OCR emitted only five unusable lines; nine manually defined bubble crops produced non-empty OCR text in `8/9` cases, with illustration noise in several results. `B1` (`YO!`) returned no OCR text. Overlay placement had no fit warnings. Manual crop grouping is evidence-only and does not change B1/B2 status or claim automatic comic grouping.
- 2026-07-29: user-provided annotation established green semantic source/mask truth and blue per-bubble safe translation bounds for the same comic page. Local, untracked comparison PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\annotated-comic-ground-truth-2026-07-29\annotated-comic-ground-truth-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\annotated-comic-ground-truth-2026-07-29\annotated-comic-ground-truth-report.json`.
- 2026-07-29: the annotation produced nine green and nine blue zones (the B8/B9 blue outlines touch, so generic connected-component counting sees eight components). Production Tesseract OCR returned non-empty text from `9/9` user-defined blue crops. With green bounds supplied as semantic source truth, current masks matched `9/9`; current translated text bounds met strict blue containment `0/9`, while emitting no fit warnings. This comparison exposed that horizontal width budgeting and right-overflow dampening are measured against semantic source bounds, not a translation layout frame.
- 2026-07-29 owner clarification: green is the semantic OCR/mask anchor; blue is evidence of the preferred, padded initial translation frame, not a persisted hard final constraint. Desired behavior is Google Translate-style: a translation box is initially minimal and centered around the original text, then expands only as actual WPF-measured content requires.
- 2026-07-29 owner approved the resulting default-policy change. ADR-018 supersedes ADR-016 and adds a session-only vertical width multiplier for debug evidence; A3 must be regenerated after A2 implementation before owner visual acceptance and a verified multi-line GitHub Issue `#32` comment.
- 2026-07-29 regenerated headless evidence with production `WpfOverlayTextMeasurer` and the owner-provided comic annotation. PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\adr018-centered-comic-2026-07-29\adr018-centered-comic-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\adr018-centered-comic-2026-07-29\adr018-centered-comic-report.json`.
- 2026-07-29: new evidence uses green as semantic source/mask truth, blue as padded initial-frame calibration, and cyan as measured final translation bounds. Production Tesseract produced non-empty text from `9/9` supplied bubble crops; masks matched green `9/9`; final translation bounds remained centered after collision handling for `8/9`; warnings `0`. `B9` moved upward by `5.5px` to avoid the already placed `B8` translation bounds.
- 2026-07-30: generated two additional untracked headless evidence scenarios with production `TesseractOcrEngine` and `WpfOverlayTextMeasurer`; neither run opened a UI window. Both PNGs contain a legend, scenario identifier, and grouping mode. `S5` comic long-dialogue manga evidence: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-manga-expansion-2026-07-30\real-manga-expansion-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-manga-expansion-2026-07-30\real-manga-expansion-2026-07-30-report.json`.
- 2026-07-30: `S5` uses six manually selected bubble crops for evidence only; Tesseract returned non-empty text for `6/6`, including the longest `M6` dialogue. The WPF overlay kept the preferred `18px` font for every translation and emitted `0` fit warnings. This is not an automatic comic-bubble detector.
- 2026-07-30: `S6` dialog real-game, two-line subtitle evidence: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-game-subtitle-2026-07-30\real-game-subtitle-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-game-subtitle-2026-07-30\real-game-subtitle-2026-07-30-report.json`.
- 2026-07-30: `S6` returned text for `1/1` manually selected subtitle crop and created a `651x58` Russian translation bounds at `18px` with `0` overlay warnings. Tesseract included a decorative dialogue-frame glyph and grouped the source at `643x102`, so this is evidence that the pending word-bounds/confidence and zone-mode work (B1/B2, Issues `#38/#37`) remains necessary; it does not claim automatic scene-text detection.
- 2026-07-30: generated local-only, ignored `outputs/**` headless vertical CJK evidence with production `TesseractOcrEngine` and `WpfOverlayTextMeasurer`; no UI window was opened. `S7` comic Japanese PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-japanese-vertical-2026-07-30\real-japanese-vertical-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-japanese-vertical-2026-07-30\real-japanese-vertical-2026-07-30-report.json`.
- 2026-07-30: `S7` runs six manually selected dialogue crops using `jpn_vert` and vertical source geometry. Tesseract returned non-empty text for `6/6`; the overlay used the preferred `18px` font and emitted `0` fit warnings. Most narrow vertical columns produced narrow masks, while the large `J3` bubble was reported as an overly broad `180x277` source group and consequently received a `360px` translation frame. This is a current line-geometry/grouping limitation, not a claim of automatic comic grouping.
- 2026-07-30: generated local-only, ignored `outputs/**` headless vertical CJK evidence for `S8` comic Chinese. PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-chinese-vertical-2026-07-30\real-chinese-vertical-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-chinese-vertical-2026-07-30\real-chinese-vertical-2026-07-30-report.json`.
- 2026-07-30: `S8` runs six manually selected dialogue crops using `chi_sim_vert` and vertical source geometry. Tesseract returned non-empty text for `6/6` and the overlay emitted `0` fit warnings, but only the narrow `C3` crop (`54x283`) was recognized cleanly. The other crops include frame, illustration, or decorative-stroke noise in their source geometry; their wide masks correctly cause wide translation frames under the current ADR-018 policy. Tesseract also reported non-fatal small-image/line-recognition diagnostics. This evidence keeps `B1` word bounds/confidence and `B2` zone-mode grouping (`#38` then `#37`) as the next OCR-quality work.
- 2026-07-30 owner clarification from annotated Japanese and Chinese vertical pages: narrow vertical source groups must exhaust symmetric horizontal translation-frame growth before the layout increases its height; font reduction remains last. Horizontal-source behavior remains unchanged. Added the focused regression test `CreateSnapshot_WithExpandedVerticalText_ExhaustsWidthBeforeIncreasingHeightAllowance`; `OverlayPositioningServiceTests` passed `27/27`.
- 2026-07-30: owner annotations use exact green semantic source/mask bounds and blue padded initial-frame calibration. Generated local-only, ignored `outputs/**` owner-geometry evidence with production `WpfOverlayTextMeasurer` and no UI window. `S9` comic Japanese PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\owner-annotated-japanese-vertical-2026-07-30\owner-annotated-japanese-vertical-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\owner-annotated-japanese-vertical-2026-07-30\owner-annotated-japanese-vertical-2026-07-30-report.json`.
- 2026-07-30: `S9` uses owner-confirmed source geometry for ten vertical Japanese groups. `S10` comic Chinese PNG: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\owner-annotated-chinese-vertical-2026-07-30\owner-annotated-chinese-vertical-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\owner-annotated-chinese-vertical-2026-07-30\owner-annotated-chinese-vertical-2026-07-30-report.json`.
- 2026-07-30: `S10` uses owner-confirmed source geometry for six vertical Chinese groups. Both owner-geometry runs intentionally bypass OCR so they isolate placement: every mask equals the supplied green bounds, blue remains calibration only, final cyan bounds are measured through `TextFormatter`, and both runs emitted `0` overlay warnings. They do not change the earlier conclusion that production CJK OCR geometry still needs B1/B2.
- 2026-07-30: owner visually accepted the `S9` Japanese and `S10` Chinese owner-geometry evidence. Published and verified the multi-line Issue `#32` checkpoint comment: `https://github.com/Dementius-cell/Game-Translator/issues/32#issuecomment-5127697636`. `A3` is complete; Issue `#32` remained open only for the raw OCR geometry dependency chain `#38` then `#37`.
- 2026-08-01: after `#38` and `#37` were completed and closed, published and verified the final dependency-sync comment for Issue `#32`: `https://github.com/Dementius-cell/Game-Translator/issues/32#issuecomment-5150385318`. Issue `#32` is closed as completed for the approved vertical/CJK overlay placement hardening scope.

## Track B: OCR Geometry and Quality

### B1. Tesseract word bounds and confidence

Status: Done

Decision record:
- 2026-07-30: the owner approved additive optional word metadata to reach the source-geometry fidelity shown by the accepted CJK evidence. ADR-019 is accepted; the verified Issue #38 decision comment is `https://github.com/Dementius-cell/Game-Translator/issues/38#issuecomment-5127956743`.
- B1 is limited to propagating actual word text, bounds, nullable confidence, and recognition-pass identity without changing existing line-block behavior. Automatic rejection, retries, preprocessing fallback, profile settings, and persistence remain B2/B3 work.

Completion:
- 2026-07-30: added engine-neutral `OcrResult.Words`. Every `OcrWord` carries text, frame-relative bounds, nullable engine-local confidence, and a recognition-pass identifier. Existing line blocks remain unchanged; engines that do not produce word metadata return an empty list.
- 2026-07-30: `TesseractOcrEngine` now maps `textLine.Words` from the `TesseractOCR` layout API. It keeps Tesseract's actual word bounds and confidence and records the selected pass as `tesseract:SingleBlock` or `tesseract:SingleBlockVertText`.
- 2026-07-30: raw word metadata is preserved by frame-reuse and current semantic text grouping so B2 can consume it; B1 does not change grouping, rejection, preprocessing, or fallback policy.
- 2026-07-30: local-only headless production evidence confirmed metadata on the owner-provided real CJK pages without opening a UI window. `S7` comic Japanese produced 75 full-page words and crop counts `J1=7, J2=6, J3=23, J4=18, J5=12, J6=10`: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-japanese-word-metadata-2026-07-30\real-japanese-vertical-2026-07-30-report.json`. `S8` comic Chinese produced 140 full-page words and crop counts `C1=4, C2=13, C3=4, C4=10, C5=3, C6=2`: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-chinese-word-metadata-2026-07-30\real-chinese-vertical-2026-07-30-report.json`.
- 2026-07-30: the evidence intentionally confirms raw engine output, including low/zero-confidence noise near illustration and frame strokes. It does not claim that the current single-block OCR already reproduces the accepted green semantic bounds; that remains B2 zone-mode detection and grouping.
- 2026-07-30 verification: focused OCR/grouping/architecture tests `83/83`; `dotnet build GameTranslator.sln -c Release --no-restore` with `0` warnings and `0` errors; full `dotnet test GameTranslator.sln -c Release --no-build` `347/347`; `tools/check-docs-mini.ps1` `0` markdown-link and `0` actionable-backtick problems.

Goal:
- Extend the Tesseract OCR path to expose word-level bounds and confidence.
- Preserve existing line/block output compatibility or provide an additive representation that downstream code can group.
- Enable filtering or quality warnings for low-confidence recognitions instead of blindly accepting unreadable output.

Why first:
- Confidence is needed before expensive fallback modes can be applied selectively.
- Word geometry is needed for honest multi-pass grouping and better overlay source geometry.

Expected impact:
- Tesseract adapter and Application OCR result contracts may need additive fields.
- Tests should prove existing consumers still work and new word/confidence data is available.

Verification:
- Focused Tesseract tests with fixture images.
- Architecture tests to ensure contracts stay in the right layer.
- Release build and relevant tests.

### B2. Zone-mode PSM and grouping passes

Status: Done

Decision record:
- 2026-07-30: B1 / Issue #38 completed and closed. The owner approved the resulting layout-aware default policy in ADR-020; the verified Issue #37 decision comment is `https://github.com/Dementius-cell/Game-Translator/issues/37#issuecomment-5128079069`.
- Existing `TranslationGroupingMode` is the compatible runtime layout signal: `BlockByBlock` -> menu/sparse, `WholeZone` -> dialog/single block, and `NearbyBlocks` -> comic/sparse detection plus line refinement. No profile schema or persisted layout setting is added.

Completion:
- 2026-07-30: added the additive runtime `OcrLayoutMode` contract. Pipeline derives it from the existing grouping setting; preprocessing and OCR request reconstruction preserve it.
- 2026-07-30: Tesseract maps menu to `SparseText` (`PSM 11`), dialog to the orientation-aware single-block pass, and comic to sparse detection followed by per-detected-line refinement. The wrapper page is disposed before each refinement because it permits only one active `Process` result per engine.
- 2026-07-30: comic semantic source blocks now use refinement word bounds with Tesseract confidence at least `50`, while all detection and refinement words remain available as pass-labelled raw diagnostics. Low-confidence words cannot expand a source mask to illustration or frame strokes.
- 2026-07-30: direct no-UI evidence exercised dialog and comic layout selection. `S6` dialog real-game subtitle returned text `1/1` with `0` overlay warnings: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-game-dialog-layout-2026-07-30\real-game-subtitle-2026-07-30-evidence.png`. `S7` comic Japanese returned text `6/6`; word geometry produces compact masks and short vertical translation no longer grows to capture width: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-japanese-comic-short-vertical-fit-2026-07-30\real-japanese-vertical-2026-07-30-evidence.png`.
- 2026-07-30: `S8` comic Chinese produced only `3/6` reliable groups. This is recorded as B3 input, not accepted CJK quality: low-confidence/noisy candidates are suppressed from semantic geometry instead of overlaying a broad translation onto artwork.

Goal:
- Choose Tesseract page segmentation mode by zone grouping/layout mode.
- Menu: use sparse text style segmentation, expected Tesseract `PSM 11`.
- Dialog: use uniform block segmentation, expected Tesseract `PSM 6`.
- Comic: run sparse detection first, then OCR individual lines/groups.
- Replace the current single `SingleBlock` / `SingleBlockVertText` choice for all layouts.

Why after B1:
- Multi-pass OCR should use word bounds/confidence to decide grouping quality and avoid producing misleading geometry.

Verification:
- Tests for menu, dialog, and comic grouping behavior.
- Evidence images with scenario number and grouping mode.
- Performance notes when additional passes are introduced.

### B3. Thai/CJK low-confidence fallback

Status: Done

Decision record:
- 2026-07-30: the owner-approved empty-comic fallback is recorded in ADR-021. It is bounded to a single orientation-aware Tesseract single-block pass only after comic sparse/refinement emits zero reliable semantic groups; no profile field, unconditional retry, or preprocessing default is introduced.
- 2026-07-30: ADR-022 is accepted for the next bounded B3 increment. It adds one empty-only `2x` bilinear quality-upscale retry for Tesseract CJK/Thai languages, with original-frame bounds mapping and explicit diagnostic pass identifiers. It does not add persisted profile settings, inversion, adaptive Otsu/Sauvola, deskew, or `tessdata_best`.

Progress:
- 2026-07-30: implemented pass-labelled `empty-comic-fallback` with the same `>= 50` confidence geometry rule as B2. It preserves raw fallback words for diagnostics and cannot restore broad line-rectangle masks.
- 2026-07-30: headless `S8` comic Chinese evidence improved from `3/6` to `5/6` non-empty crop results: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-chinese-comic-empty-fallback-2026-07-30\real-chinese-vertical-2026-07-30-evidence.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\real-chinese-comic-empty-fallback-2026-07-30\real-chinese-vertical-2026-07-30-report.json`.
- 2026-07-30: the report confirms `C3` recovered through `tesseract:SingleBlockVertText:empty-comic-fallback` with a `68x197` source bounds. `C6` still has no reliable words and C1/C4/C5 have incomplete or overly broad geometry. These remain the required preprocessing/model-quality evidence before B3 can be complete.
- 2026-07-30: local-only evidence-only preprocessing sweep compared baseline, existing production threshold/scale, bilinear `2x`/`3x`, Otsu, Sauvola, and inversion variants. Chinese report/contact sheet: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\b3-preprocessing-sweep-noscale-2026-07-30\real-chinese-b3-preprocessing-sweep-2026-07-30\b3-preprocessing-sweep-report.json`; Thai reports are under `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\b3-preprocessing-sweep-2026-07-30\thai-b3-preprocessing-sweep-*`.
- 2026-07-30: evidence showed Thai baseline already reliable on the available calibration crops, while Chinese `C6` only became useful when `2x` upscale happened before comic sparse detection and line refinement. No-scale threshold/Otsu/Sauvola mostly produced single-symbol noise for `C6`; `tessdata_best`, deskew, persisted profile settings, and unconditional retries were not promoted to production.
- 2026-07-30: implemented ADR-022 in `TesseractOcrEngine`: empty CJK/Thai results run one `2x` bilinear quality-upscale retry. Comic layout retries sparse detection plus line refinement and maps diagnostics/semantic bounds back to the source frame; non-comic layout retries the selected PSM and maps bounds back.
- 2026-07-30: production headless `S8-B3` comic Chinese sweep now reports baseline non-empty semantic output for `6/6` crops, `11` semantic blocks, `28` reliable words, average reliable confidence `78.21`, total baseline time `1081.97ms`. `C6` recovered through `tesseract:SparseText:quality-upscale-detection` and `tesseract:SingleBlockVertText:quality-upscale-line-refinement` with `2` semantic blocks, `4` reliable words, and average reliable confidence `84.07`: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\b3-production-comic-quality-upscale-2026-07-30\real-chinese-b3-preprocessing-sweep-2026-07-30\b3-preprocessing-sweep-contact-sheet.png`; report: `C:\Users\admin\Documents\Codex\2026-07-29\game-translator-continuation-2026-07-29\outputs\b3-production-comic-quality-upscale-2026-07-30\real-chinese-b3-preprocessing-sweep-2026-07-30\b3-preprocessing-sweep-report.json`.
- 2026-08-01: the owner accepted the B3 production evidence. ADR-022 completes the bounded empty-result fallback; partial non-empty OCR, including still-imperfect C1/C4/C5 Chinese geometry/text, deliberately remains outside this scope. Any future fallback for wrong-but-non-empty output needs a separate confidence/geometry quality policy and evidence.
- 2026-07-30 verification: `dotnet build GameTranslator.sln -c Release --no-restore` passed with `0` warnings and `0` errors; full `dotnet test GameTranslator.sln -c Release --no-build` passed `361/361`; `tools/check-docs-mini.ps1` reported `0` markdown-link and `0` actionable-backtick problems.
- 2026-08-01: published and verified the final Issue `#37` B3 sync comment: `https://github.com/Dementius-cell/Game-Translator/issues/37#issuecomment-5150383025`. Issue `#37` is closed as completed under ADR-020/021/022 scope.

Goal:
- Add bounded fallback only when confidence or geometry indicates poor OCR, without profile or persistence changes unless evidence justifies them.
- Candidate preprocessing: inversion for light text on dark background, adaptive Otsu/Sauvola-style binarization, deskew, and higher-quality upscale.
- Candidate OCR fallback: `tessdata_best` only for low-confidence cases because it is slower than fast data.
- Thai uses Tesseract `tha` on this Windows build; Windows OCR Thai is unavailable in the known environment.

Why last:
- Without confidence, expensive fallback would run blindly.
- Without zone-mode PSM, fallback may improve pixels while layout segmentation remains wrong.

Verification:
- Thai/CJK fixture tests and evidence.
- Latency/performance measurement for fallback paths.
- Confirm fallback does not become an unconditional default.

## Track C: Modern Scene-Text OCR Research

### C1. Issue #39 PaddleOCR local benchmark

Status: Done

Scope:
- Research-only benchmark for an optional modern scene-text OCR engine candidate.
- No product code, runtime dependency, OCR default, profile schema, release packaging, model files, cloud OCR/VLM, game hooks, memory reads, or input interception are introduced.
- Any future integration still requires an owner-approved Change Request/ADR because it would add a third OCR runtime and deployment footprint.

Evidence:
- 2026-08-01: created a local-only ignored benchmark workspace at `C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator\outputs\issue-39-scene-ocr-benchmark-2026-08-01`.
- Local environment: Python `3.12.13`, PaddlePaddle `3.3.0`, PaddleOCR `3.7.0`, Windows 11 CPU. The benchmark venv is `811.53 MiB`; downloaded model cache is `231.21 MiB`.
- Official PaddleOCR `PP-OCRv6` supports `en`, `ch`, and `japan` in this run. Thai is not available through `PP-OCRv6`, so the Thai case uses `lang=th` with `PP-OCRv5`.
- The default Windows CPU oneDNN/MKLDNN path failed before OCR output with Paddle runtime `NotImplementedError`; all successful benchmark cases required `enable_mkldnn=False`.
- JSON report: `C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator\outputs\issue-39-scene-ocr-benchmark-2026-08-01\results\paddleocr-benchmark-report.json`.
- Markdown report: `C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator\outputs\issue-39-scene-ocr-benchmark-2026-08-01\results\paddleocr-benchmark-report.md`.
- Evidence PNGs include a legend, scenario id, and grouping mode:
  - `P1` dialog game subtitle full frame: `19.63s` first run, `19.47s` steady, `10` regions, expected subtitle substrings `3/3`, but it also detects unrelated HUD text.
  - `P2` comic English manga full page: `21.13s` first run, `21.43s` steady, `38` regions, most dialogue text detected automatically; reading order interleaves left/right bubbles and still needs grouping before overlay use.
  - `P3`/`P4` comic Japanese vertical full page: `12.20s` to `13.45s`, `20` regions. It detects many vertical dialogue columns on the full page; textline orientation did not materially improve this case.
  - `P5`/`P6` comic Chinese vertical full page: about `20s`, `11` regions. It detects several vertical blocks but misses/merges some dialogue and includes punctuation/page noise; textline orientation did not materially improve this case.
  - `P7` dialog Thai crop: first run included `130.73s` model download/init; cached OCR is about `4.06s` steady for `3` Thai regions. Current accepted Tesseract Thai evidence on the same crop is tens of milliseconds with reliable confidence.

Recommendation:
- Defer product integration for now. PaddleOCR is promising as a research/reference detector, especially for full-page comic text discovery, but the current Windows CPU latency, venv/model footprint, MKLDNN workaround, and unresolved grouping/reading-order work make it unsuitable as a normal runtime dependency without a separate owner-approved ADR.
- Keep Windows OCR plus Tesseract as the product OCR engines. Revisit only if the owner wants a new ADR focused on optional packaged model deployment, per-zone async/background use, and a concrete quality win over the current B1/B2/B3 pipeline on real user captures.

Verification:
- Local-only ignored benchmark script: `C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator\outputs\issue-39-scene-ocr-benchmark-2026-08-01\run_paddleocr_benchmark.py`.
- No UI window was opened.
- No product source or test code was changed for this research checkpoint.
- 2026-08-01: `tools/check-docs-mini.ps1` reported `0` markdown-link and `0` actionable-backtick problems.
- 2026-08-01: published and verified the final Issue `#39` research summary comment: `https://github.com/Dementius-cell/Game-Translator/issues/39#issuecomment-5150366070`. The owner approved closing `#39`; Issue `#39` is closed as completed with product integration deferred.

## Next Delivery Guidance

- No active OCR/overlay track remains in this status file after `#32`, `#37`, `#38`, and `#39` were closed.
- Do not start `#29` or `#30` until `#28` is closed or the project owner explicitly authorizes that transition.
- Current open GitHub follow-ups outside this OCR/overlay chain are `#34` experimental web translator diagnostics validation and `#35` test architecture/calibration workflow hardening. Treat their human/manual validation scope separately from production OCR/runtime changes.
- Any future third OCR runtime integration, including PaddleOCR, requires a separate owner-approved Change Request/ADR.

## Parallelization Guidance

Safe to start in parallel:
- A1 and B1.

Do not start in parallel as final implementation:
- A2 before A1.
- B3 before B1.
- B3 before B2 unless explicitly scoped as a narrow experiment.
- OCR public contract breaking changes without governance classification and owner decision.

## Status Update Rules

When any item starts or completes:
- Update its `Status` line in this file.
- Add issue/PR references if created.
- Add verification results and evidence paths with tracked/generated/local/build-output labels when applicable.
- Keep this file concise; move detailed evidence to issues or dedicated reports.

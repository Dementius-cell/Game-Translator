# Issue 32 Overlay Placement Production Promotion Spec

Status: historical approved production-promotion record from 2026-07-01. The implementation was completed; current runtime policy is defined by ADR-018, ADR-030, ADR-031, and [Architecture](../02-architecture.md).

The `WebAuto`, `BlockByBlock`, `WholeZone`, and `NearbyBlocks` names in the evidence tables below record the then-current smoke configuration. They are not current user choices: `WebAuto` is removed from production selection, and normal candidate grouping is controlled by writing-system policy under `ContentLayoutMode.DialogComic`.

Date: 2026-07-01

Scope: production promotion of Sprint 26 calibration placement rules into runtime overlay placement. Project-owner approval was granted on 2026-07-01 before production code changes.

## Calibration Inputs

Decision record:

- `docs/design/golden-reference-calibration.md`
- `artifacts/calibration/mixed-orientation-frame/decision-record.json`

Evidence:

- `artifacts/calibration/candidate-evidence.png`
- `artifacts/calibration/scorecard.json`
- `artifacts/calibration/mixed-orientation-frame/placement-evidence.png`
- `artifacts/calibration/mixed-orientation-frame/placement-evidence-map.json`
- `artifacts/calibration/mixed-worst-case-frame/placement-evidence.png`
- `artifacts/calibration/mixed-worst-case-frame/placement-evidence-map.json`
- `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence.png`
- `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence-map.json`
- `artifacts/calibration/vertical-long-translation-fit-frame/fit-rules.json`

Availability: these paths identify the calibration review inputs used for approval. Some are tracked deterministic artifacts, and some may be generated/local review artifacts. Production code must not load any of them.

Manual approvals:

- Candidate evidence third candidate column was accepted after compact vertical groups stopped receiving X dampening and the book-page horizontal overlay was lifted to the semantic group top.
- Mixed-orientation final cells `#4`, `#8`, and `#12` were accepted on 2026-07-01.
- Worst-case mixed frame final cells `#4`, `#8`, `#12`, `#16`, and `#20` were accepted on 2026-07-01.
- Vertical long-translation fit final simultaneous cell `#4` was revised and accepted on 2026-07-01 after recording the `110%` semantic-area cap, no width growth, and no-shrink behavior when text fits.
- Production #32 promotion approval was granted by the project owner on 2026-07-01 for runtime overlay placement code changes under this spec.

## Current Runtime Path

Current production flow:

1. `TranslationPipelineService` creates `sourceResult` from OCR.
2. `TranslationTextGroupingService.CreateTranslationSourceResult(sourceResult, zone)` groups source OCR blocks into translator request blocks.
3. `TranslationPipelineService.CreateTranslatedResult(...)` copies translated text onto grouped source bounds.
4. `OverlayPositioningService.CreateSnapshot(...)` maps each translated block into `OverlayTextItem` and `OverlayMaskItem`.

Important boundary before promotion:

- `OverlayPositioningService` currently receives translated blocks with combined bounds, but not the raw OCR member blocks that formed each semantic group.
- The calibrated line-count rule depends on raw group members, not only on the combined bounds.
- Therefore production promotion should first preserve group metadata from grouping into overlay placement. It should not guess line count from translated text.

Implemented production boundary:

- `OcrResult` carries `TextBlockSources` sidecar metadata for each text block.
- `TranslationTextGroupingService` preserves raw member bounds when it creates grouped translation-source blocks.
- `TranslationPipelineService` carries that metadata through translated results.
- `OverlayPositioningService` applies placement and text-fit rules from this spec using runtime metadata only; calibration JSON/PNG artifacts are not loaded by production code.

## Proposed Rule Set

Apply the placement pipeline independently per semantic group:

1. Start from the existing expanded/centered overlay bounds for the translated block.
2. Compute source semantic bounds from the group source bounds.
3. Compute source line count from grouped raw OCR member bounds.
4. For horizontal groups only, apply line-count Y offset:
   - one line: `0 px`
   - additional lines: `-8 px` per additional line
5. Apply semantic right-overflow X dampening:
   - ratio: `0.5`
   - skip for vertical groups with semantic width `< 100 px`
6. For horizontal multiline groups where both line-count offset and right-overflow dampening are active, align final overlay top to semantic group top if the intermediate overlay remains lower than the semantic bounds.
7. Clamp final text bounds to non-negative screen coordinates and keep existing mask padding behavior unchanged.

Vertical long-translation text fitting:

1. Treat text fitting as a second step after placement bounds are chosen.
2. Compute semantic-group area from bounds as `semanticWidth * semanticHeight`; this is cheap enough to do per group during placement.
3. Keep overlay area within `semanticGroupArea * 1.10` and do not solve long text by uncontrolled width growth.
4. For vertical source groups, when translated text needs more height than the initial blue overlay bounds, expand the overlay upward first while keeping it inside the semantic group's usable vertical range and area cap.
5. Let the overlay occupy the semantic group's top-to-bottom vertical range when that stays within the area cap.
6. If text fits after bounded vertical expansion, keep the base font size.
7. Only when text still does not fit after bounded vertical expansion, reduce rendered font size or wrapping density instead of extending outside the semantic group.
8. Keep the mask based on source OCR bounds; only the translation text layout changes.

## Data Contract Needed For Runtime

Minimum runtime metadata per translated semantic group:

- translated text;
- combined source semantic bounds;
- raw OCR member bounds in reading order;
- source orientation mode;
- zone id and capture-region scaling context already available through `OcrResult.Request`.

Preferred implementation shape:

- Keep production metadata in the Application layer.
- Add a small grouping result type that carries grouped text plus source member bounds.
- Keep `OcrTextBlock` as the OCR primitive unless implementation review finds a narrower change.
- Keep profile JSON schema unchanged for the first promotion; the calibrated constants should be internal defaults unless the project owner explicitly approves profile-level settings.

## Layer Ownership

Allowed production touch points after approval:

- `GameTranslator.Application.Pipeline.TranslationTextGroupingService`
- `GameTranslator.Application.Pipeline.TranslationPipelineService`
- `GameTranslator.Application.Overlay.OverlayPositioningService`
- focused Application tests

Avoid:

- Domain profile schema changes unless separately approved.
- Infrastructure OCR changes unless evidence shows runtime OCR data cannot provide needed bounds.
- UI rendering changes unless the generated `OverlayTextItem`/`OverlayMaskItem` contract proves insufficient.

## Tests Required Before Promotion Is Accepted

Application tests:

- horizontal single-line group keeps existing centered placement;
- horizontal multiline group receives line-count Y offset;
- horizontal multiline group with right overflow receives X dampening and semantic-top alignment;
- compact vertical group below `100 px` semantic width does not receive X dampening;
- wider vertical group can receive X dampening without regressing accepted vertical fixtures;
- vertical long translation keeps overlay area within `110%` of semantic-group area, does not grow width, expands upward before shrinking text, keeps base font size when text fits after expansion, and does not extend below the semantic group once vertical bounds are exhausted;
- mixed-orientation frame applies rules independently per group in one snapshot;
- masks remain based on source OCR bounds and keep current padding/clamping behavior;
- previous-snapshot jitter stabilization still works with adjusted text bounds.

Calibration regression:

- keep `MixedOrientationPlacementRules_WhenAppliedPerSemanticGroup_KeepsAdjustmentsIndependent`;
- keep `MixedOrientationVisualEvidence_WhenGenerated_WritesNumberedPlacementPanels`;
- keep `MixedWorstCaseVisualEvidence_WhenGenerated_WritesNumberedPlacementPanels`;
- keep `VerticalLongTranslationFitEvidence_WhenGenerated_WritesSimultaneousFinalOverlayPanel`;
- run full calibration test suite with local tessdata when available.

Pre-promotion visual gate:

- review `artifacts/calibration/mixed-worst-case-frame/placement-evidence.png`;
- confirm final cells `#4`, `#8`, `#12`, `#16`, and `#20`;
- reviewed and accepted `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence.png` final simultaneous cell `#4`, including `0 px` width expansion, `finalOverlayArea <= semanticArea * 1.10`, and no font shrink when text fits at the base size.

Manual smoke:

- run the app on at least one horizontal multiline sample and one vertical Japanese sample;
- confirm overlay text does not cover forbidden UI/face regions;
- confirm mask still covers the original source text rather than the translated overlay area.

## Acceptance Criteria

Production promotion is acceptable only when:

- the project owner explicitly approves editing production code for issue #32;
- no restricted governance item is crossed without approval;
- production tests pass;
- `dotnet build GameTranslator.sln -c Release` passes;
- `dotnet test GameTranslator.sln -c Release --no-build` passes;
- calibration artifacts remain test-only and are not loaded by production code.

## Open Questions Before Coding

- Should calibrated constants remain hard-coded internal defaults, or become profile settings later?
- Should grouping metadata be represented as a new Application-layer type or as optional metadata attached to grouped blocks?
- Is a diagnostics-schema update needed to expose final placement offsets for manual smoke, or can this wait until after #32?
- No vertical-fit visual approval question remains open after accepting final simultaneous cell `#4`; explicit production #32 approval is still required before runtime code changes.

## Approval Gate

Production issue #32 promotion was approved on 2026-07-01. Further changes that affect overlay rules, diagnostics contracts, OCR interfaces, profile schema, architecture, or another governed area still require a new explicit approval.

## Post-Promotion Verification - 2026-07-02

Implementation status:

- Production placement promotion has been implemented in the runtime overlay placement path under the approved touch points in this spec.
- Calibration artifacts remain test-only and are not loaded by production code.
- The profile schema remains unchanged.

Runtime verification:

- `dotnet build GameTranslator.sln -c Release` passed.
- `dotnet test GameTranslator.sln -c Release --no-build` passed with `329/329` tests.
- `git diff --check` was clean except known LF-to-CRLF warnings in touched test/document files.

Manual/static evidence:

Availability: these manual/static evidence paths are historical local review snapshots, not deterministic CI inputs and not guaranteed in a clean checkout.

- Synthetic Start Live harness summary: `artifacts/manual-smoke-diagnostics/game-translator-live-manual-smoke-summary-20260702-143335.md`
- Synthetic game-zone evidence: `artifacts/manual-smoke-diagnostics/game-translator-live-game-evidence-20260702-143334.png`
- Synthetic one-large-comic-zone evidence: `artifacts/manual-smoke-diagnostics/game-translator-live-comic-evidence-20260702-143335.png`
- Real OCR/translator Start Live summary: `artifacts/manual-real-smoke-diagnostics/game-translator-real-live-smoke-summary-20260702-145600.md`
- Real English UI/dialogue evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-eng-ui-evidence-20260702-145555.png`
- Real Thai comic evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-tha-comic-evidence-20260702-145557.png`
- Real Japanese vertical evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-jpn-vertical-evidence-20260702-145558.png`

Real Start Live smoke results:

| Scenario | OCR/language | Translator | Grouping | Start Live to last overlay | Result |
| --- | --- | --- | --- | ---: | --- |
| English UI and dialogue | Tesseract `eng`, horizontal | `WebAuto` -> `GoogleWeb`, `en` -> `ru` | menu `BlockByBlock`, dialogue `WholeZone` | `937.37 ms` | `0` warnings, `0` failures |
| Thai comic bubble | Tesseract `tha`, horizontal | `WebAuto` -> `GoogleWeb`, `th` -> `ru` | `NearbyBlocks` | `812.69 ms` | `0` warnings, `0` failures |
| Japanese vertical text | Tesseract `jpn_vert`, vertical | `WebAuto` -> `GoogleWeb`, `ja` -> `ru` | `NearbyBlocks` | `2096.51 ms` | `0` warnings, `0` failures |

Notes:

- The real smoke uses a static frame capture source to exercise the application pipeline deterministically while still using real OCR and translation.
- It does not replace an interactive packaged-app smoke through Windows Graphics Capture and the window picker.
- The historical `WebAuto` → `GoogleWeb` path was external-service dependent. `WebAuto` is now removed; direct web-provider evidence remains manual-only and must not become a CI gate.

## Follow-Up Test Correction - 2026-07-02

User review identified two smoke-method issues after the initial promotion verification:

- Debug export was previously exercised through an interactive save-location dialog. Follow-up smoke must use a deterministic `IDialogService`/harness path and write debug reports directly to a project output folder.
- Placement stability was previously evaluated from a live/static smoke frame even though the project already has a fixed reference with accepted overlay values. Follow-up placement verification must use `artifacts/calibration/vertical-long-translation-fit-frame/` and compare against the accepted `fit-rules.json` / `placement-evidence-map.json` values.

Follow-up evidence:

Availability: these `outputs/` files are ignored local run products from the follow-up smoke. Regenerate or verify them locally before using them as current evidence.

- Fixed-reference summary: `outputs/game-translator-fixed-reference-user-smoke-summary-20260702-205958.md`
- Accepted overlay value digest: `outputs/game-translator-fixed-reference-overlay-values-20260702-205958.json`
- Silent user-like smoke summary: `outputs/game-translator-live-manual-smoke-summary-20260702-205834.md`
- Silent debug reports: `outputs/game-translator-live-game-debug-20260702-205832.txt`, `outputs/game-translator-live-comic-debug-20260702-205833.txt`

Follow-up validation:

- `work/AppDiagnosticsSmoke/bin/Release/net9.0-windows10.0.19041.0/AppDiagnosticsSmoke.exe --frame=artifacts/manual-smoke/full-app-overlay-smoke/full-app-overlay-smoke.png --output=outputs` passed with `0` failures and `6` existing synthetic horizontal width-ratio review warnings.
- `dotnet test GameTranslator.sln -c Release --no-build --filter FullyQualifiedName~VerticalLongTranslationFitEvidence_WhenGenerated_WritesSimultaneousFinalOverlayPanel` passed.
- `dotnet build GameTranslator.sln -c Release --no-restore` passed with `0` warnings and `0` errors.
- `dotnet test GameTranslator.sln -c Release --no-build` passed with `329/329` tests after a test-only debug-export flake fix.

Test-only flake fix:

- `tests/GameTranslator.Tests/Smoke/ProfileManagerViewModelTests.cs` now waits for final debug export `StatusMessage` before reading/deleting the hotkey-exported debug file. Waiting only for `File.Exists(...)` could race with the async export and leave the temp debug file locked during cleanup.

Boundary:

- This follow-up does not add new production overlay rules.
- Fixed calibration JSON/PNG remains test-only and is not loaded by production runtime.

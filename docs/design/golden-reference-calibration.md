# Golden Reference Calibration Sandbox

Status: active Sprint 26 test-only design note.

Scope: offline calibration tests and manual smoke for OCR fidelity, semantic grouping, translation-request order, mask source selection, overlay placement, and diagnostics packages.

## Purpose

The production application must keep the approved Capture -> OCR -> Grouping -> Translation -> Overlay flow. Calibration tests are different: they are allowed to bypass individual runtime stages with approved fixture data so agents can evaluate placement and grouping hypotheses without rewriting application code first.

This sandbox exists to save manual tuning time:

1. Build a golden reference from generated images or manually approved screenshots.
2. Store the expected text, reading order, semantic groups, translation, mask bounds, overlay bounds, and forbidden regions.
3. Run small deterministic gates against those references.
4. Ask for human visual verification when a candidate result looks good.
5. Promote only the verified rule or parameter into production code, following governance when required.

## Calibration Gates

Recommended gates:

- Fixture manifest: case type, source text, approved reading order, semantic groups, approved translation, source/mask/overlay bounds, and forbidden regions.
- OCR fidelity: generated or approved image -> OCR result, normalized text match, character/word error rate, reading order, and bounds tolerance.
- Real OCR sweep: optional local Tesseract run against generated source frames writes `artifacts/calibration/real-ocr-sweep.json`; when required `tessdata` files are missing, the artifact must report `status: unavailable` instead of failing deterministic CI. The package also writes `artifacts/calibration/real-ocr/*-source.png`, candidate `tessdata` locations, and setup instructions so reviewers can reproduce the sweep locally.
- Grouping and translation request: OCR blocks -> semantic groups -> exact request texts sent to the translator/cache layer.
- Translation meaning smoke: deterministic or offline semantic checker confirms required fragments, order, numbers, names, and negations are not lost.
- Overlay geometry: text and masks stay near approved reference bounds, inside bubble/frame/label regions, and outside forbidden regions.
- Diagnostics contract: export source image, `sourceOcr`, `translationSourceOcr`, `maskSourceOcr`, semantic groups, text/mask intersections, selected preset/settings, and no secrets.
- Candidate scorecard: compare a test-only matrix of OCR preset, grouping merge distance, mask source/padding, and overlay offset/inflation candidates against approved references, then write ranked `artifacts/calibration/scorecard.json` and visual `artifacts/calibration/candidate-evidence.png` for review.
- Contact sheet: write manual-review PNGs and fixture manifests under `artifacts/calibration/<fixture-id>/` when a calibration test needs visual approval.

The initial seed catalog should stay small and readable: one vertical CJK manga/bubble case, one vertical Japanese kana prompt, one book/page horizontal-text case, and one plain UI/text-label case. Add new cases only when they protect a distinct OCR, ordering, translation, masking, or overlay-placement risk.

## Allowed Test-Only Bypasses

Calibration tests may inject:

- captured frames or generated frames instead of live capture;
- approved OCR blocks instead of real OCR;
- approved semantic groups instead of current grouping output;
- approved translations or fake translator responses instead of network providers;
- approved overlay and mask bounds for comparison;
- experimental semantic helpers when they are deterministic or explicitly offline/manual smoke only.
- candidate OCR/grouping/mask/overlay parameter sets when they are written only as calibration evidence and not loaded by production code.

These bypasses are test-only. They must not become runtime dependencies by accident.

## Non-Negotiable Limits

Calibration tests must still obey project safety and privacy rules:

- no game memory reads/writes;
- no process hooks, DLL injection, drivers, or anti-cheat bypass;
- no secrets in JSON, logs, diagnostics, screenshots, or fixture manifests;
- no required network/API dependency in deterministic CI;
- no production behavior change solely because a calibration test passes.

## Promotion Workflow

A candidate calibration result becomes production behavior only after:

1. The calibration diagnostics package is generated.
2. The user visually approves the reference result.
3. The proposed production rule or parameter is described.
4. Required approval/ADR is completed if the change affects OCR interfaces, overlay rules, profile schema, translator behavior, diagnostics schema, architecture, or restricted areas.
5. Focused production tests are added for the promoted behavior.

Until then, calibration tests are evidence, not application rules.

## Sprint 26 Placement Decision Record - 2026-07-01

Status: accepted for calibration only. Production issue #32 promotion is not approved by this record.

Scope:

- Golden-reference calibration tests and generated artifacts only.
- Overlay placement evidence for vertical CJK/Japanese, Thai horizontal and multiline text, book-style horizontal text, plain UI text, and a mixed-orientation frame containing vertical and horizontal semantic groups in one screenshot.
- Production OCR, grouping, overlay runtime, profile schema, and diagnostics schema remain unchanged.

Reviewed evidence:

- `artifacts/calibration/candidate-evidence.png`
- `artifacts/calibration/scorecard.json`
- `artifacts/calibration/mixed-orientation-frame/contact-sheet.png`
- `artifacts/calibration/mixed-orientation-frame/group-placement-rules.json`
- `artifacts/calibration/mixed-orientation-frame/placement-evidence.png`
- `artifacts/calibration/mixed-orientation-frame/placement-evidence-map.json`
- `artifacts/calibration/mixed-worst-case-frame/placement-evidence.png`
- `artifacts/calibration/mixed-worst-case-frame/placement-evidence-map.json`
- `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence.png`
- `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence-map.json`
- `artifacts/calibration/vertical-long-translation-fit-frame/fit-rules.json`

Manual visual approvals:

- Candidate evidence third candidate column was accepted after keeping compact vertical groups out of X dampening and aligning the book-page horizontal overlay to the semantic top.
- Mixed-orientation final cells `#4`, `#8`, and `#12` were accepted by the project owner on 2026-07-01.
- Mixed worst-case final cells `#4`, `#8`, `#12`, `#16`, and `#20` were accepted by the project owner on 2026-07-01.
- Vertical long-translation fit evidence final simultaneous cell `#4` was revised and accepted by the project owner on 2026-07-01 after review: the fixture records bounded overlay area, no width growth, and no font shrink when text fits after vertical expansion.

Calibration rule conclusions:

1. Placement rules must be evaluated per semantic group, not once for the whole frame.
2. Line-count Y offset applies only to horizontal semantic groups. Single-line horizontal groups keep `0` Y offset; multiline horizontal groups use the calibrated `-8 px` step per additional line.
3. Semantic right-overflow X dampening uses a calibrated `0.5` ratio, but compact vertical semantic groups narrower than `100 px` keep their original X placement.
4. For multiline horizontal groups using both line-count offset and right-overflow dampening, the final overlay should align to the semantic group top when the intermediate overlay remains lower than the source semantic bounds.
5. Mixed-orientation frames must keep each semantic group's placement independent so a vertical group cannot inherit horizontal line-count behavior and a compact vertical group cannot receive wide-group X dampening.
6. Long translated text in vertical source groups is a text-fitting problem after placement: keep overlay area bounded to `110%` of semantic-group area, expand vertically from semantic top to bottom when the area budget allows, and keep the base font size when text fits after expansion. Reduce text size or wrapping density only when text still does not fit after bounded vertical expansion.

Observed mixed-orientation final bounds:

- `compact-vertical-japanese`, cell `#4`: `x=34,y=60,w=60,h=72`
- `single-line-horizontal-ui`, cell `#8`: `x=126,y=38,w=104,h=38`
- `two-line-horizontal-book`, cell `#12`: `x=42,y=168,w=144,h=58`

Promotion boundary:

- This record remains calibration evidence; calibration artifacts are not loaded by production code.
- Production #32 promotion was explicitly approved by the project owner on 2026-07-01 and is tracked by `docs/design/issue-32-overlay-placement-production-promotion-spec.md`.
- The accepted `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence.png` final simultaneous cell `#4` is the calibration-only evidence for the `110%` semantic-area cap, `0 px` width growth, and no-shrink behavior when text fits at the base font size.
- Any further production change affecting overlay rules, diagnostics contracts, OCR interfaces, profile schema, architecture, or another restricted governance category still requires explicit project-owner approval before editing runtime code.

## Sprint 26 Real Live Smoke Evidence - 2026-07-02

Status: accepted as post-promotion manual-smoke evidence for issue #32. This section records runtime verification evidence; it does not introduce additional calibration rules.

Scope:

- Production issue #32 placement behavior after project-owner approval and runtime implementation.
- Full `MainViewModel.StartLiveTranslationAsync` flow using a static manual frame capture source, real `TesseractOcrEngine`, real `WebAuto` translator routing to `GoogleWeb`, runtime grouping, runtime overlay placement, and debug export.
- The harness lives under ignored `work/RealAppDiagnosticsSmoke/` and is not part of deterministic CI.

Evidence:

- Summary: `artifacts/manual-real-smoke-diagnostics/game-translator-real-live-smoke-summary-20260702-145600.md`
- English UI/dialogue evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-eng-ui-evidence-20260702-145555.png`
- English UI/dialogue debug: `artifacts/manual-real-smoke-diagnostics/game-translator-real-eng-ui-debug-20260702-145555.txt`
- Thai comic bubble evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-tha-comic-evidence-20260702-145557.png`
- Thai comic bubble debug: `artifacts/manual-real-smoke-diagnostics/game-translator-real-tha-comic-debug-20260702-145557.txt`
- Japanese vertical evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-jpn-vertical-evidence-20260702-145558.png`
- Japanese vertical debug: `artifacts/manual-real-smoke-diagnostics/game-translator-real-jpn-vertical-debug-20260702-145558.txt`

Run results:

| Scenario | OCR | Translator | Grouping | Start Live to last overlay | Calls | Result |
| --- | --- | --- | --- | ---: | --- | --- |
| English UI and dialogue | Tesseract `eng`, horizontal | `WebAuto` -> `GoogleWeb`, `en` -> `ru` | menu `BlockByBlock`, dialogue `WholeZone` | `937.37 ms` | capture/OCR/translation `4/4/2` | `0` warnings, `0` failures |
| Thai comic bubble | Tesseract `tha`, horizontal | `WebAuto` -> `GoogleWeb`, `th` -> `ru` | `NearbyBlocks` | `812.69 ms` | capture/OCR/translation `3/3/1` | `0` warnings, `0` failures |
| Japanese vertical text | Tesseract `jpn_vert`, vertical | `WebAuto` -> `GoogleWeb`, `ja` -> `ru` | `NearbyBlocks` | `2096.51 ms` | capture/OCR/translation `2/2/1` | `0` warnings, `0` failures |

Decision:

- No additional placement rule change is requested from this smoke evidence.
- Runtime placement promotion remains bounded to the issue #32 rules already approved in `docs/design/issue-32-overlay-placement-production-promotion-spec.md`.
- The evidence confirms the app path from Start Live through OCR, language/provider selection, grouping, translation, overlay generation, and debug export on the manual smoke frame.

Limits and risks:

- This smoke uses a static frame capture source, so it does not replace an interactive Windows Graphics Capture/window-picker packaged-app smoke.
- `WebAuto` depends on network and external web translation behavior, so this evidence is manual-smoke only and must not become a deterministic CI requirement.
- OCR text quality remains language/model dependent; this gate verifies pipeline execution, grouping/overlay counts, bounds checks, and diagnostics rather than semantic translation perfection.

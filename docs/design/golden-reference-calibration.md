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

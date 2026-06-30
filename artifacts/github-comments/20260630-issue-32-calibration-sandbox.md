Calibration sandbox checkpoint pushed for #32 / Sprint 26:

- Commit: `a138e7b Add OCR calibration sandbox evidence`
- Scope is test-only evidence, not a production overlay rule change.

What was added:

- Offline golden-reference calibration tests for the OCR -> grouping -> translation request -> mask -> overlay path.
- Manual-review contact sheets and manifests under `artifacts/calibration/<fixture-id>/`.
- `artifacts/calibration/candidate-evidence.png` with a reviewer legend:
  - black = raw OCR block;
  - green = semantic group;
  - gray = source-text mask;
  - blue = translated overlay;
  - red = forbidden region that masks/overlays must not cover.
- A source-reference column in the evidence image, so reviewers can compare the original text without masks/overlays against each candidate panel.
- Four seed fixtures:
  - vertical Chinese/CJK bubble: `你好`;
  - vertical Japanese prompt: `セーブしますか`;
  - horizontal book/page text;
  - plain UI label.
- `artifacts/calibration/scorecard.json`, ranking a test-only parameter matrix across OCR preset, grouping merge distance, mask source/padding, and overlay offset/inflation.
- Current selected calibration candidate remains `threshold-scale_merge-30_mask-raw-4_overlay-centered`.
- Optional real OCR sweep artifact:
  - `artifacts/calibration/real-ocr-sweep.json`;
  - generated source frames under `artifacts/calibration/real-ocr/*-source.png`;
  - required local traineddata now includes `chi_sim_vert.traineddata`, `jpn_vert.traineddata`, and `eng.traineddata`;
  - when traineddata is not present locally, the artifact reports `status: unavailable` and records setup instructions instead of failing CI.

Why this should help the next developer:

- It separates visual questions that were previously tangled together:
  - did OCR find the original text;
  - did grouping create the intended semantic unit;
  - did the mask cover only accepted source glyphs;
  - did the translated overlay stay inside the intended bubble/frame/label;
  - did anything touch forbidden regions.
- The evidence image is meant for manual review before promoting any heuristic into production.
- The real OCR sweep is intentionally soft so CI stays deterministic while local reviewers can reproduce CJK/Japanese OCR behavior when the required Tesseract traineddata files are available.

Verification for this checkpoint:

- `dotnet build GameTranslator.sln -c Release` passed with 0 warnings / 0 errors.
- `dotnet test GameTranslator.sln -c Release --no-build` passed: `324/324`.
- `git diff --check` passed with only existing CRLF/LF normalization warnings.

Remaining before closing #32:

- Manually accept the new vertical Japanese row in `candidate-evidence.png`.
- Run a local real OCR sweep with `chi_sim_vert.traineddata`, `jpn_vert.traineddata`, and `eng.traineddata` available.
- Use the calibration evidence to decide whether any production OCR/grouping/overlay heuristic should be promoted. Any such promotion still needs explicit approval because it would change production behavior.

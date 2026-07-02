# Sprint 26 Calibration Chat Handoff - 2026-07-01

## Scope

This handoff documents the calibration and visual-validation work performed in the Codex chat that was compacted twice.

Work remained test-only inside the golden-reference calibration sandbox for issue #32 / Sprint 26. Production overlay placement, OCR behavior, profile schema, and runtime pipeline were not changed.

Repository:

`C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator`

Branch:

`main`

GitHub repository:

`Dementius-cell/Game-Translator`

## Required Reading Boundary

Before edits, the chat followed the repository `AGENTS.md` reading order:

1. `docs/00-project-constitution.md`
2. `docs/01-technical-specification.md`
3. `docs/02-architecture.md`
4. `docs/03-technical-risks-and-decisions.md`
5. `docs/04-implementation-roadmap.md`
6. `docs/06-sprint-development-plan.md`
7. `docs/07-definition-of-done-quality-gates.md`
8. `docs/05-ai-development-rules.md`

For this work, only calibration tests and generated calibration artifacts were changed. No production promotion was approved.

## Main Outcomes

- Real OCR sweep was made practical with local tessdata and now records `status = ran` when the tessdata path is available.
- Tesseract PSM sweep was added for vertical Japanese fixtures using `Engine.Process(image, PageSegMode)`.
- Thai calibration coverage was added:
  - `thai-horizontal-hello`
  - `thai-multiline-dialogue`
  - `thai-three-line-dialogue`
- Japanese vertical multi-column coverage was added:
  - `vertical-japanese-two-column-prompt`
  - `vertical-japanese-three-column-prompt`
- Render variants for vertical Japanese OCR were added:
  - `fixture`
  - `native-wide-28-step-29`
  - `native-block-font-24`
- Test-only overlay candidates were added:
  - `overlay-high-8`
  - `overlay-high-12`
  - `overlay-line-count-step-8`
  - `overlay-line-count-step-8-x-half-right-overflow`
- `candidate-evidence.png` now numbers every visual panel as `#N`.
- `scorecard.json` now includes `visualEvidenceCellMap` mapping each numbered panel to fixture/candidate metadata.

## Overlay Calibration State

Current evidence columns are:

1. Source reference
2. `overlay-centered`
3. `overlay-line-count-step-8`
4. `overlay-line-count-step-8-x-half-right-overflow`
5. `overlay-high-8`
6. `overlay-high-12`
7. overlay failure candidate
8. OCR/grouping failure candidate

Numbering is row-major: left to right, top to bottom.

The user reported these after the first X-dampening attempt:

- Problematic: `#4`, `#12`, `#20`
- Accepted: `#28`, `#36`, `#44`, `#52`, `#60`, `#68`

The current test-only fix adds a vertical semantic-group width gate:

- `SemanticRightOverflowDampeningMinVerticalGroupWidth = 100`
- For vertical fixtures with semantic group width below 100 px, X-dampening is not applied.
- This leaves compact vertical cases unchanged while retaining X-dampening for wider vertical and horizontal cases.

Final measured overlay bounds after the gate:

```text
vertical-cjk-basic-bubble: line=(58,56,110,92) damped=(58,56,110,92) OK
vertical-japanese-save-prompt: line=(72,64,96,80) damped=(72,64,96,80) OK
vertical-japanese-two-column-prompt: line=(68,54,104,72) damped=(68,54,104,72) OK
vertical-japanese-three-column-prompt: line=(60,54,124,78) damped=(55,54,124,78) OK
book-page-horizontal-lines: line=(48,56,144,64) damped=(42,56,144,64) OK
thai-horizontal-hello: line=(58,70,126,48) damped=(56,70,126,48) OK
thai-multiline-dialogue: line=(50,64,148,60) damped=(50,64,148,60) OK
thai-three-line-dialogue: line=(50,48,152,76) damped=(48,48,152,76) OK
plain-ui-save-game: line=(58,78,124,42) damped=(55,78,124,42) OK
```

## Real OCR Sweep State

Use this local tessdata path when running OCR evidence:

```powershell
$env:TESSDATA_PREFIX = 'C:\Users\admin\Documents\Codex\2026-06-30\game-translator-next-stage\work\tessdata_mixed'
```

Required traineddata currently used by evidence:

- `chi_sim_vert.traineddata`
- `jpn_vert.traineddata`
- `eng.traineddata`
- `tha.traineddata`

Last observed best OCR rows:

```text
vertical-cjk-basic-bubble: best fixture CER=0
vertical-japanese-save-prompt: best native-wide-28-step-29 CER=0.4286
vertical-japanese-two-column-prompt: best native-block-font-24 CER=0.4286
vertical-japanese-three-column-prompt: best native-block-font-24 CER=0.3333
book-page-horizontal-lines: best fixture CER=0
thai-horizontal-hello: best fixture CER=0
thai-multiline-dialogue: best fixture CER=0
thai-three-line-dialogue: best fixture CER=0
plain-ui-save-game: best fixture CER=1
```

`plain-ui-save-game` remains weak in the real OCR sweep and should not be interpreted as solved by this calibration pass.

## Changed Files

Tracked dirty files at handoff time:

- `artifacts/calibration/candidate-evidence.png`
- `artifacts/calibration/real-ocr-sweep.json`
- `artifacts/calibration/real-ocr/vertical-japanese-save-prompt-source.png`
- `artifacts/calibration/scorecard.json`
- `artifacts/calibration/vertical-japanese-save-prompt/contact-sheet.png`
- `tests/GameTranslator.Tests/Calibration/GoldenReferenceCalibrationTests.cs`
- `docs/handoff/sprint-26-calibration-chat-handoff-2026-07-01.md`

Untracked generated artifacts at handoff time:

- `artifacts/calibration/real-ocr/thai-horizontal-hello-source.png`
- `artifacts/calibration/real-ocr/thai-multiline-dialogue-source.png`
- `artifacts/calibration/real-ocr/thai-three-line-dialogue-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-save-prompt-native-block-font-24-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-save-prompt-native-wide-28-step-29-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-three-column-prompt-native-block-font-24-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-three-column-prompt-native-wide-28-step-29-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-three-column-prompt-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-two-column-prompt-native-block-font-24-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-two-column-prompt-native-wide-28-step-29-source.png`
- `artifacts/calibration/real-ocr/vertical-japanese-two-column-prompt-source.png`
- `artifacts/calibration/thai-horizontal-hello/`
- `artifacts/calibration/thai-multiline-dialogue/`
- `artifacts/calibration/thai-three-line-dialogue/`
- `artifacts/calibration/vertical-japanese-three-column-prompt/`
- `artifacts/calibration/vertical-japanese-two-column-prompt/`

## Validation Completed

Latest successful checks:

```powershell
dotnet build GameTranslator.sln -c Release
$env:TESSDATA_PREFIX = 'C:\Users\admin\Documents\Codex\2026-06-30\game-translator-next-stage\work\tessdata_mixed'
dotnet test GameTranslator.sln -c Release --no-build
git diff --check
```

Results:

- Build: success, 0 warnings, 0 errors.
- Tests: success, `313/313`.
- `git diff --check`: no whitespace errors; only the expected Git warning that `GoldenReferenceCalibrationTests.cs` will be normalized from LF to CRLF when Git touches it.

Note: full tests may need elevated filesystem permission in Codex because calibration tests overwrite artifacts under the checkout.

## Risks And Open Questions

- The X-dampening width gate is empirical and test-only. It is not yet a production placement rule.
- The user has visually accepted some numbered panels, but should review the latest numbered `candidate-evidence.png` once more before any production promotion.
- Production #32 overlay placement must not be changed without explicit approval.
- If production promotion is requested, read:
  - `docs/adr/README.md`
  - `docs/governance/change-approval-required.md`
  - then document the decision boundary before editing runtime code.
- #34 web-provider diagnostics still needs manual smoke.
- Do not start #29 or #30 until #28 Sprint 26 is closed/confirmed.

## Suggested Next Step

Ask the user to visually confirm the latest numbered evidence, especially:

- `#4`, `#12`, `#20`
- `#28`, `#36`, `#44`, `#52`, `#60`, `#68`

If accepted, either:

1. Continue test-only with harder fixtures such as numbers/digits and denser horizontal/vertical text, or
2. Ask for explicit approval to evaluate whether any #32 production placement change should be promoted.

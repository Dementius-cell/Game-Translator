# Sprint 26 Calibration Handoff - 2026-07-01 - Vertical Fit Accepted

User language: Russian.

Repository:

- Working repository: `C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator`
- GitHub repo: `Dementius-cell/Game-Translator`
- Branch at handoff: `main`
- Working tree is intentionally dirty with test-only calibration changes and generated artifacts.
- Production runtime under `src/` is unchanged.

Required startup in the next chat:

1. Read `AGENTS.md` and mandatory documents in the order specified there before any edits.
2. For calibration test work, also read `tests/AGENTS.md` and `tests/GameTranslator.Tests/Calibration/AGENTS.md`.
3. For production-promotion planning, read `docs/adr/README.md` and `docs/governance/change-approval-required.md`.
4. Continue in Russian.
5. Do not edit production/runtime code for issue #32 until the project owner explicitly approves production #32 promotion.

Current sprint context:

- Sprint 26 / issue #32 calibration remains active.
- Do not start #29/#30 before #28 is closed or explicitly confirmed.
- #34 web-provider diagnostics still needs manual smoke.
- Production placement/OCR runtime has not been touched in this calibration sequence.

Latest accepted visual evidence:

- Mixed-orientation final cells `#4`, `#8`, and `#12` accepted.
- Mixed worst-case final cells `#4`, `#8`, `#12`, `#16`, and `#20` accepted.
- Vertical long-translation fit final simultaneous cell `#4` accepted after revision.

Vertical long-translation rule now accepted for calibration only:

- Compute semantic-group area as `semanticWidth * semanticHeight`; this is cheap enough to do per semantic group in runtime planning.
- Keep overlay area within `semanticGroupArea * 1.10`.
- Do not solve long text by uncontrolled width growth.
- Expand upward within semantic top/bottom first, while respecting the area cap.
- Let overlay occupy semantic top-to-bottom height only when the area cap allows.
- If text fits after bounded vertical expansion, keep the base font size.
- Shrink font or wrapping density only if text still does not fit after bounded vertical expansion.
- The accepted fixture records `WidthExpansionPixels = 0`, `WasShrunk = false`, `FittedFontSizePt = 14`, and `FinalOverlayArea <= MaxOverlayArea` for the long vertical case.

Important artifacts and docs:

- `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence.png`
- `artifacts/calibration/vertical-long-translation-fit-frame/placement-evidence-map.json`
- `artifacts/calibration/vertical-long-translation-fit-frame/fit-rules.json`
- `artifacts/calibration/mixed-orientation-frame/decision-record.json`
- `docs/design/golden-reference-calibration.md`
- `docs/design/issue-32-overlay-placement-production-promotion-spec.md`
- `tests/GameTranslator.Tests/Calibration/GoldenReferenceCalibrationTests.cs`

Latest relevant test-only fixture:

- Test: `VerticalLongTranslationFitEvidence_WhenGenerated_WritesSimultaneousFinalOverlayPanel`
- Fixture id: `vertical-long-translation-fit-frame`
- Current final long-case values from generated evidence:
  - `SemanticArea = 6728`
  - `MaxOverlayAreaRatio = 1.1`
  - `MaxOverlayArea = 7400.8`
  - `FinalOverlayArea = 7308`
  - `WidthExpansionPixels = 0`
  - `WasShrunk = false`
  - `FittedFontSizePt = 14`
  - `TextFitsAtBaseFontAfterExpansion = true`

Latest verification before handoff:

- `dotnet build GameTranslator.sln -c Release` passed.
- `dotnet test GameTranslator.sln -c Release --no-build` passed with local `TESSDATA_PREFIX` pointing at a reviewer-owned tessdata folder, for example `work/tessdata_mixed`: `318/318`.
- `git diff --check` clean except the known LF-to-CRLF warning for `tests/GameTranslator.Tests/Calibration/GoldenReferenceCalibrationTests.cs`.
- `git diff --name-only -- src` returned empty.

Recommended next step:

- Start the next chat with a short Russian status that the vertical long-translation visual gate is accepted for calibration only.
- Ask whether the user wants to request explicit approval for production #32 promotion or do any additional pre-production calibration/manual-smoke step first.
- Do not begin production code changes without an explicit production #32 approval from the project owner.

# Sprint 26 Issue 32 Production Real Smoke Handoff - 2026-07-02

User language: Russian.

Repository:

- Working repository: `C:\Users\admin\Documents\Codex\2026-06-10\github-game-translator`
- GitHub repo: `Dementius-cell/Game-Translator`
- Branch at handoff: `main`
- Working tree is intentionally dirty with production issue #32 changes, test-only calibration changes, and generated/manual evidence artifacts.

Required startup in the next chat:

1. Read `AGENTS.md` and mandatory documents in the order specified there before any edits.
2. For calibration test work, also read `tests/AGENTS.md` and `tests/GameTranslator.Tests/Calibration/AGENTS.md`.
3. For production-promotion planning or further restricted changes, read `docs/adr/README.md` and `docs/governance/change-approval-required.md`.
4. Continue in Russian.
5. Do not start #29/#30 before #28 is closed or explicitly confirmed.

Current sprint context:

- Sprint 26 / issue #32 production promotion is approved and implemented.
- The project owner approved production #32 promotion on 2026-07-01 for runtime overlay placement code changes under `docs/design/issue-32-overlay-placement-production-promotion-spec.md`.
- Further changes that affect overlay rules, diagnostics contracts, OCR interfaces, profile schema, architecture, or another governed area still require explicit project-owner approval.

What changed in production scope:

- Runtime grouping now preserves source OCR member bounds for translated semantic groups.
- Runtime overlay placement applies the accepted issue #32 rules per semantic group instead of per frame.
- Vertical long-translation fitting keeps overlay area bounded to `semanticArea * 1.10`, avoids width growth, expands vertically first, and keeps the base font size when text fits after bounded expansion.
- Debug export/hotkey/button behavior was restored during the issue #32 work.

Validation already performed:

- `dotnet build GameTranslator.sln -c Release` passed.
- `dotnet test GameTranslator.sln -c Release --no-build` passed with `329/329` tests.
- Real Start Live smoke with a static manual frame, real `TesseractOcrEngine`, and real `WebAuto` translation passed with `0` warnings and `0` failures.
- `git diff --check` was clean except known LF-to-CRLF warnings in touched test/document files.

Real Start Live smoke evidence:

- Summary: `artifacts/manual-real-smoke-diagnostics/game-translator-real-live-smoke-summary-20260702-145600.md`
- English UI/dialogue evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-eng-ui-evidence-20260702-145555.png`
- English UI/dialogue debug: `artifacts/manual-real-smoke-diagnostics/game-translator-real-eng-ui-debug-20260702-145555.txt`
- Thai comic evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-tha-comic-evidence-20260702-145557.png`
- Thai comic debug: `artifacts/manual-real-smoke-diagnostics/game-translator-real-tha-comic-debug-20260702-145557.txt`
- Japanese vertical evidence: `artifacts/manual-real-smoke-diagnostics/game-translator-real-jpn-vertical-evidence-20260702-145558.png`
- Japanese vertical debug: `artifacts/manual-real-smoke-diagnostics/game-translator-real-jpn-vertical-debug-20260702-145558.txt`

Real smoke run summary:

| Scenario | OCR/language | Translator | Grouping | Start Live to last overlay | Result |
| --- | --- | --- | --- | ---: | --- |
| English UI and dialogue | Tesseract `eng`, horizontal | `WebAuto` -> `GoogleWeb`, `en` -> `ru` | menu `BlockByBlock`, dialogue `WholeZone` | `937.37 ms` | `0` warnings, `0` failures |
| Thai comic bubble | Tesseract `tha`, horizontal | `WebAuto` -> `GoogleWeb`, `th` -> `ru` | `NearbyBlocks` | `812.69 ms` | `0` warnings, `0` failures |
| Japanese vertical text | Tesseract `jpn_vert`, vertical | `WebAuto` -> `GoogleWeb`, `ja` -> `ru` | `NearbyBlocks` | `2096.51 ms` | `0` warnings, `0` failures |

Synthetic Start Live evidence:

- Summary: `artifacts/manual-smoke-diagnostics/game-translator-live-manual-smoke-summary-20260702-143335.md`
- Game-zone evidence: `artifacts/manual-smoke-diagnostics/game-translator-live-game-evidence-20260702-143334.png`
- One-large-comic-zone evidence: `artifacts/manual-smoke-diagnostics/game-translator-live-comic-evidence-20260702-143335.png`
- Synthetic run passed with `0` failures and `6` review warnings around horizontal width ratio near `1.35`.

Important docs:

- Production spec and verification: `docs/design/issue-32-overlay-placement-production-promotion-spec.md`
- Calibration decision record: `docs/design/golden-reference-calibration.md`
- Previous pre-production handoff: `docs/handoff/sprint-26-calibration-chat-handoff-2026-07-01-vertical-fit-accepted.md`

Commit/PR preparation notes:

- Include production source and focused tests for issue #32.
- Include `docs/design/issue-32-overlay-placement-production-promotion-spec.md`, `docs/design/golden-reference-calibration.md`, and this handoff.
- Decide explicitly whether generated evidence under `artifacts/manual-real-smoke-diagnostics/`, `artifacts/manual-smoke-diagnostics/`, and calibration artifact directories should be committed or kept as local review artifacts.
- `work/AppDiagnosticsSmoke/` and `work/RealAppDiagnosticsSmoke/` are ignored harnesses and should stay out of normal commits unless the project owner asks to promote a harness.

Remaining risks / next step:

- The real smoke uses a static frame capture source and does not replace an interactive packaged-app smoke through Windows Graphics Capture and the window picker.
- `WebAuto` depends on public web translation behavior and should remain manual-smoke evidence, not a deterministic CI gate.
- If #34 specifically requires interactive web-provider diagnostics from the packaged UI, run that separately before closing #34.
- Next practical step: review the staged/commit scope, then either commit issue #32 production promotion or post a GitHub issue #32 progress comment from a body file.

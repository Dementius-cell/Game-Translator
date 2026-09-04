# Local unpublished worktree status

Snapshot date: 2026-09-04

## 1. Current boundary

The inspected branch is `main`; the last product implementation baseline before this documentation sync is `2ca05091c97944ec2726a045c6307d68beb8d6f7` (`Harden live translation and add guided setup`). Published source includes the current candidate pipeline, adaptive horizontal/vertical grouping, Thai tolerance, transient-overlay retention, Bing attempt/failure diagnostics, parameter help, and the seven-step welcome tour with post-r43 startup/spotlight/OCR-guidance corrections.

After the current documentation sync, the intended local difference from `main` is exactly nine owner-protected paths: eight calibration artifacts and one calibration test. They are not staged, reverted, deleted, regenerated, or uploaded by documentation/release maintenance.

The chronological implementation and evidence record remains in [OCR/overlay work status](ocr-overlay-work-status.md). Current durable behavior is described by [architecture](../02-architecture.md), [roadmap](../04-implementation-roadmap.md), and the [user guide](../user-guide.md).

## 2. Protected tracked paths

Calibration artifacts (`8`):

- `artifacts/calibration/candidate-evidence.png`
- `artifacts/calibration/full-screen-mixed-content-frame/candidate-scorecard.json`
- `artifacts/calibration/full-screen-mixed-content-frame/readable-final-crops.png`
- `artifacts/calibration/full-screen-mixed-content-frame/readable-final-overlays.png`
- `artifacts/calibration/real-ocr-sweep.json`
- `artifacts/calibration/real-ocr/vertical-japanese-save-prompt-source.png`
- `artifacts/calibration/scorecard.json`
- `artifacts/calibration/vertical-japanese-save-prompt/contact-sheet.png`

Calibration test (`1`):

- `tests/GameTranslator.Tests/Calibration/GoldenReferenceCalibrationTests.cs`

These changes belong to Issue #35. The next action is an explicit owner review that chooses one of three outcomes per path: promote a content-safe deterministic fixture/test, regenerate and compare before promotion, or explicitly discard it. Until that decision, preservation is the correct action; a broad stage, revert, cleanup, or automatic normalization is prohibited.

## 3. Current published product behavior

- Four production modules remain: `Domain`, `Application`, `Infrastructure`, and `UI`; `GameTranslator.Tests` is the fifth solution project but not a product module. The packaged Python Paddle worker belongs to the Infrastructure adapter.
- The normal ADR-030 route is manual OCR zone → GPU Paddle detector → bounded writing-system grouping → Tesseract crop recognition → explicitly selected translator → per-region overlay.
- Seven writing-system cohorts are resolved centrally. CJK horizontal/hybrid and adaptive CJK vertical are implemented; `SpacedLeftToRight` Auto can continue coherent text beyond ten detector lines; Thai/complex South-East Asian uses its narrow evidenced tolerance and bounded line capacity.
- `ContentLayoutMode.DialogComic` is the only accepted mode. `Book` and `StaticMenu` remain future product decisions.
- Windows OCR and Tesseract remain supported engines. Vertical selection is limited to Japanese and simplified/traditional Chinese.
- Providers are `Google`, `Azure`, `Yandex`, `GoogleWeb`, `BingWeb`, and `YandexWeb`. `WebAuto` and `glhf` are removed; no provider is selected automatically and no cross-provider fallback exists.
- Profiles remain JSON schema `1.0`; credentials remain in Windows Credential Manager; translation cache TTL remains 30 days.
- Live reports retain bounded lifecycle/provider diagnostics. Under the owner-approved local evidence policy they may include bounded OCR, translation-input, and translated text, but never credentials, raw provider responses, or frame pixels. Reports have no upload path.
- Roadmap optimization items 18.1.1-18.1.3 are completed. Byte-identical OCR reuse and early detector prewarm remain deferred and owner-gated.

## 4. Portable and publication boundary

The newest retained ignored/local-only portable is:

`work/release-hardening/release-candidates/v0.1.0-pre.20260904-welcome-tour-parameter-help-r43`

r43 is an unpacked, self-contained owner-smoke candidate with the pinned Paddle runtime/model and exactly `chi_sim`, `chi_sim_vert`, `eng`, `jpn`, `jpn_vert`, and `tha` Tesseract packs. Its manifest verification passed all `30,729` records; the complete candidate contains `30,730` files / `5.020 GiB`. It is not archived, signed, committed, pushed, or published.

r43 does not contain the later source fixes for true spotlight targeting, early welcome-template startup safety, the persistent `? Тур` header action, or the explicit `Check OCR language` → `Install OCR language` guidance. A source-equivalent replacement portable has not been assembled. Therefore r43 must not be described or distributed as the current source build.

The existing GitHub Release is historical and also does not represent current `main`. A new RC/Release, archive, signature, or public upload requires a separate owner release decision and applicable QG21 evidence.

## 5. Last completed verification

- Release build: `0` warnings, `0` errors.
- Full Release test suite: `645/645`.
- Focused welcome-tour/startup/header/OCR-guidance regressions passed.
- Documentation mini-check: `0` Markdown-link problems and `0` actionable-backtick path problems.
- `git diff --check`: clean for the published implementation change.

This is the most recent completed source gate. Documentation-only changes in the current sync are rechecked separately and do not claim a new application build or runtime smoke.

## 6. Current issue boundary

Completed by the current product/documentation state and eligible for successful closure:

- #29 — RC documentation, help, and safe-default review;
- #50 — CJK horizontal/Korean hybrid profile groundwork;
- #51 — adaptive CJK vertical profile;
- #52 — Thai/complex South-East Asian profile;
- #56 — per-zone Content layout mode policy.

Remain open:

- #30 — package and publish Release 1.0; current source-equivalent package and owner publication approval are still missing;
- #34 — human validation of current provider failure UI/diagnostics;
- #35 — owner review and disposition of the nine protected calibration paths;
- #48 — parent writing-system epic while its remaining cohorts are open;
- #49 — LTR EN/RU owner-smoke acceptance;
- #53-#55 — Brahmic/Indic, RTL Hebrew, and RTL Arabic-derived implementation/evidence.

## 7. Local-only and ignored data

All `work/**`, `outputs/**`, AppData diagnostics/cache, local OCR/translation reports, source screenshots, models, generated archives, and portable candidates remain excluded from normal commits. Their presence is not a dirty-source defect and they must not be uploaded without explicit artifact-level owner approval under the [Evidence Artifact Policy](../evidence-artifacts.md).

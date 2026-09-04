# Local unpublished worktree status

Snapshot date: 2026-09-04

## 1. Current boundary

The inspected branch is `main`; current published HEAD before the r44 status update is `20e2e1b3611002d8a2974926185fe0f31bd277bf` (`Refresh project and module documentation`), and the last product implementation baseline is `2ca05091c97944ec2726a045c6307d68beb8d6f7` (`Harden live translation and add guided setup`). Published source includes the current candidate pipeline, adaptive horizontal/vertical grouping, Thai tolerance, transient-overlay retention, Bing attempt/failure diagnostics, parameter help, and the seven-step welcome tour with post-r43 startup/spotlight/OCR-guidance corrections.

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

`work/release-hardening/release-candidates/v0.1.0-pre.20260904-post-r43-current-source-r44`

r44 is an unpacked, self-contained source-equivalent candidate with the pinned Paddle runtime/model and exactly `chi_sim`, `chi_sim_vert`, `eng`, `jpn`, `jpn_vert`, and `tha` Tesseract packs. Hidden packaged Tesseract smoke passed; packaged Paddle reached Ready in `2731.3 ms`, returned its first Standard result in `3892.1 ms`, and found `28` candidates. Independent verification passed all `30,729` checksum records with no malformed, duplicate, unsafe, missing, mismatched, or extra path; the complete candidate contains `30,730` files / `5.020 GiB`. Applicable product assemblies match the current Release outputs and the packaged worker text matches source. It is not archived, signed, committed, pushed, owner-live accepted, or published.

r44 contains the true spotlight targeting, early welcome-template startup safety, persistent `? Тур` header action, explicit `Check OCR language` → `Install OCR language` guidance, and all other source behavior at published HEAD `20e2e1b`. The superseded local r42 and r43 directories were permanently removed after r44 verification; their historical evidence remains in [OCR/overlay work status](ocr-overlay-work-status.md).

The existing GitHub Release is historical and also does not represent current `main`. A new RC/Release, archive, signature, or public upload requires a separate owner release decision and applicable QG21 evidence.

## 5. Last completed verification

- Release build: `0` warnings, `0` errors.
- Full Release test suite: `645/645`.
- Focused welcome-tour/startup/header/OCR-guidance regressions passed.
- Documentation mini-check: `0` Markdown-link problems and `0` actionable-backtick path problems.
- `git diff --check`: clean for the published implementation change.
- Local r44 package: Tesseract smoke `passed`, Paddle headless `passed`, all `30,729` checksum entries independently rehashed, six language packs matched the runtime lock, no generated transfer archive.

The `645/645` suite is the most recent completed source gate. The later r44 package gate reuses that source build and adds packaged-runtime smoke and integrity evidence; documentation-only status changes are rechecked separately.

## 6. Current issue boundary

Completed by the current product/documentation state and eligible for successful closure:

- #29 — RC documentation, help, and safe-default review;
- #50 — CJK horizontal/Korean hybrid profile groundwork;
- #51 — adaptive CJK vertical profile;
- #52 — Thai/complex South-East Asian profile;
- #56 — per-zone Content layout mode policy.

Remain open:

- #30 — package and publish Release 1.0; a local source-equivalent r44 directory now exists, but owner acceptance, transfer archive, signing, and publication approval are still missing;
- #34 — human validation of current provider failure UI/diagnostics;
- #35 — owner review and disposition of the nine protected calibration paths;
- #48 — parent writing-system epic while its remaining cohorts are open;
- #49 — LTR EN/RU owner-smoke acceptance;
- #53-#55 — Brahmic/Indic, RTL Hebrew, and RTL Arabic-derived implementation/evidence.

## 7. Local-only and ignored data

All `work/**`, `outputs/**`, AppData diagnostics/cache, local OCR/translation reports, source screenshots, models, generated archives, and portable candidates remain excluded from normal commits. Their presence is not a dirty-source defect and they must not be uploaded without explicit artifact-level owner approval under the [Evidence Artifact Policy](../evidence-artifacts.md).

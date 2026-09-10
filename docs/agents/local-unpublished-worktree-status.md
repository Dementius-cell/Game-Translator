# Local unpublished worktree status

## Publication update — 2026-09-10

Current source includes Korean detector-line OCR, preserved Korean spaces, ADR-032 and punctuation-only translation filtering. Local r46 package smoke and integrity passed. Owner live evidence confirms punctuation filtering and complete recognized line counts; numeric OCR instability and letter/digit watermark noise remain open. A roughly 10-second first-result delay was observed once; a startup timeout/restart is suspected, not proven. Three subsequent detector starts completed in 3.1–3.4 seconds. Local crop experiments reproduced digit instability; switching RawLine to SingleLine or adding crop padding did not provide a reliable correction, so no speculative OCR change was shipped.

The local 681-test result includes protected unpublished calibration changes. Publication verification passed on an export of the staged source with the committed calibration version: 676/676 tests, Release build with zero warnings/errors; Markdown link checks passed. Images, OCR transcripts, model binaries, logs, portable packages and the ten protected paths are excluded. Historical sections below describe earlier local snapshots; they are not the current publication state.


Snapshot date: 2026-09-08

## Latest delivery: 2026-09-08

The local worktree additionally contains owner-authorized Korean OCR/whitespace corrections and the verified unpacked r45 with ADR-032. Current source gates are 668/668 tests and Release build with zero warnings/errors; calibration tests write only to an isolated local root. See [r45 handoff](korean-ocr-r45-status.md) for the package, exact behavior, local evidence and remaining owner smoke. The r44 package and earlier verification below are retained historical baseline information. All ten protected/safeguarded paths and the root owner language pack remain unchanged.

## 1. Current boundary

The inspected branch is `main`; current published HEAD is `9c40f4ed081a4497723968aafa1b7cd0c577b5a6` (`Record cleanup of obsolete C drive runtimes`). Published source includes the current candidate pipeline, adaptive horizontal/vertical grouping, Thai tolerance, transient-overlay retention, Bing attempt/failure diagnostics, parameter help, the seven-step welcome tour with post-r43 startup/spotlight/OCR-guidance corrections, current documentation, r44 status, and the recorded C-drive cleanup.

The active local implementation adds the owner-approved ADR-032 empty-OCR watchdog, its lifecycle diagnostics, tests and documentation. In addition, nine owner-protected paths remain dirty: eight calibration artifacts and one calibration test. A 2026-09-07 handoff audit found one more modified calibration image, `artifacts/calibration/full-screen-mixed-content-frame/clean-frame.png`, after it had been opened as the static live-smoke source. That modification was not intentional and its cause is not established, so it is safeguarded separately pending owner disposition. None of these ten paths may be staged, reverted, deleted, regenerated, or uploaded by watchdog/documentation maintenance.

The chronological implementation and evidence record remains in [OCR/overlay work status](ocr-overlay-work-status.md). Current durable behavior is described by [architecture](../02-architecture.md), [roadmap](../04-implementation-roadmap.md), and the [user guide](../user-guide.md).

## 2. Protected and safeguarded tracked paths

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

Additional safeguarded path discovered during the 2026-09-07 reinstall handoff audit:

- `artifacts/calibration/full-screen-mixed-content-frame/clean-frame.png`

Its working SHA-256 is `E30C39CAAABB0E07FA4804415220F67A9600007B96D58098A63C3F304DFCFF72`; the published HEAD blob is `29110e574425ab923ab318696093c969c22ae4ea`. It was used only as a visible static source for live-smoke, not as an intended calibration edit. Do not fold it into Issue #35 or restore it automatically until the owner reviews the difference.

## 3. Current published product behavior

- Four production modules remain: `Domain`, `Application`, `Infrastructure`, and `UI`; `GameTranslator.Tests` is the fifth solution project but not a product module. The packaged Python Paddle worker belongs to the Infrastructure adapter.
- The normal ADR-030 route is manual OCR zone в†’ GPU Paddle detector в†’ bounded writing-system grouping в†’ Tesseract crop recognition в†’ explicitly selected translator в†’ per-region overlay.
- Seven writing-system cohorts are resolved centrally. CJK horizontal/hybrid and adaptive CJK vertical are implemented; `SpacedLeftToRight` Auto can continue coherent text beyond ten detector lines; Thai/complex South-East Asian uses its narrow evidenced tolerance and bounded line capacity.
- `ContentLayoutMode.DialogComic` is the only accepted mode. `Book` and `StaticMenu` remain future product decisions.
- Windows OCR and Tesseract remain supported engines. Vertical selection is limited to Japanese and simplified/traditional Chinese.
- Providers are `Google`, `Azure`, `Yandex`, `GoogleWeb`, `BingWeb`, and `YandexWeb`. `WebAuto` and `glhf` are removed; no provider is selected automatically and no cross-provider fallback exists.
- Profiles remain JSON schema `1.0`; credentials remain in Windows Credential Manager; translation cache TTL remains 30 days.
- Live reports retain bounded lifecycle/provider diagnostics. Under the owner-approved local evidence policy they may include bounded OCR, translation-input, and translated text, but never credentials, raw provider responses, or frame pixels. Reports have no upload path.
- ADR-032 retries an unchanged live candidate after consecutive completed empty OCR results with bounded `5 s` в†’ `10 s` в†’ `20 s` backoff. The retry remains candidate-local, resets after any non-empty OCR and reconfirms grouping from the third empty result.
- Roadmap optimization items 18.1.1-18.1.3 are completed. Byte-identical OCR reuse and early detector prewarm remain deferred and owner-gated.

## 4. Portable and publication boundary

The prior retained ignored/local-only portable (r44 baseline) is:

`work/release-hardening/release-candidates/v0.1.0-pre.20260904-post-r43-current-source-r44`

r44 is an unpacked, self-contained source-equivalent candidate with the pinned Paddle runtime/model and exactly `chi_sim`, `chi_sim_vert`, `eng`, `jpn`, `jpn_vert`, and `tha` Tesseract packs. Hidden packaged Tesseract smoke passed; packaged Paddle reached Ready in `2731.3 ms`, returned its first Standard result in `3892.1 ms`, and found `28` candidates. Independent verification passed all `30,729` checksum records with no malformed, duplicate, unsafe, missing, mismatched, or extra path; the complete candidate contains `30,730` files / `5.020 GiB`. Applicable product assemblies match the current Release outputs and the packaged worker text matches source. It is not archived, signed, committed, pushed, owner-live accepted, or published.

r44 contains the true spotlight targeting, early welcome-template startup safety, persistent `? РўСѓСЂ` header action, explicit `Check OCR language` в†’ `Install OCR language` guidance, and all other source behavior at published HEAD `20e2e1b`. The superseded local r42 and r43 directories were permanently removed after r44 verification; their historical evidence remains in [OCR/overlay work status](ocr-overlay-work-status.md).

r44 predates ADR-032. The separately owner-authorized r45 now includes it and the Korean fixes; see the latest-delivery section above.

The existing GitHub Release is historical and also does not represent current `main`. A new RC/Release, archive, signature, or public upload requires a separate owner release decision and applicable QG21 evidence.

## 5. Previous completed verification (before r45)

- Release build: `0` warnings, `0` errors.
- Full Release test suite: `648/648`.
- Focused watchdog pipeline/diagnostics regressions: `73/73`.
- Documentation mini-check: `0` Markdown-link problems and `0` actionable-backtick path problems.
- `git diff --check`: clean for the published implementation change.
- Local r44 package: Tesseract smoke `passed`, Paddle headless `passed`, all `30,729` checksum entries independently rehashed, six language packs matched the runtime lock, no generated transfer archive.
- 2026-09-07 live-smoke: four completed empty OCR candidates emitted `CandidateEmptyOcrRetryScheduled` after `5008.6` to `5146.7 ms`; each immediately started `WorkAttempt=2` with `GroupingReset=False`. The final local-only report is `C:\Users\admin\AppData\Local\GameTranslator\Diagnostics\Live\game-translator-live-live-stopped-20260907-193645-533-0003.txt`.

The `648/648` suite is the most recent completed source gate for the local ADR-032 implementation. The earlier r44 package gate predates this source change and adds packaged-runtime smoke and integrity evidence only for its own source baseline; documentation changes are rechecked separately.

## 6. Current issue boundary

Completed by the current product/documentation state and eligible for successful closure:

- #29 вЂ” RC documentation, help, and safe-default review;
- #50 вЂ” CJK horizontal/Korean hybrid profile groundwork;
- #51 вЂ” adaptive CJK vertical profile;
- #52 вЂ” Thai/complex South-East Asian profile;
- #56 вЂ” per-zone Content layout mode policy.

Remain open:

- #30 вЂ” package and publish Release 1.0; a local source-equivalent r44 directory now exists, but owner acceptance, transfer archive, signing, and publication approval are still missing;
- #34 вЂ” human validation of current provider failure UI/diagnostics;
- #35 вЂ” owner review and disposition of the nine protected calibration paths;
- #48 вЂ” parent writing-system epic while its remaining cohorts are open;
- #49 вЂ” LTR EN/RU owner-smoke acceptance;
- #53-#55 вЂ” Brahmic/Indic, RTL Hebrew, and RTL Arabic-derived implementation/evidence.

## 7. Local-only and ignored data

All `work/**`, `outputs/**`, AppData diagnostics/cache, local OCR/translation reports, source screenshots, models, generated archives, and portable candidates remain excluded from normal commits. Their presence is not a dirty-source defect and they must not be uploaded without explicit artifact-level owner approval under the [Evidence Artifact Policy](../evidence-artifacts.md).

The self-contained reinstall handoff is [reinstall-handoff-2026-09-07.md](reinstall-handoff-2026-09-07.md).

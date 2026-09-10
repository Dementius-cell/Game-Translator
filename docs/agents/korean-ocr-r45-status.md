# Korean OCR and r45 owner-check handoff

## Publication update — 2026-09-10

Current source includes Korean detector-line OCR, preserved Korean spaces, ADR-032 and punctuation-only translation filtering. Local r46 package smoke and integrity passed. Owner live evidence confirms punctuation filtering and complete recognized line counts; numeric OCR instability and letter/digit watermark noise remain open. A roughly 10-second first-result delay was observed once; a startup timeout/restart is suspected, not proven. Three subsequent detector starts completed in 3.1–3.4 seconds. Local crop experiments reproduced digit instability; switching RawLine to SingleLine or adding crop padding did not provide a reliable correction, so no speculative OCR change was shipped.

The local 681-test result includes protected unpublished calibration changes. Publication verification passed on an export of the staged source with the committed calibration version: 676/676 tests, Release build with zero warnings/errors; Markdown link checks passed. Images, OCR transcripts, model binaries, logs, portable packages and the ten protected paths are excluded. Historical sections below describe earlier local snapshots; they are not the current publication state.


Date: 2026-09-08. Status: ready for owner live verification; local-only unpacked package, not published.

## Delivered behavior

- Korean whitespace is preserved in OCR comparison, translation inputs and cache keys. Existing cache rows and profiles are not cleared or migrated.
- Both candidate routes pass crop-relative detector member bounds through preprocessing. Korean horizontal crops with 1..12 vertically non-overlapping members, each at least 20 px high and width/height at least 1.5, use one RawLine operation per member inside the existing three-slot native executor. Other candidates retain the previous recognizer behavior; no region is discarded by this eligibility rule.
- Raw OCR bounds remain intact. Local lifecycle output includes OcrRecognition=DetectorRawLine plus OcrExpectedLines, OcrRecognizedLines and OcrMissingLines. Missing lines remain visible as diagnostics; this is not a universal quality score or an unbounded retry.
- ADR-032 is included unchanged: candidate-local 5/10/20-second empty-OCR retry, grouping reconfirmation from the third empty result, and reset after non-empty OCR.
- The runtime lock now includes the officially verified kor fast traineddata pack. Build/runtime verification accepts an explicitly selected, fully hash-checked language-data directory, so the owner-preserved root chi_sim_vert pack was never replaced.

## Verification

- Release build: 0 warnings, 0 errors. All 668 tests passed: 652 normal tests and 16 calibration tests using an isolated output root. Calibration source and the ten safeguarded working paths were not modified by test execution.
- Local real-Paddle/real-Tesseract comparison on the two owner-provided images: all 15 ordinary dialogue lines recovered, including the previously omitted line and split first line. Three passes over nine detected candidates produced 27 paired observations. Tiny watermark candidates and the near-square sound-effect candidate retained baseline output after restricting RawLine eligibility.
- Warm eligible-crop mean: 52.46 ms baseline, 59.26 ms new (+12.9%, +6.79 ms); maximum new warm observation 86.23 ms. The extra work recognizes previously lost text. This small local benchmark is not a full live latency/CPU acceptance: the harness main-process peak working set was 124,805,120 bytes and total CPU was 3,593.75 ms across the comparison; these figures exclude the separate Paddle worker. Owner live acceptance remains pending.
- Packaged Tesseract smoke: passed, 308 ms. Packaged Paddle: Ready 3236.6 ms, first result 4404.3 ms, 28 candidates.
- Independent integrity: 30730 checksum entries, 30731 files, 5392207065 bytes, 4 product assemblies matching Release outputs, 7 lock-matching packs and model hashes, matching worker source, no extra/missing/hash-mismatched paths or links.
- Ten protected path hashes unchanged. Root owner chi_sim_vert SHA-256 remains ea672a78157199c333aa12ec4e74550077689b545df5fc770903716850c8b2e5. No stage, commit, push, archive, signature or publication.

## Local paths and remaining work

Package (ignored local build output): work/release-hardening/release-candidates/v0.1.0-pre.20260908-korean-ocr-watchdog-r45; start app/GameTranslator.UI.exe inside it. Full Python/runtime/models are copied; no junctions are used.

Comparison, input images, test outputs and integrity report (ignored local-only evidence): work/korean-ocr-r45. Do not upload these files. The intermediate failed PowerShell 7 publish is retained there as failed-pwsh7-publish; the successful build used Windows PowerShell.

Keep Tesseract, ko->ru and the same explicitly selected YandexWeb provider for the comparison. Repeat the two images with static pauses, including a 40-45-second pause when an empty candidate remains visible. The stylized white-on-black lettering still has OCR errors; RawLine is not a universal fix for decorative text or translator quality. Actual live 10/20-second watchdog branches, grouping reset and non-empty reset still need owner/device smoke; deterministic watchdog tests passed. No visible WPF live capture was launched during this task.

QG status: build/warnings/architecture/tests, OCR geometry, cache, diagnostics, multi-zone and vertical regressions passed; pinned runtime and package smoke/integrity passed on this same host. QG5 owner acceptance of the measured crop-time increase and full live performance is pending; QG21 physical clean-host/install/rollback, visible owner acceptance, archive/signing/publication are not claimed. Profile schema, hotkeys, mask/layout, credentials, updater and provider selection were unchanged.

Scope is the direct owner request; open GitHub issues were checked read-only. No issue was closed, and no local OCR evidence was uploaded. Existing accepted ADRs were not rewritten. Application/Infrastructure AGENTS contracts were updated; ownership and the parent index are unchanged.

## 2026-09-09 punctuation filter follow-up (source only)

Owner requested filtering OCR noise before translation. The Application translation projection now removes standalone Unicode punctuation plus bars/tildes before grouping/cache, while keeping raw OCR and watchdog semantics. Numeric and letter-containing watermark fragments remain deliberately unclassified; short legitimate numbers and dialogue must survive. Standalone punctuation dialogue is suppressed by this policy; punctuation within text remains.

Verification: 13 added regressions cover rejected punctuation, retained letters/numbers/symbols, mixed-block geometry in three grouping modes, and no empty-OCR watchdog for filtered nonempty recognition. Local generated test evidence is under work/punctuation-filter-tests (ignored, reproducible). Calibration tests run in the local isolated work/korean-ocr-r45/calibration-sandbox; protected root evidence is not regenerated. No portable was rebuilt for this follow-up; r45 still contains the earlier source.

Synthetic grouping microbenchmark, 100,000 calls after 10,000 warmups, tiered compilation disabled: two useful blocks 3.372 -> 3.460 microseconds/call, 4568 -> 4592 allocated bytes/call; one useful block plus punctuation 3.556 -> 0.286 microseconds/call, 4272 -> 680 bytes/call. This measures only grouping, not live end-to-end latency. The local ignored harness is work/punctuation-filter-tests/benchmark.

Owner live logs confirmed 5/10/20/20-second empty OCR retries and renewed grouping confirmation on the same candidate. The deterministic nonempty-deferred reset test also passed; live reset-after-nonempty is not claimed.

## Portable r46 (2026-09-09)

Local-only, self-contained win-x64 package: work/release-hardening/release-candidates/v0.1.0-pre.20260909-punctuation-filter-r46/app/GameTranslator.UI.exe. Includes the punctuation filter, Korean OCR corrections and ADR-032; watermark text with letters/numbers remains outside the filter. Supersedes r45 for owner verification; r45 is retained.

Package gates passed: Tesseract synthetic smoke 327.6 ms; packaged detector ready 2976.3 ms, first result 3970.6 ms with 19 candidates on the local owner image. Independently verified all 30,730 checksum entries, 30,731 files / 5,392,208,184 bytes, four product DLLs against applicable Release outputs (UI and Infrastructure use win-x64), and all ten protected paths unchanged. Evidence is local/ignored under work/punctuation-filter-tests/package-*. No archive or publication. Source validation remains 681/681 tests; owner live verification of r46 pending.

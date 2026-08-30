# Local unpublished worktree status

Snapshot date: 2026-08-30

## 1. Scope and authority

This document inventories changes that exist only in the local `main` worktree relative to Git HEAD `77c99382038bffdae2d0092d3f49e5c63d061cf9` (`feat: approve ADR-030 live diagnostics and layout policy`, 2026-08-22). It is a local review and handoff record, not proof that GitHub, an Issue, a pull request or a release contains these changes.

At this snapshot the worktree has `70` tracked paths different from HEAD (`69` modified and `1` deleted), plus `8` untracked files including this inventory. Nothing is staged. Existing user-owned calibration/evidence changes are preserved and must not be normalized, reverted or staged mechanically.

The chronological evidence remains in [OCR/overlay work status](ocr-overlay-work-status.md). Durable product boundaries are reflected in [architecture](../02-architecture.md), [roadmap section 18](../04-implementation-roadmap.md), and the [experimental web-provider smoke guide](../smoke/sprint-26-experimental-web-translators.md).

## 2. Implemented local product behavior

### 2.1 Candidate pipeline performance and authority

- Native Tesseract crop recognition runs outside the caller through a process-wide three-slot bounded executor while preserving one disposable engine per request and the public OCR seam.
- Candidate completion wakes a separate serialized collection/publication path, so a revision-valid completed overlay does not wait for the next detector poll.
- The unused full-zone frame fingerprint copy was removed; byte-exact crop identity remains authoritative.
- Candidate identity may survive bounded detector jitter only through a deterministic one-to-one match with the same member count, IoU at least `0.95`, and each outer edge within `4 px` of its discovery anchor.
- Lifecycle events are delivered incrementally and retained in a bounded `131072`-event queue.
- Fast/Balanced/Conservative live stability requires both matching grouping observations and minimum wall-clock grouping durations; source/member revision resets both grouping and OCR stability.
- Exact overlapping translation-cache misses coalesce per cache key without serializing unrelated text.
- Roadmap 18.1 items 1-3 are implemented. Byte-identical OCR reuse and detector prewarm, items 4-5, remain unimplemented and owner-gated.

### 2.2 OCR grouping, reading order and filtering

- Per-zone candidate grouping settings preserve schema `1.0` Auto defaults and explicit `1..12` hard overrides.
- Horizontal Auto keeps existing writing-system limits with a narrow strictly aligned continuation up to ten lines.
- `CjkVertical` Auto has no fixed column count. It evaluates immediate columns right-to-left and cuts on local gap plus whole-group width/alignment/overlap coherence, preventing transitive page-wide creep.
- Explicit `MaximumVerticalColumns` remains the old hard-limit path; every non-`CjkVertical` writing-system profile preserves its prior behavior.
- A tightly gated ragged-bottom allowance accepts proven three-column live geometry only with shared overlap at least `0.95`, normalized top offset at most `0.05`, normalized bottom offset at most `1.0`, and every adjacent gap at most `2 px`.
- Post-OCR translation grouping orders vertical CJK columns right-to-left and text inside a column top-to-bottom.
- The candidate batch/live paths apply the existing vertical-CJK recognition guard consistently before stability, cache and provider work.
- A wide aggregate accepted by grouping can pass the CJK post-filter only when it has at least two fully contained source members and every member independently has vertical aspect ratio. A single wide candidate or any horizontal member still fails.
- Vertical orientation remains selectable only for installed language layouts that support it; Japanese and simplified/traditional Chinese retain the vertical path.

### 2.3 Translation providers and cache output

- Aggregate `WebAuto` and the temporary `glhf` alias are removed from implementation, dependency injection, credential metadata and UI selection. Existing profiles remain readable but require an explicit supported replacement.
- `GoogleWeb`, `BingWeb` and `YandexWeb` are selected directly. None is a silent default or fallback for another provider.
- YandexWeb uses one direct Android-form request. Its narrow output sanitizer repairs only proven pathological provider repetitions on misses and old/new cache hits; intentionally repeated source text, short repetitions and other providers are unchanged.
- BingWeb uses one direct request, a provider-local `15 s` timeout, a warning on the first consecutive timeout, a `60 s` pause after the second, and an immediate pause for HTTP `429` with a longer valid `Retry-After` honored. Success resets the counter.
- Bing does not immediately retry the same text or switch providers. An authoritative timeout/throttle keeps a previously published overlay visible without republishing its stale revision.
- Provider exceptions carry failure kind, retry interval and consecutive count for UI status. The current stopped-session lifecycle report can lose the detailed timeout/429 kind after recovery even though it retains translation-stage failure events.

### 2.4 Chinese detector presets and recognizer research

- Every OCR zone stores a `TextCandidateDetectorPreset`; missing JSON remains `Standard` and invalid enum values fail profile validation.
- Standard uses Paddle `threshold=0.30`, `boxThreshold=0.60`, `unclipRatio=1.20`.
- Chinese-only opt-ins use `boxThreshold=0.65` and `0.70`; every non-Chinese language safely resolves them back to Standard.
- Thresholds are passed per detector request through the existing persistent predictor without model reload.
- Local A/B evidence supports `0.65` only as an owner-smoke opt-in. The global default remains `0.60`.
- Local PP-OCRv5 mobile/server recognition research is not wired into production or portable packaging. It improved horizontal Chinese crops but failed a tall vertical crop that Tesseract recognized, so production adoption remains gated on a broader annotated benchmark, vertical routing/packaging decisions and a separate ADR.

### 2.5 UI and saved-zone controls

- The zone editor exposes detector preset, horizontal/vertical Auto grouping and explicit limits with compatibility-safe defaults and validation.
- OCR preprocessing labels are separated from live timing; the explicit non-default tiny-source preset performs heavier scaling and preprocessing.
- Unsupported vertical-language combinations force Horizontal and disable the vertical selector with a reason.
- Pipeline status has warning/error severity suitable for provider timeout/throttle visibility.
- Shared WPF spacing is substantially denser while keeping desktop typography readable: smaller rail, chrome, card padding, repeated gaps, tab strip, previews and control heights; commands, bindings, validation and tab ownership are unchanged.

### 2.6 Diagnostics and privacy boundary

- Candidate diagnostics include grouping/stability counts and durations, cache counters, provider timestamps, ordered OCR/group-member bounds, bounded geometry fingerprints, resolved writing-system/orientation and detector-preset metrics.
- By explicit owner decision, automatic local live reports may also retain ordered OCR text, actual translation inputs and final translated output. Each category is limited to `16` entries; each entry is normalized to one line and limited to `512` UTF-16 code units without splitting a surrogate pair.
- The report is capped at `99,000,000` UTF-8 bytes and preserves its header plus newest tail when truncated.
- Raw HTTP/provider responses, credentials and frame pixels are excluded. The application has no diagnostic upload path. These reports are local-only and must not be uploaded or copied into tracked evidence.
- OCR-stage failures retain bounded exception type/message diagnostics without secret or frame content.

### 2.7 Portable packaging hardening

- Portable output is self-contained for `.NET 9`/WindowsDesktop and includes the pinned Paddle runtime/model plus exactly `chi_sim`, `eng`, `jpn`, `jpn_vert`, and `tha` Tesseract packs.
- Packaging preserves correct WPF runtime assemblies, validates the Infrastructure direct-reference closure, keeps Tesseract native binaries in `x64`/`x86`, rejects root-level duplicates and runs a hidden packaged OCR smoke.
- Worker IPC is explicit UTF-8 without preamble; the Python worker defensively accepts the known BOM/mojibake first-request variants.
- The release verifier sends the complete Standard detector request and checks runtime/model/language-pack paths, manifest safety and hashes.
- Current ignored/local-only candidates are r34, r35, r36, r37, r38 and r39. r35 and r34 have archives recorded in the chronological status; r36-r39 are unpacked candidates without a generated transfer archive.
- r39 is the newest owner-smoke candidate. It is not signed, committed, pushed or published.

## 3. Verification and owner-live evidence

- Latest source gate before r39: focused candidate-region tests `16/16`; focused OCR/grouping/pipeline tests `111/111`; Release build with zero warnings/errors; full Release suite `625/625`; docs mini-check clean; `git diff --check` clean apart from line-ending notices.
- r39 manifest independently rehashed `30,728` entries with no malformed, duplicate, unsafe, missing, mismatched or extra file. Packaged Tesseract and Paddle hidden smokes passed.
- Owner r39 live smoke retained `41` wide multi-member CJK completions: `12` two-column, `19` three-column and `10` four-column. `33` reached non-empty OCR/translation; `8` were OCR-empty.
- The exact four-column `115x101` case completed as one candidate, four OCR blocks and one translation request/output. No group exceeded four members in this run and no adjacent-bubble/page-wide merge was observed.
- Chinese translations contained no run of three or more consecutive identical words. No rapid non-empty → empty → non-empty publication gap under one second was found.
- Bing produced `28` translation-stage failures across three sessions, consistent with the `15 s` timeout and later `60 s` pause. Failures did not clear an existing overlay. No HTTP `429` marker occurred in this sample.
- Yandex Chinese and English sessions had no translation-stage failure. One accepted three-column Chinese crop remained partially recognized by Tesseract, so post-filter acceptance does not imply perfect OCR text.

## 4. Known limits and deferred work

- Do not tune grouping further from the accepted r39 evidence unless a future report shows a concrete neighbor-bubble merge or new failing geometry.
- Yandex translation quality/repetition beyond the existing narrow sanitizer is separate from candidate grouping.
- Chinese SFX noise and slower perceived Chinese OCR remain open quality/performance observations; no SFX classifier or PP-OCR recognizer is in production.
- The stopped-session report should retain explicit Bing timeout/throttle kind and cooldown state if UI-state proof becomes required; current evidence proves translation-stage failures and timing, not the exact warning/error presentation after recovery.
- Roadmap 18.1 items 4-5 remain out of scope.
- Local calibration PNG/JSON changes and ignored OCR-text research evidence require explicit owner review before any future staging or publication.

## 5. File-level coverage relative to HEAD

Tracked calibration/evidence changes (`8`):

- `artifacts/calibration/candidate-evidence.png`
- `artifacts/calibration/full-screen-mixed-content-frame/candidate-scorecard.json`
- `artifacts/calibration/full-screen-mixed-content-frame/readable-final-crops.png`
- `artifacts/calibration/full-screen-mixed-content-frame/readable-final-overlays.png`
- `artifacts/calibration/real-ocr-sweep.json`
- `artifacts/calibration/real-ocr/vertical-japanese-save-prompt-source.png`
- `artifacts/calibration/scorecard.json`
- `artifacts/calibration/vertical-japanese-save-prompt/contact-sheet.png`

Tracked documentation changes (`5`, before this new file):

- `docs/02-architecture.md`
- `docs/04-implementation-roadmap.md`
- `docs/05-ai-development-rules.md`
- `docs/agents/ocr-overlay-work-status.md`
- `docs/smoke/sprint-26-experimental-web-translators.md`

Tracked Application changes (`18`):

- cache: `TranslationCacheResult.cs`, `TranslationCacheService.cs`
- credentials: `TranslatorCredentialService.cs`
- OCR: `BoundedTextCandidateGroupingService.cs`, `ITextCandidateDetector.cs`, `OcrRequest.cs`, `OcrService.cs`, `TesseractLanguageCatalog.cs`, `TextCandidateRegionOcrService.cs`
- pipeline: `CandidatePipelineReadiness.cs`, `LiveCandidateLifecycleEvent.cs`, `LiveTranslationPipelineUpdate.cs`, `TranslationPipelineRunOptions.cs`, `TranslationPipelineService.cs`, `TranslationPipelineTextStability.cs`, `TranslationTextGroupingService.cs`
- translation: `TranslatorProviderException.cs`, `TranslatorProviderFailureKind.cs`

Tracked Domain changes (`3`): `OcrZone.cs`, `ProfileValidationErrorCodes.cs`, `ProfileValidator.cs`.

Tracked Infrastructure changes (`9`):

- `src/GameTranslator.Infrastructure/AGENTS.md`
- composition registration
- Paddle detector, Tesseract engine and Python detector worker
- BingWeb, GoogleWeb and YandexWeb providers
- deleted tracked `WebAutoTranslatorProvider.cs`

Tracked UI changes (`5`): `App.xaml.cs`, `GameTranslator.UI.csproj`, `MainViewModel.cs`, `OcrZoneEditorViewModel.cs`, `ShellView.xaml`.

Tracked tests (`19`) cover Application grouping/OCR/cache/pipeline/provider behavior, profile validation/storage, Infrastructure providers/Paddle/Tesseract, calibration, zone layout, profile view models and workspace XAML.

Tracked release tooling changes (`3`): `tools/build-track-d-opt-in-release.ps1`, `tools/finalize-track-d-opt-in-release.ps1`, `tools/verify-track-d-opt-in-release.ps1`.

Untracked source/document files (`8`, including this inventory):

- `src/GameTranslator.Application/Translation/TranslationOutputSanitizer.cs`
- `src/GameTranslator.Domain/Profiles/OcrCandidateGroupingSettings.cs`
- `src/GameTranslator.Domain/Profiles/TextCandidateDetectorPreset.cs`
- `src/GameTranslator.Infrastructure/Ocr/BoundedNativeOcrExecutor.cs`
- `src/GameTranslator.Infrastructure/Ocr/PaddleTextDetectionPresetResolver.cs`
- `src/GameTranslator.Infrastructure/Properties/AssemblyInfo.cs`
- `src/GameTranslator.UI/Diagnostics/PortableOcrSmokeRunner.cs`
- `docs/agents/local-unpublished-worktree-status.md`

Ignored/local-only evidence and build outputs include the adaptive-CJK handoff, writing-system smoke workspaces, Chinese detector/PP-OCRv5 benchmark workspace, AppData live reports and r34-r39 candidate directories. They are not deterministic clean-checkout inputs and must remain outside normal staging unless the owner explicitly promotes a specific artifact.

## 6. GitHub sync boundary

The owner authorized a GitHub sync after this inventory was prepared. The publishable change set consists of current source code, architecture/status/smoke documentation, deterministic non-calibration tests, local instruction updates and release tooling.

The following dirty paths remain deliberately outside the GitHub commit:

- all eight modified files under `artifacts/calibration/**` listed above;
- `tests/GameTranslator.Tests/Calibration/GoldenReferenceCalibrationTests.cs`, whose broad historical worktree diff is still explicitly unpromoted under Issue `#35`;
- every ignored `work/**`, `outputs/**`, AppData diagnostic/cache file, portable candidate, model cache, source screenshot, OCR/translation text report and generated archive.

This exclusion is not data loss or a revert. The files remain unchanged in the owner worktree for later explicit review. No diagnostic text, provider response, credential, frame pixel or third-party source image is included in the GitHub sync.

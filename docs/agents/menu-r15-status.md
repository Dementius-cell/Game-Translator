# Menu r15 source status

Updated: 2026-11-18. Scope: ADR-044 development implementation and local verification, not release publication or completed live-game acceptance.

## Current menu association change

Explicit Horizontal/None/watermark-off single-line menus can now enter a separate OCR-confirmed association when bounded padding jitter fails the strict matcher. Entry removes the old translation and retires the OCR generation; fresh independent OCR must confirm the new authority. Fixed anchors, reciprocal uniqueness, source freshness, cache routing and late-result rejection remain required. Auto/Vertical, grouped and expanded-core paths retain their previous rules. See [ADR-044](../adr/README.md#adr-044) and the [implementation and validation record](../design/incremental-menu-regions-plan.md#ocr-подтверждаемое-сопоставление--2026-11-16).

Release build passed without warnings/errors; 1569/1569 tests and maintenance/docs checks passed. A local real-Tesseract replay on synthetic pixels restored confirmation under alternating detector boxes and handled changed text; detector/capture/provider/overlay were simulated. More OCR work completed instead of being cancelled, increasing replay CPU; this does not establish game performance. Real-menu visibility, CPU/queue behaviour and mixed-zone endurance remain open under #59.

The first owner game run exercised 25 associations and confirmed seven with three independent source frames each. Follow-up analysis separated keycap border noise from ordinary-label OCR changes. A narrow projection fix now lets short Latin OCR with edge brackets/bars reach the existing single-enclosed-glyph pixel check; it does not strip raw recognition, match arbitrary words as keys or accept multiple glyphs. Release build and 1588/1588 tests passed. Universal short-line padding was rejected after nine regressions on 144 synthetic controls. The next owner run exercised the fix: 128 top-left keycap OCR results and 104 of 124 top-right results projected no translation, with no published OCR invalidation in either area. Twenty slash-decorated results still passed through. Edge slashes now reach the same single-glyph check; Release build and 1603/1603 tests passed. A further 118-second owner run confirmed that all 29 slash-decorated single-key results were excluded. Decorated existing key names now have a separate contained-rectangle/matching-glyph-count check, tested on synthetic controls and sampled source crops; Release build and 1634/1634 tests passed. The next 123-second owner run verified this named-key path: all 109 decorated escape-key results were excluded and no published OCR invalidation remained in that keyboard-key area. Controller icon/text mixtures and ordinary-label OCR stability remain open. The owner requested a temporary capture-affinity switch for screen recording. It is now available in diagnostics while idle, resets on app restart and preserves OCR self-capture protection with a post-hide source boundary. Intentional capture gaps can appear in recordings; this mode changes timing and cannot establish baseline protected-mode flicker. Third-party recorder inclusion remains to be checked. See the validation record above.

## Current diagnostics and capture

The source includes automatic HDR/WCG capture with FP16-to-BGRA8 OCR tone mapping, ordinary OCR startup/failure diagnostics, and a persistent **Collect logs and diagnostics** master switch. The switch applies immediately to application/OCR logs, automatic crash/recovery and live reports, input recording and optional frame samples. Explicit manual report export remains available when collection is disabled. See the [user guide](../user-guide.md) for defaults, storage limits and cleanup, and [HDR checks](../testing/hdr-capture.md) for capture limitations.

Diagnostic schema 2 adds invalidation reasons and translation state before removal, OCR queue/scheduler samples, ambiguous geometry matching, publication identifiers and bounded WPF presentation events. Capture sample logging records save/limit/error outcomes. Correlate `OverlaySnapshotId` using the [maintenance workflow](../testing/maintenance-and-live-smoke.md); a Rendering callback precedes composition and cannot prove monitor presentation. These changes improve investigation and do not establish a fix for overlay disappearance. Fresh current-build reproduction and live acceptance remain open under #59.

Local development packages may be refreshed before the source commit is created. An embedded Git hash or an older package manifest alone therefore cannot certify the current executable payload; follow the build-provenance checks in the maintenance workflow. Historical validation totals below apply only to their dated runs.

## Background OCR initialization (ADR-037 / #59)

The shell now starts one shared background detector initialization and displays a localized banner above the profile title: real library/model stage, indeterminate spinner and elapsed time, then a ready checkmark and frozen total duration, or failure/retry. Slow startup is not killed by a five-second elapsed limit; frames submitted during preparation return a controlled unavailable result without cancelling or duplicating initialization. Shutdown cancels and joins initialization. Missing CUDA/runtime and process failure remain nonfatal; explicit retry follows failure, with no CPU/provider/legacy fallback. The independent five-second detector request deadline and cancellation drain remain unchanged.

This corrects the adapter's old startup policy, not the cause of slow native loading. ADR-028 had already superseded the five-second cold enablement gate, and ADR-030 retained a readiness barrier. Current package versions match the preserved September manifest. A cold run on the local C: copy reached ready in about 10.3 s, versus 92.9 s on D: in the same sequential experiment; warm starts were approximately 3.4–3.8 s. These measurements are same-host diagnostic evidence, not a general disk benchmark or proof of antivirus causality. The owner subsequently confirmed the cold-start banner at 10.3 s and working translation/overlay on 2026-11-02. This is brief manual acceptance; extended live-game acceptance remains open.

2026-11-02 validation: Release build and self-contained publish passed; regression tests 1,424/1,424; three Python startup-protocol tests passed. The historical two Windows PowerShell fake-worker failures did not reproduce in this run; their timeouts/assertions were not relaxed. Native smoke passed two worker lifecycles with two detector/Tesseract requests each, followed by shutdown, and the packaged Tesseract smoke passed. No-CUDA/driver/crash cases are synthetic fault evidence, not validation on a physical non-NVIDIA host. Reports, TRX files and test binaries remain ignored local outputs, not public fixture dependencies. The current package is an owner-test development output, not release acceptance under #30.

## Included implementation

- Bounded detector requests, cancellation/drain recovery and opt-in numeric timing diagnostics.
- Per-zone capture/detector failure isolation, recovery and visible degraded state.
- Menu-owned OCR/text state without retaining menu frames in the legacy optimization cache.
- Suppression of identical final overlay publications while preserving immediate invalidation and geometry changes.
- Conservative source-pixel recognition margins and description grouping, without changing detector identity or raw OCR coordinates.
- Pixel-evidence filtering of isolated keycaps and star ratings from translation projection; raw OCR remains available locally.
- Fixed-anchor geometry matching with bounded ink/core evidence for small detector padding variations.
- Regression coverage for mixed menu/dialogue zones, late work, cancellation, recovery and scheduler fairness.

Exact normalized text comparison, source provenance, two-second freshness, three outstanding OCR leases and provider/cache boundaries remain unchanged. No screenshot-specific coordinates or replacement dictionary are introduced.

## Current priorities

1. Remaining brief disappearance/reappearance of translated overlays. Known geometry cases were improved offline; full live stability is not established. Fresh owner observations and current-build lifecycle evidence are needed to distinguish detector loss, changing OCR and presentation failures.
2. Unstable recognition of textured descriptions and unresolved decorative-emblem false positives. Rejected preprocessing and topology experiments are not shipped.
3. Native detector cold import remains slow/variable. The new background lifecycle avoids repeated startup timeout kills; it does not establish or fix the system-level loading cause.
4. Memory investigation is deferred by owner decision until overlay stability is addressed. An eight-minute fixed-frame OCR probe completed 21,504 calls without execution errors but showed approximately 2.86 MiB/min late private-memory growth after GC, with stable GC committed memory. This is an offline observation, not a demonstrated unbounded leak or normal-game rate. Resume earlier if actual memory pressure or related failures occur.
5. Real mixed-zone capture/overlay endurance and later legacy cleanup remain open.

## Evidence and delivery limits

The recovered Windows environment passed a Release build with zero warnings/errors and 1,414/1,414 regression tests using process-only PSExecutionPolicyPreference=Bypass for Windows PowerShell fake workers. Both Detector_RequestDeadlineRetiresSilentWorkerAndAllowsRecovery variants passed in that environment; this does not constitute a permanent harness fix. No production timeout or tests were changed.

The owner confirmed the synthetic-source capture/OCR/translation/overlay smoke test, including positioning, blank/restore and stop/start. Session diagnostics corroborate three recognized/translated items, empty overlay snapshots and restoration, and a hidden overlay after stopping. This is local-only evidence for that scenario, not full game/endurance acceptance. That earlier smoke preceded ADR-037; its startup timeout is historical and no longer describes the current background initialization policy.

The current local Release build output contains copied Python/packages, detector model and tessdata, without runtime junctions or old-location dependencies. Preserved runtime versions were not changed or downloaded again. Ordinary build/publish does not assemble the full OCR payload; use the existing [packaging script](../../tools/build-track-d-opt-in-release.ps1). The restored build output is not an approved transferable release. Signing, archive delivery, recovery/offline-host gates and explicit release approval remain tracked separately in issue #30.

Private screenshots, OCR/translation transcripts, runtime models, diagnostic logs, local harness outputs, profiles and binaries are excluded from this source update. Public regression fixtures use synthetic content. GitHub Issues remain the delivery source; this document records the implementation and unresolved limits without publishing owner materials.

## Owner verification — 2026-11-02

The owner confirmed translation and overlay on the current development build. A selective local diagnostic review of two live sessions at 19:33–19:36 Moscow time found 100/199 completed capture/detection cycles, zero session restarts, no final translation failure, 10/25 overlay publications and 3/11 final text elements, followed by normal application shutdown. One translation wait warning reached 5,012 ms (3,435 ms queued); translation completed without a provider failure. These are aggregate observations from a brief manual check, not endurance or release acceptance. Diagnostic logs and their OCR/translation content remain private local-only evidence.


## Temporary capture-affinity control — 2026-11-17

The owner explicitly requested a recording switch after noting that ordinary Windows recordings omit the protected overlay. The implementation is opt-in, session-only, idle-only and separate from log collection. Release build passed with zero warnings/errors; 1639/1639 tests passed, followed by the focused native WPF affinity/hide/restore checks after strengthening that test. Maintenance/docs checks passed. Runtime/model, profiles, default capture and provider/cache configuration were not changed. See the [recording workflow and limitations](../testing/maintenance-and-live-smoke.md#temporary-overlay-recording). Real recorder verification remains open; no claim of unchanged recording-mode cadence or real-game flicker acceptance is made.


The owner identified the cyan top-left message panel during recording. It was populated by positioning DebugMetricLines even with debug overlay disabled; these messages are not runtime exceptions. The floating message panel has been removed from the overlay view. Raw snapshot diagnostics and exported placement warnings remain available; translation, masks and optional OCR debug boxes are unchanged.


## Menu font-fit follow-up — 2026-11-18

The supplied recording confirms capture of translations and removal of the cyan message panel. Global blank intervals are expected in recording mode and do not establish protected-mode flicker. Separate visible truncation led to a font-fit audit: the owner's selected zone used fixed 16 bold text with AutoFontSize disabled. Menu auto sizing also incorrectly reserved expanded-dialogue vertical padding; it now uses the existing menu overflow allowance consistently. Fixed-font rendering, source bounds, masks, other layouts and confirmation authority are unchanged.

Three real-WPF regression cases verify the largest fitting menu size at three geometry scales. Release build passed with zero warnings/errors; 1643/1643 tests passed. Local reconstruction of 88 unique single-block logged cases found 80 overflow warnings with fixed size and 10 with automatic sizing. The correction increased automatic size in 72 cases, reduced none and added no warnings; 11 cases now reach the minimum, versus 44 before. These are measurement results using logged OCR bounds, not exact reconstruction of every grouped/positioned overlay or a readability assessment. Private inputs, text and harness outputs stay ignored under work/video-review-20261117/fit-probe.

The current local test profile is prepared with AutoFontSize enabled after preserving a private backup. No profile defaults or migrations change. Small frames and false icon recognitions remain open. The owner's current acceptance priority is disappearance/reappearance on unchanged text; further font/readability work is deferred. Performance/runtime/provider/release acceptance is not claimed. Application AGENTS was clarified; UI ownership contracts are unchanged.

## Unchanged-text disappearance follow-up — 2026-11-18

The next owner recording and its local capture samples reproduce changing OCR on a visually unchanged menu label over an animated background. Lifecycle evidence separately records published-text contradictions, geometry removal and source expiry; recording-mode hide/restore does not explain all disappearances. Two contradiction-to-next-translation-publication intervals on the same label were about 1.77 s and 1.13 s. These are pipeline intervals, not exact monitor presentation times. No text dictionary, tolerance in text comparison, delayed revocation or freshness extension is introduced.

Tesseract now supplies a recognition-only margin for explicit horizontal, unprocessed single menu lines whose detector member occupies the entire crop and whose foreground contrast reaches an outer edge. The line must be at least three times as wide as tall; input work is bounded to one million pixels. The margin is one quarter of the transverse crop extent and uses the median border luminance. Source pixels/provenance are preserved, and recognized blocks/words are mapped back into the original crop. Already separated text, Auto/Vertical, grouped/context crops, manual/adaptive preprocessing and other content modes retain their paths. This is a bounded missing-margin correction within the existing recognizer; it does not expand masks or detector geometry. Tesseract documents [missing-border recognition limitations](https://github.com/tesseract-ocr/tessdoc/blob/main/ImproveQuality.md#borders).

Validation: zero-warning Release build and 1658/1658 tests, including 15 margin/coordinate/route regression cases. Native offline replay on nine source crops improved exact recognition from 2 to 5; four still fail. On synthetic controls, tight crops improved from 99/144 to 136/144 without regressions, while crops with existing margins remained 142/144. Another 72 Russian/Japanese/Chinese controls improved from 38 to 64 without regressions. All returned geometry stayed within the source; unchanged multilingual text retained identical block bounds. General unconditional padding was rejected because it regressed controls. Native median recognition latency in these sequential local probes did not show a material increase (tight controls 33.9 to 32.5 ms; nine source samples 33.9 to 36.3 ms); this is not a game CPU/RAM or endurance measurement.

Private capture crops, transcripts, recordings, native harnesses and comparisons are ignored local-only outputs under work/video-review-20261118. No new profile/settings/runtime/model changes are required. Infrastructure AGENTS documents the new recognition margin; Application/UI ownership contracts are unchanged. Real-game verification must target disappearance on unchanged labels with recording off; geometry/expiry cases, remaining OCR mistakes and mixed-zone endurance remain open in #59. The correction is partial and does not establish flicker acceptance.

# Menu r15 source status

Updated: 2026-11-02. Scope: source status and local runtime recovery verification, not release publication or completed live-game acceptance.

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

# Menu r15 source status

Updated: 2026-11-01. Scope: source status and local runtime recovery verification, not release publication or completed live-game acceptance.

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
3. Detector startup timeout remains reproducible on some first process starts; later recovery does not prove the startup issue fixed.
4. Memory investigation is deferred by owner decision until overlay stability is addressed. An eight-minute fixed-frame OCR probe completed 21,504 calls without execution errors but showed approximately 2.86 MiB/min late private-memory growth after GC, with stable GC committed memory. This is an offline observation, not a demonstrated unbounded leak or normal-game rate. Resume earlier if actual memory pressure or related failures occur.
5. Real mixed-zone capture/overlay endurance and later legacy cleanup remain open.

## Evidence and delivery limits

The recovered Windows environment passed a Release build with zero warnings/errors and 1,414/1,414 regression tests using process-only PSExecutionPolicyPreference=Bypass for Windows PowerShell fake workers. Both Detector_RequestDeadlineRetiresSilentWorkerAndAllowsRecovery variants passed in that environment; this does not constitute a permanent harness fix. No production timeout or tests were changed.

The owner confirmed the synthetic-source capture/OCR/translation/overlay smoke test, including positioning, blank/restore and stop/start. Session diagnostics corroborate three recognized/translated items, empty overlay snapshots and restoration, and a hidden overlay after stopping. This is local-only evidence for that scenario, not full game/endurance acceptance. Cold Paddle startup timeout remains reproducible; manual stop/start recovered the tested session.

The current local Release build output contains copied Python/packages, detector model and tessdata, without runtime junctions or old-location dependencies. Preserved runtime versions were not changed or downloaded again. Ordinary build/publish does not assemble the full OCR payload; use the existing [packaging script](../../tools/build-track-d-opt-in-release.ps1). The restored build output is not an approved transferable release. Signing, archive delivery, recovery/offline-host gates and explicit release approval remain tracked separately in issue #30.

Private screenshots, OCR/translation transcripts, runtime models, diagnostic logs, local harness outputs, profiles and binaries are excluded from this source update. Public regression fixtures use synthetic content. GitHub Issues remain the delivery source; this document records the implementation and unresolved limits without publishing owner materials.

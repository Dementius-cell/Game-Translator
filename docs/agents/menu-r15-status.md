# Menu r15 source status

Updated: 2026-10-29. Scope: owner-authorized source synchronization, not release publication or completed live acceptance.

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

The previous local source gate passed 1,414 tests. Native replay and package checks are local-only evidence, not visual acceptance or a clean-host release gate. The local test application is an unpacked r15 directory with shared runtime junctions; it is not an independent transferable release package. Signing, archive delivery, recovery/offline-host gates and explicit release approval remain tracked separately in issue #30.

Private screenshots, OCR/translation transcripts, runtime models, diagnostic logs, local harness outputs, profiles and binaries are excluded from this source update. Public regression fixtures use synthetic content. GitHub Issues remain the delivery source; this document records the implementation and unresolved limits without publishing owner materials.

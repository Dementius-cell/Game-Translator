# GameTranslator.Application AGENTS.md

These instructions apply to `src/GameTranslator.Application/**`.

## Purpose

Keep application use cases and contracts independent from concrete UI, platform, persistence, OCR, and translation implementations.

## Ownership

- Application services and orchestration.
- Ports and abstractions for capture, OCR, translation, cache, credentials, settings, profiles, hotkeys, debug, and updates.
- Translation pipeline behavior, timings, grouping, multi-zone handling, cancellation, and recoverable failures.
- Opt-in watermark candidate selection and bounded per-session/per-zone page evidence and bounded source-frame and neighboring OCR-line context for fragmented emblems; preserve raw OCR and source/revision authority, exclude only translation/overlay projection.
- Profile migrations and import/export orchestration.

## Local Contracts

- Application may depend on Domain and lightweight DI abstractions already accepted by the project.
- Do not add WPF, Windows API, SQLite, Tesseract, network SDK implementation, Credential Manager, file dialog, or UI framework dependencies here.
- Cache lookup remains part of normal translation flow before provider calls; the default TTL policy stays 30 days unless a Decision Record changes it.
- Keep OCR contracts engine-neutral. Windows OCR and Tesseract remain mandatory product capabilities implemented outside this layer.
- Keep translator contracts provider-neutral. Credentialed Google, Azure, and Yandex remain the supported provider set unless a Decision Record changes it.
- Resolve combined Tesseract model lists by their component writing systems. Bound horizontal candidate stacks by original detector-member heights (maximum 2:1) so large titles cannot absorb small captions through intermediate lines.
- Preserve raw OCR block geometry for diagnostics and masking; semantic grouping may create translation groups without destroying source geometry. Candidate requests may carry immutable crop-relative detector line hints; preprocessing must scale hints with pixels, and line coverage remains bounded numeric evidence rather than proof of OCR accuracy.
- With automatic translation source enabled, preserve compact aligned dialogue lines across script changes; a script change alone is not a translation boundary. Preserve detector-region identity in projected crop sources and never merge different detector regions. Spatially independent fragments can remain separate. Han/Latin remain language-ambiguous; explicit Japanese/Korean hints require substantial kana/Hangul. Split inside a line only with complete, confident word geometry from one recognition pass. Preserve raw masks and reading order; route/cache by each group's effective source language, then restore output order. Manual-source profiles retain their grouping behavior.

## Work Guidance

- Capture frames may carry immutable `Origin` metadata (source instance, compositor timestamp and actual source time) and `IsCachedReuse`. Preserve both through crop, scale, padding and recognition preparation. Legacy `CapturedAt` remains the delivery timestamp for compatible routes; it is not proof of a new source frame. Missing origin means unknown provenance, not an independently confirmed observation. These metadata alone do not establish capture liveness for a static source.

- `IRecentCaptureFrameSource` is an optional explicit freshness port, exposed by `CaptureService.CaptureRecentAsync`. Unsupported sources fail rather than silently use ordinary capture. Identity uses source ID/compositor timestamp; age uses `FreshnessTimestamp`, bounded by actual monotonic arrival when the compositor timestamp leads arrival. Cached delivery never refreshes either. Live StaticMenu candidates explicitly use this port; other capture routes remain ordinary.

- Live candidate capture failures are isolated to the zone whose capture failed. Continue capturing/reconciling the other zones; retain their OCR/text/translation authority. Remove the failed zone's candidates and pending work, emit CaptureFailed with its zone ID, and require fresh confirmation after recovery. When restoring a capture-hidden overlay, rebuild it from surviving states if any zone failed. Cancellation/disposal is not a recoverable zone failure: late capture success or failure must not restore an old session's overlay. Legacy full-page capture still propagates its failures.

- `MenuOcrScheduler<T>` runs live-menu OCR under the live-session authority. Supply monotonic times in the same domain as frame origins and complete each lease only after OCR actually terminates. Removed/cancelled generations retain their global slot until completion; pixel changes only replace the latest pending work. `MenuTextAuthority` separately checks source freshness, exact projected text confirmation and translation version. Keep the existing comic route independent.

- `MenuGeometryMatcher` requires reciprocal uniqueness across the complete compatibility matrix, including exact bounds matches. Preserve fixed discovery anchors and member checks; never resolve ambiguity greedily by nearest distance or iteration order. Fresh same-version menu OCR renews authority without republishing identical presentation. Compare text/style, masks, debug content and placement constraints; ignore only the snapshot timestamp, and still publish confirmed geometry changes.

- ADR-036 StaticMenu Auto conservatively groups horizontal descriptions while keeping short elements and columns separate; explicit per-zone limits retain writing-system grouping. Preserve raw source geometry and never use owner fixture text as production rules. Menu numeric displays and complete standalone control hints are excluded only from translation projection; letters inside prose remain text. Explicit recognized key tokens may separate short action captions with complete word evidence, never wrapped prose. Keep source-bounded menu overlay, short CJK labels, output number/key integrity checks on cache hits, and mixed-mode independence. Do not add whole-menu context by concatenating independent labels or change cache/provider semantics.

- Model external effects as interfaces here and implement them in Infrastructure or UI services.
- Prefer explicit request/result types over loosely shaped dictionaries or stringly typed contracts.
- Keep async operations cancellable where the surrounding service already supports cancellation.
- Use deterministic clocks, options, and fakes in tests when cache, timing, expiration, or retry behavior is involved.

## Verification

- For application behavior changes, run focused Application tests and the architecture dependency tests.
- For pipeline, cache, OCR contract, translation contract, profile migration, or multi-zone changes, run the full test suite unless there is a documented reason not to.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

ADR-033 expanded overlay layout preserves source height where unobstructed and optionally fits font size through IOverlayTextMeasurer; the bounded in-memory font-fit cache does not replace translation caching.

Explicit user cache clearing removes all memory and persistent translations with translation stopped; ordinary expiry and the 30-day TTL remain unchanged. Translation and diagnostic reading order honor resolved OCR source orientation before request-level Auto, independently for projected detector regions.

Opt-in OCR preparation applies inversion before Sauvola (31 px window, k=0.2, R=128); Sauvola replaces the fixed threshold and is capped at four million source pixels with cancellation. OcrService adds 10 px white borders after scaling, shifts detector hints and removes padding from blocks, members, words and candidate bounds before returning the original scaled coordinate system. Preserve pass IDs, orientation, confidence and zone metadata. Existing profiles perform the original single OCR pass; no recognizer/provider fallback is introduced.

Three-way preparation selects effective settings before any filtering. Adaptive analyzes original pixels with bounded sampling; it constructs fresh neutral manual settings and never inherits stored filters. It uses crop hints for scale/padding and conservative luminance/color heuristics for inversion/Sauvola. No new OCR retries. Returned OCR requests carry effective settings for diagnostics.

For a single horizontal StaticMenu crop, Adaptive may isolate sparse near-white glyph cores on a colored high-contrast background using inversion and a fixed luminance threshold. Selection uses pixel proportions and aspect ratio, never fixture text or screen coordinates. None/Manual and comic preparation retain their existing behavior; the selected branch still performs one recognition pass.

StaticMenu metric rows may project independent labels only with complete single-pass word geometry, reliable confidence, repeated numeric values and relative horizontal spacing. Preserve raw OCR, keep numeric values outside label masks, and abstain for prose, item quantities, incomplete word evidence or wrapped text. This projection cannot recover detector omissions.

Horizontal StaticMenu candidate selection preserves small edge overlaps between aligned neighboring wide boxes using relative height/width limits. Substantial, nested and vertically shifted intersections remain suppressed; preserve original detector geometry and other content modes. Do not confuse post-selection omissions with missing raw detector candidates.

Partial metric crops may use transient relative detector-row evidence (two wide neighbors and a compact neighbor) together with complete confident word geometry and wider numeric spacing. Preserve independent detector identities and progressive OCR; never wait for or concatenate neighboring OCR. Carry this hint through preprocessing and padding; it is not semantic proof.

StaticMenu live candidates use explicit recent capture (500 ms budget), one active OCR and one latest pending crop per area, and at most three outstanding OCR leases per session. Pixel changes do not cancel OCR or reset text history. After 2 s and three distinct changed frames the periodic path persists; launches are at least 500 ms apart. Static areas also receive fresh checks. Accept exact NFC/whitespace-normalized projected groups preserving case, numbers, punctuation and group boundaries; require two distinct source frames and 200 ms, preserving stricter selected text/grouping settings. Stale observations and published text expire after 2 s of source age. Different/empty OCR revokes the old translation immediately; late results require the current generation/text version. Relative reciprocal-unique matching uses fixed discovery anchors (IoU >= .85, edge drift <= .15 of the smaller height), including detector members. Disappearance, ambiguity, context changes and capture loss clear without dialogue grace. One-shot and comic behavior retain their own paths. Menu diagnostics carry source identity/age/reuse, generation/version, decisions and aggregated queue counters; never infer real-game success from synthetic tests alone.

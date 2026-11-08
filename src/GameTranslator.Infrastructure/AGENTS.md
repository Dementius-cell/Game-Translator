# GameTranslator.Infrastructure AGENTS.md

These instructions apply to `src/GameTranslator.Infrastructure/**`.

## Purpose

Keep concrete external integrations isolated behind Application contracts.

## Ownership

- Windows Graphics Capture and Direct3D capture helpers.
- Windows OCR, Tesseract OCR, and OCR language pack adapters.
- SQLite translation cache persistence.
- JSON profile and settings persistence.
- Windows Credential Manager secret storage.
- Google, Azure, Yandex, diagnostic web translation providers, and update providers.

## Local Contracts

- Infrastructure may depend on Application and Domain, but must not depend on UI.
- For Auto Japanese/Chinese (optionally with English), bounded consistent detector-line geometry may resolve writing direction before native layout analysis. Judge individual members, not the enclosing group. Ambiguous/mixed geometry, other language sets, explicit directions, legacy requests without hints and explicit Comic segmentation retain existing behavior. Only an empty, large, single vertical column resolved this way receives one half-size retry; map blocks and words back to original crop coordinates. Do not globally tune Tesseract textord thresholds or add another recognizer.
- Implement Application interfaces without changing their public semantics unless the matching Application contract is intentionally changed and tested.
- Do not remove Windows OCR or Tesseract support, and do not make the product depend on only one OCR engine. Korean horizontal detector-line recognition is limited to the measured eligible geometry; other crops retain the existing recognition path. Preserve raw OCR coordinates and report expected/recognized line counts without introducing automatic recognizer or provider fallback.
- Outlined-color recovery stays within horizontal detector-member crops and the existing Tesseract engine. It requires detached interior glyphs, a thin enclosing component, bounded pixel work and high improved recognition confidence for every member; partial recovery cannot replace a group. Map resized recognition geometry back to each original member. Ordinary, vertical and legacy requests retain their existing path.
- Treat a combined Tesseract language selection as a complete required set. Resolve each model with the requested orientation, report missing packs and fail recognition rather than silently dropping unavailable languages. Keep readiness checks, installation and native recognition consistent; unknown model codes must not become arbitrary download paths.
- Keep on-demand language installation separate from the locked release runtime. Installing more catalog models in the UI does not change the package lock or make every model a release requirement. Preserve cancellation, per-package failure reporting and existing installed data; test missing/partial selections and combined-model readiness.
- Store secrets only through Windows Credential Manager or an approved protected fallback; redact provider credentials from logs, exceptions, diagnostics, and artifacts.
- Credentialless web translator providers are separately selected diagnostic/experimental paths and must not become silent production defaults or automatic fallback chains.
- Do not add process injection, hooks, game memory access, drivers, anti-cheat bypass, or reverse engineering.

## Work Guidance

- Capture reads the active color mode of the captured monitor through DisplayConfig, including the current Windows SDR white level. Poll at most once per second under the capture gate; a mode/white-level change retires cached frames and the old pool generation before accepting new pixels. SDR retains BGRA8. HDR/WCG/unknown mode use FP16 scRGB WGC surfaces, native staging readback and a fixed SDR-white-normalized shoulder plus sRGB encoding for opaque BGRA OCR input. Unknown mode is explicit, never silently labeled SDR; unavailable white level uses the 80-nit scRGB reference and is marked unknown. Do not change Windows HDR settings or adapt exposure from frame content. Preserve source origin/reuse and failure/cancellation semantics. The immediate D3D context is multithread-protected and HDR reads are serialized per source.

- WGC snapshot origins are assigned from the actual compositor frame before copying pixels. Cache delivery retains that origin and marks reuse; delivery time remains separate for existing consumers. OCR image transformations preserve origin/reuse metadata. A cached delivery may be the first consumer observation of a source frame that finished copying after the wait timed out; consumers must compare source identity/timestamp rather than assume the reuse flag alone proves a duplicate or capture loss.

- An explicit recent-frame request validates age on QPC time. If necessary it may recreate the same WGC session once, under the shared capture gate, at most once per 500 ms; it must fail on timeout, unknown/old provenance or closure instead of returning stale cache as success. Ordinary capture does not automatically restart. Register the waiter before starting capture, retain source identity across verification, reject callbacks from retired generations, clear cache on item closure and preserve caller cancellation. The actual monotonic arrival bounds a slightly leading compositor timestamp without extending the freshness budget.

- Keep platform-specific exception handling close to the adapter and return recoverable Application-level failures where the contract supports it.
- Keep file paths, SQLite schema use, Tesseract data paths, and Windows language-pack behavior observable through tests or diagnostics without exposing secrets.
- Prefer adapter-level tests with fakes or temporary local storage over live network/API calls.

## Verification

- While the shared diagnostics master switch is enabled, OCR startup/failure summaries are retained in a 64-entry runtime history and the ordinary application log through IApplicationLogger, independently of the optional timing marker. Log lifecycle changes and actual failed requests, not every unavailable poll or successful frame. Keep the last failure after retry; record native exit state/code before our termination, never label a forced-kill code as a native crash. Only allowlisted worker error codes and startup stages cross into diagnostics; exception type/HResult/native Win32 error may be recorded, but messages, stderr, paths and image/OCR data may not. Diagnostic sink failure must not affect OCR readiness, deadlines or cancellation. Worker model/library/CUDA/memory/input error categories are evidence, not justification for automatic fallback or deadline extensions.

- A detector exchange has an independent finite request deadline (5 s by default), including pipe write/read. Caller cancellation may shorten its remaining lifetime with the existing 2 s drain window but cannot extend the original request deadline. Timeout retires that worker and returns an observable unavailable result. ADR-037 production initialization is single-flight and background-owned, with no elapsed-time cutoff; pending frames do not start or cancel it. Failed initialization or retired runtime is latched until explicit retry. App shutdown cancels initialization and awaits cleanup through IDetectorRuntime.StopAsync. Explicit diagnostic startup deadlines remain supported separately. No automatic full-frame/provider fallback is introduced.

- For infrastructure changes, run focused Infrastructure tests plus architecture dependency tests.
- For cache persistence, profile JSON, credentials, OCR engines, capture, or provider changes, run the relevant smoke/application tests that exercise the adapter through its Application contract.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

For Auto Japanese crops already resolved as vertical, recover up to four wholly missing long detector columns with the same engine only when 2–8 bounded vertical hints agree and the crop is at most one million pixels. Require native mean confidence >= 0.8; retain existing blocks, map recovered blocks/words back and order columns right to left. Do not retry short interjections, partially recognized columns, explicit directions or other language sets.

ADR-036: single horizontal StaticMenu detector crops use SingleLine segmentation in Tesseract. Wrapped descriptions retain SingleBlock; vertical and DialogComic paths are unchanged. Do not infer or repair unknown key glyphs from expected fixture text.

Capture diagnostics follow the shared live settings and active session; the former marker supplies the initial preference. Save sampled delivered capture-region PNGs and origin/hash metadata under local Diagnostics/Frames; at most one encoder, 1 sample/sec, 1800 samples and a configured byte budget up to 2 GiB per source. Never block capture on encoding, change pixels or upload frames; disk/encoding errors stop sampling only. PNGs record the delivered 8-bit BGRA OCR input, including HDR-to-SDR conversion where applied, not raw HDR scene data. Metadata records actual pool format, detected active display mode, detection method, SDR white level/availability and tone-mapping method.

Detector stage diagnostics require both the shared diagnostics master switch and opt-in via detector-timing.enabled beside the executable. Disabled logging creates no JSONL file; queued entries from a previous collection generation are discarded. The adapter owns a bounded nonblocking queue (256 entries), a background JSONL writer (8 MiB per instance), and refuses a new file when Diagnostics/Detector already contains 32 detector files. Keep request IDs, worker generations, source metadata, fixed outcome codes and numeric timing fields only; never serialize worker stderr/error text, paths, pixels, OCR or provider content. Saturation, storage failure and shutdown may lose diagnostic entries but must not block detection or change cancellation, timeout, thresholds or freshness semantics. Worker timings travel in the existing response and are not a second file writer.

With --timings or --progress the worker emits startup-progress before imports and predictor setup. Application-owned initialization uses --progress even without diagnostic logging; only seven allowlisted codes map to typed library/model readiness stages. Monotonic elapsed time starts at initialization scheduling and freezes at completion. The adapter records diagnostic stage codes once per generation only when logging is enabled. Progress is not readiness and never resets an explicit diagnostic startup deadline; unknown stage text is not logged. Startup failure emits only a fixed startup-error code; missing/unusable CUDA is distinguished from generic initialization failure, without exposing exception text to UI.

Caller cancellation returns immediately while an already-sent detector exchange retains exclusive ownership of its pipe and temporary BMP for at most two more seconds. Drain and discard that exact response; reuse the worker only after an ok response with the matching request ID. Waiting callers cannot read or delete that input. Drain timeout, bad/missing response ID, worker failure or disposal retires the worker. Background startup belongs to the application lifetime, not a frame cancellation. Never publish a cancelled result or start a second reader on the same pipe. Disposal cancels drain and serializes process cleanup under the worker gate.

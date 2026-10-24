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

- WGC snapshot origins are assigned from the actual compositor frame before copying pixels. Cache delivery retains that origin and marks reuse; delivery time remains separate for existing consumers. OCR image transformations preserve origin/reuse metadata. A cached delivery may be the first consumer observation of a source frame that finished copying after the wait timed out; consumers must compare source identity/timestamp rather than assume the reuse flag alone proves a duplicate or capture loss.

- An explicit recent-frame request validates age on QPC time. If necessary it may recreate the same WGC session once, under the shared capture gate, at most once per 500 ms; it must fail on timeout, unknown/old provenance or closure instead of returning stale cache as success. Ordinary capture does not automatically restart. Register the waiter before starting capture, retain source identity across verification, reject callbacks from retired generations, clear cache on item closure and preserve caller cancellation. The actual monotonic arrival bounds a slightly leading compositor timestamp without extending the freshness budget.

- Keep platform-specific exception handling close to the adapter and return recoverable Application-level failures where the contract supports it.
- Keep file paths, SQLite schema use, Tesseract data paths, and Windows language-pack behavior observable through tests or diagnostics without exposing secrets.
- Prefer adapter-level tests with fakes or temporary local storage over live network/API calls.

## Verification

- For infrastructure changes, run focused Infrastructure tests plus architecture dependency tests.
- For cache persistence, profile JSON, credentials, OCR engines, capture, or provider changes, run the relevant smoke/application tests that exercise the adapter through its Application contract.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory.

For Auto Japanese crops already resolved as vertical, recover up to four wholly missing long detector columns with the same engine only when 2–8 bounded vertical hints agree and the crop is at most one million pixels. Require native mean confidence >= 0.8; retain existing blocks, map recovered blocks/words back and order columns right to left. Do not retry short interjections, partially recognized columns, explicit directions or other language sets.

ADR-036: single horizontal StaticMenu detector crops use SingleLine segmentation in Tesseract. Wrapped descriptions retain SingleBlock; vertical and DialogComic paths are unchanged. Do not infer or repair unknown key glyphs from expected fixture text.

Explicit capture diagnostics are enabled only by capture-frames.enabled beside the executable. Save sampled delivered capture-region PNGs and origin/hash metadata under local Diagnostics/Frames; at most one encoder, 1 sample/sec, 1800 samples and 2 GiB per source. Never block capture on encoding, change pixels or upload frames; disk/encoding errors stop sampling only. This records the existing 8-bit BGRA input, not HDR scene data.

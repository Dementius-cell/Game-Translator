# Portable r51 baseline and watermark handoff

Owner-approved delivery, 2026-09-13. Watermark filtering is not implemented in this baseline.

## Baseline behavior

- Bing has a dedicated singleton HTTP client with a 5-second connection timeout, one shared bootstrap GET retry on transport timeout, a 15-second per-attempt timeout, and the existing 30-second Application budget including queueing. Two consecutive timeouts trigger the existing cooldown; translation POSTs are not automatically replayed inside the adapter.
- Pending translation diagnostics distinguish queue, bootstrap and response waits; completed OCR is recorded independently. Synthetic timeout/429/CAPTCHA evidence is explicitly labeled and is not real service acceptance.
- Transient network failures can recover on unchanged source through bounded retries. User verification requires intervention. No provider or recognizer fallback.
- One-pixel changes to internal detector-line edges preserve the overlay only for unchanged crop pixels, unchanged outer bounds and equal member count. Larger/internal count/source changes still invalidate normally.
- Prior Hebrew/Arabic mixed RTL, numbers and Latin grouping and Japanese normalization remain included. ADR-032 empty-OCR watchdog remains unchanged (5/10/20 seconds, candidate-local).
- Issue #35: nine generated calibration outputs cease being tracked but remain locally preserved; active calibration source and required promoted fixtures remain.

## Verification and delivery boundary

Release build: zero warnings/errors. Automated checks: 729 non-calibration and 16 isolated calibration tests passed (745 total). Four new tests cover bootstrap recovery, bounded repeated failure/cooldown, no POST replay, and actual SocketsHttpHandler timeout exception semantics with a fake connection callback (no network). The existing queue-budget test now waits for both terminal candidate results, rather than treating an empty pending-diagnostics list as work completion.

Local-only build target: work/release-hardening/release-candidates/v0.1.0-pre.20260913-bing-connect-r51. Package verification results are recorded beside the build; this source document does not assert package completion. Manual overlay/panel and real Bing availability acceptance remain owner checks. Official API credentials are unavailable (#57); #34 capture-loss evidence remains open. No public release, archive, signature or clean-host claim. Hotkeys, profile schema and update transport are unchanged; their new manual gates are N/A. Existing architecture/cache/multizone/RTL regressions are included in the full suite.

## Next task: watermark filtering

Implement four combined signals: local contrast, logo-like composition, repeated visual pattern across genuinely different pages, and website/address text with bounded OCR-error tolerance. Repeated frames of the same static page are not independent evidence. None of low contrast, digits, a URL or corner position alone may reject a block.

OCR settings: Off / Cautious / Strong; default Off until validated. Call it filter sensitivity, not measured transparency. A flattened capture does not supply the watermark alpha matte. Preserve uncertain text in Cautious mode and record bounded per-signal reasons for exclusions. Never reconstruct/remove image pixels: this feature excludes OCR/translation candidates only. Keep raw OCR geometry and multi-zone/source authority. Define profile compatibility and regression tests before adding persisted settings.

Owner's confirmed example has a compact colored logo on a gray square in the bottom-left corner, separate from a large three-line Korean speech bubble. Both were accepted as separate candidates and translated; the problem is false-positive content selection, not provider failure or grouping with the bubble. Keep the original image and text-bearing logs local-only. Regression must reject the logo while keeping the bubble, faint/small text, numbers, colored subtitles and mixed scripts. No fixed screen-coordinate mask or site-specific blacklist as the general solution.

Research: https://research.google/blog/making-visible-watermarks-more-effective/ and https://watermark-cvpr17.github.io/ show repeated semi-transparent patterns and logo structures; they do not validate this application's proposed filter. Watermark opacity/gradient alone cannot distinguish ordinary faint text.

GitHub open tasks at inspection: #30 local/package publication work, #34 web diagnostics, #57 contributor official-API validation backlog. #35 and #48 are closed. Read current root/scoped AGENTS and accepted ADRs before continuing. Do not create another task automatically: owner will choose the new chat.

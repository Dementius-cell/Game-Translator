# Portable r51 baseline and watermark handoff

## Current delivery status — 2026-09-22

This source update publishes the owner-authorized r51–r61 implementation: watermark filtering, grouping, Bing session/connection compatibility, DPI-correct overlays, ADR-033 source height/auto font size, whole-word wrapping and render containment. Release verification passed 835 tests with zero build warnings/errors. Bing and strong watermark behavior have positive owner evidence; final r61 overlay acceptance remains pending.

The r61 portable, runtime/model files, diagnostic logs, private OCR text and generated visual evidence remain local-only. The unrelated reinstall handoff remains untracked. The 2026-09-13 calibration disposition remains active: retained source/promoted fixtures are tracked; nine generated outputs remain ignored. Earlier dated status blocks below are historical and do not describe current publication or retained portable versions.


Owner-approved delivery, 2026-09-13. Watermark filtering is not implemented in this baseline.

## Baseline behavior

- Bing has a dedicated singleton HTTP client with a 5-second connection timeout with staggered TCP attempts across the endpoint DNS addresses (200 ms spacing; losing sockets cancelled and disposed), one shared bootstrap GET retry on transport timeout, a 15-second per-attempt timeout, and the existing 30-second Application budget including queueing. Two consecutive timeouts trigger the existing cooldown; translation POSTs are not automatically replayed inside the adapter.
- Pending translation diagnostics distinguish queue, bootstrap and response waits; completed OCR is recorded independently. Synthetic timeout/429/CAPTCHA evidence is explicitly labeled and is not real service acceptance.
- Transient network failures can recover on unchanged source through bounded retries. User verification requires intervention. No provider or recognizer fallback.
- One-pixel changes to internal detector-line edges preserve the overlay only for unchanged crop pixels, unchanged outer bounds and equal member count. Larger/internal count/source changes still invalidate normally.
- Prior Hebrew/Arabic mixed RTL, numbers and Latin grouping and Japanese normalization remain included. ADR-032 empty-OCR watchdog remains unchanged (5/10/20 seconds, candidate-local).
- Issue #35: nine generated calibration outputs cease being tracked but remain locally preserved; active calibration source and required promoted fixtures remain.

## Verification and delivery boundary

Release build: zero warnings/errors. Automated checks: 729 non-calibration and 16 isolated calibration tests passed (745 total). Four new tests cover bootstrap recovery, bounded repeated failure/cooldown, no POST replay, and actual SocketsHttpHandler timeout exception semantics with a fake connection callback (no network). The existing queue-budget test now waits for both terminal candidate results, rather than treating an empty pending-diagnostics list as work completion.

Local-only build target: work/release-hardening/release-candidates/v0.1.0-pre.20260913-bing-connect-r51. Package verification results are recorded beside the build; this source document does not assert package completion. Manual overlay/panel and real Bing availability acceptance remain owner checks. Official API credentials are unavailable (#57); #34 capture-loss evidence remains open. No public release, archive, signature or clean-host claim. Hotkeys, profile schema and update transport are unchanged; their new manual gates are N/A. Existing architecture/cache/multizone/RTL regressions are included in the full suite.

## Watermark implementation follow-up

The checkout now implements the approved opt-in filter; r51 remains the unchanged pre-filter package. See the watermark sections in the [architecture](../02-architecture.md) and [user guide](../user-guide.md). The original image and text-bearing logs remain local-only. The owner requested a new portable after the source implementation. r52 is a local-only build output; its owner logs showed isolated emblem rows were retained because crop-only structure was absent. The follow-up adds bounded frame context without per-emblem training, and invalidates stale decisions when that context changes. The supplied two pages were checked locally with the packaged detector and production grouping; private images and detector coordinates are not tracked or included in the portable.

## Approved scope: watermark filtering

Implement four combined signals: local contrast, logo-like composition, repeated visual pattern across genuinely different pages, and website/address text with bounded OCR-error tolerance. Repeated frames of the same static page are not independent evidence. None of low contrast, digits, a URL or corner position alone may reject a block.

OCR settings: Off / Cautious / Strong; default Off until validated. Call it filter sensitivity, not measured transparency. A flattened capture does not supply the watermark alpha matte. Preserve uncertain text in Cautious mode and record bounded per-signal reasons for exclusions. Never reconstruct/remove image pixels: this feature excludes OCR/translation candidates only. Keep raw OCR geometry and multi-zone/source authority. Define profile compatibility and regression tests before adding persisted settings.

Owner's confirmed example has a compact colored logo on a gray square in the bottom-left corner, separate from a large three-line Korean speech bubble. Both were accepted as separate candidates and translated; the problem is false-positive content selection, not provider failure or grouping with the bubble. Keep the original image and text-bearing logs local-only. Regression must reject the logo while keeping the bubble, faint/small text, numbers, colored subtitles and mixed scripts. No fixed screen-coordinate mask or site-specific blacklist as the general solution.

Research: https://research.google/blog/making-visible-watermarks-more-effective/ and https://watermark-cvpr17.github.io/ show repeated semi-transparent patterns and logo structures; they do not validate this application's proposed filter. Watermark opacity/gradient alone cannot distinguish ordinary faint text.

GitHub open tasks at inspection: #30 local/package publication work, #34 web diagnostics, #57 contributor official-API validation backlog. #35 and #48 are closed. Read current root/scoped AGENTS and accepted ADRs before continuing. Do not create another task automatically: owner will choose the new chat.

Bing session compatibility update (r58): use the page-issued IG/IID and token expiry interval instead of generating IG, fixing IID, or assuming a one-hour lifetime. POST carries the translator-page Referer. Requests prefer HTTP/2 and permit HTTP/1.1 negotiation. Existing connection budgets, cancellation, shared bootstrap and no automatic POST replay remain in force. These changes follow the classic ttranslatev3 path in [plainheart/bing-translate-api](https://github.com/plainheart/bing-translate-api/blob/master/src/index.js); MET/EPT paths and upstream POST retries are not adopted. This is a protocol compatibility change, not proof of permanent Bing availability.

## 2026-09-21 — r59 overlay DPI correction

Owner reports Bing r58 generally working. New regression: changing the primary display from 100% to 125% makes translated text exceed its overlay. Confirmed mismatch: OverlayWindow converted device-pixel bounds to DIP but kept the measured font size and horizontal padding unconverted. r59 converts font, padding, preview minimum sizes together, and refreshes the retained snapshot on WPF DpiChanged notification. Source placement and mask geometry remain unchanged; primary-monitor scope remains unchanged. No profile/schema/provider/runtime dependency changes.

Normal delivery under the owner's direct request. Changed production file: src/GameTranslator.UI/Views/OverlayWindow.xaml.cs; new headless regression: tests/GameTranslator.Tests/Smoke/OverlayDpiTests.cs. Before correction the 100% test passed and 125/150/200% failed (24px font became 30/36/48px). After correction all four pass, including actual XAML template measurement. Release build: zero warnings/errors; 800 main tests and 16 isolated calibration tests passed. Local-only generated evidence and owner instructions: workspace outputs/r59-dpi and outputs/ПРОВЕРКА-R59.md; package is a local build output, not published. Physical owner-monitor verification remains pending. Existing visible split report still requires overlay screenshot.

Applicable gates: build, tests, architecture, overlay geometry/template, multi-zone regressions, mask regressions and package integrity. Provider/network, cache, profile migration, hotkeys, updater and OCR policy gates are N/A for this UI-only correction; previous fixes remain in the package. No new logging or secret path. Scoped AGENTS contracts unchanged.
Performance: local-only headless projection benchmark (7 x 10000 items, reflection included): r58 median 0.00018321 ms/item; r59 0.00018659 ms/item (+1.85%); allocated bytes/item 88.02 to 96.02; measured CPU 15.625 ms both. This measures projection, not end-to-end live rendering. Generated report: workspace outputs/r59-performance.json.

## 2026-09-21 — r60 source height and auto font

Owner approved ADR-033. Expanded frames retain source text height when unobstructed; crowded frames stay compact, and extra legacy multiline Y displacement is removed for expanded mode. Optional AutoFontSize persists per zone with false default for old schema-1 profiles; selection is largest fitting whole size 8–72 in the final frame. Existing measurement/DPI responsibilities and mask bounds remain intact. Cache of auto-fit results is memory-only, bounded to 256, keyed by text/style/dimensions, without a lock around WPF dispatch.

Validation: Release build zero warnings/errors; 813 main + 16 isolated calibration tests pass. Actual WPF templates fixed/auto tested at 100/125/150/200 percent. Synthetic 25-region warm layout: r59 4.648 ms, r60 fixed 4.611 ms, auto 4.739 ms. Uncached auto adds measurement cost (22.690 ms in the same workload); progressive one-region publication and the bounded cache reduce repeated work. Fixed allocation 431432 to 495880 bytes per 25-region snapshot (+15%, about 2.6 KB/region) reflects obstacle checks for the new height policy; CPU time similar. This is local layout evidence, not a whole-app performance claim.

Generated local-only reports: workspace outputs/r60-performance.json, r60-dpi, r60-verification.json and ПРОВЕРКА-R60.md. New portable is a local build output; user-monitor acceptance remains pending. Relevant gates cover build, tests, architecture, profile compatibility/import-export, layout/collision, mask, DPI, measured performance, packaging. Network, OCR policy, updater and hotkey registration are unchanged and N/A for feature-specific revalidation; packaged OCR runtime is smoke-tested. No source or binaries published.

## 2026-09-22 — r61 whole-word wrapping and render containment

Owner reports occasional bottom clipping and one/two-letter word fragments in r60 auto sizing. WPF Wrap allowed emergency intra-word breaks. r61 aligns TextFormatter and TextBlock on WrapWithOverflow, reports actual line width/overflow, and includes word overflow in fixed-mode expansion/reduction as well as automatic fitting. No syllabic dictionary/hyphenation engine is introduced. Expanded rendering adds Uniform/DownOnly containment with small vertical insets to guard actual glyph layout against a too-short frame; it never scales text upward.

Read-only audit of 33 final snapshot items from 2026-09-22 logs did not reproduce the exact occasional bottom clip. A synthetic undersized frame reproduces out-of-bounds text in r60 at all 100/125/150/200 percent scales; the new template contains it. Long-word measurement also fails the new regression on r60. Five of six new tests fail on r60; all pass after correction. 819 main + 16 isolated calibration tests pass, Release build has zero warnings/errors. Real WPF template images inspected headlessly. 25-region warmed layout: fixed 4.805 ms, auto 4.747 ms; no meaningful slowdown relative to r60 in this local workload.

Changed production paths: Application/Overlay/OverlayPositioningService.cs, UI/Services/WpfOverlayTextMeasurer.cs, UI/Views/OverlayWindow.xaml. Relevant tests include WpfTextFitTests, OverlayDpiTests and template source assertions. Existing Domain/schema, translator/cache policy, OCR/runtime, hotkeys and updater remain unchanged. Gates: build/tests, layout and mask regressions, multi-zone/collision regressions, DPI, measurement, documentation, package runtime/integrity. Physical owner acceptance remains pending. Generated evidence and instructions are local-only workspace outputs/r61-* and ПРОВЕРКА-R61.md; private OCR text is not packaged or published.

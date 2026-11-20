# Menu r15 source status

Updated: 2026-11-21 (Europe/Moscow). Delivery: [issue #59](https://github.com/Dementius-cell/Game-Translator/issues/59); release gates: [#30](https://github.com/Dementius-cell/Game-Translator/issues/30). **Overlay stability is unresolved.** This is a development status, not release acceptance.

## Current source and installed behavior

Latest production change: [0300108](https://github.com/Dementius-cell/Game-Translator/commit/0300108), following native reuse [b7f10f5](https://github.com/Dementius-cell/Game-Translator/commit/b7f10f5). Its source-equivalent Release build passed with zero warnings/errors and **1724/1724 tests**, plus maintenance and documentation checks. These are local results, not a claim about current remote CI or a published binary release.

The owner rejected the practical result of the ADR-046 750ms contradiction-hold experiment: no perceived stability improvement, and lingering translations interfered with changed scenes. On 2026-11-21 the installed development package was rolled back using the local file menu-contradiction-hold.disabled beside the executable. It takes effect at the next live start. Earlier fixes remain installed; cache, profiles and diagnostic history were not cleared.

**The rollback marker is local-only and is not included in Git.** Experimental code and tests remain in main. Application defaults false, but the current UI enables the experiment when the marker is absent. A clean build therefore does not inherit the owner's rollback. Do not report a repository-wide code revert or re-enable this as an accepted fix. The accepted ADR records authorization to test, not successful live acceptance; its original decision is preserved.

## Retained improvements and evidence

| Method | Evidence and current disposition |
| --- | --- |
| Fresh cached association (ADR-045), fixed anchors and bounded relative geometry | Retained with separate local rollback. Enables independent reconfirmation after padding jitter; first transition still removes the old result. Dynamic bounds are not a fixed screen grid. Overall flicker remains open. |
| Preserve queue position across speculative suppression | Retained; fixes pending work repeatedly moving behind newer requests. Deterministic scheduler coverage; does not eliminate service-capacity limits. |
| Preserve established periodic OCR cadence across association | Retained; new generations keep cadence and last-start time, never old text/source authority. Synthetic moving-background reproduction passed for unchanged and changed text. |
| Up to three exclusive reusable native engines on the narrow English horizontal menu route | Retained. Offline fresh/reused outputs matched all 405 inputs, including blocks/words, sequentially and with three workers. Concurrent median call time 37.8 to 9.1ms. A later fresh-engine replay reproduced all 87 selected failure-associated outputs: reuse-state contamination was not supported for those samples. |
| Live queue/service improvement after native reuse | Two noncontrolled owner runs: OCR median 56.2 to 14.6ms, P95 88.5 to 50.1ms; queue median 888.05 to 412.1ms, P95 1147.3 to 560.7ms; association-to-publication median 3164.923 to 1544.535ms, 15/15 later intervals completed. Published source-expiry events fell 5 to 0 in that pair, but returned in the later multi-menu run. Performance improved; expiry and flicker are not eliminated. |
| Guarded recognition margin and minimum-RGB chromatic preparation | Retained only under existing geometry/pixel/route guards. Scoped margin probes improved without observed regressions; chromatic probe: 488 cases, no correct-to-incorrect regression. Other dim/outlined/textured inputs still fail; do not broaden guards without negative controls. |
| Conservative keycap/rating projection | Retained with raw OCR preserved. Owner logs confirmed isolated decorated-key exclusions. Mixed controller icons, badges and ordinary-label errors remain unresolved. |
| Diagnostic corrections | Retained: proposed/previous/fixed-anchor geometry, exact engine-entry OCR inputs, 250ms capture sampling, pending-hold fields, removal reasons and bounded presentation history. These improve evidence; they are not stability fixes. |
| Recording/HUD changes | Recording switch works; cyan diagnostic panel removed. Recording intentionally hides overlays during capture and cannot measure normal protected-mode flicker. Baseline tests use recording off. Font-fit work is separate from the disappearance priority. |

## Rejected or inconclusive methods

These are finite offline experiments unless identified as an owner game run. Private images, actual text and harnesses are not public fixtures. Do not repeat a rejected method without new evidence or different controls.

| Method | Result and disposition |
| --- | --- |
| First nonempty contradiction held up to 750ms (ADR-046) | Tested in source 0300108; rejected by owner after live run, disabled locally. Do not substitute a longer grace interval for recognition/identity fixes. |
| Raw-text return within 750ms as predicted success count | 54/88 earlier removals had a raw return in that window. Not a projected-authority replay or 54 prevented disappearances; geometry, capture and occlusion also matter. |
| Universal padding | Regressed 9/144 synthetic controls. Rejected; the distinct guarded missing-margin correction above remains. |
| Extra source margins 15/25/50% on 25 inputs | Baseline 18 correct; variants 16/19/21, with 6/5/3 regressions. More aggregate matches do not justify breaking previously correct text. |
| Directional and stroke-guided margins | Best directional variant 20/25 versus 18 baseline but 2 regressions; stroke-guided 16/25 with 1 fix and 3 regressions. Rejected. |
| Cubic enlargement 2x/3x | 34/53 and 30/53 versus 41/53 baseline. Rejected for these inputs. |
| Segmentation changes | SingleLine 41/53; RawLine 33/53 with 11 regressions. SingleBlock/Sparse reproduced baseline outputs. No justified improvement. |
| Adaptive processing at saved engine entry | 3/53 in this probe, but it does not reproduce the whole-profile adaptive route faithfully. Inconclusive for profile-level Adaptive; do not claim that route was fully tested. |
| Stronger chromatic suppression | 29/53 versus 41/53, 12 regressions and 0 fixes. Rejected; preserve the narrow minimum-RGB gate. |
| Contrast-derived identity mask | Matched 11 adjacent stable pairs and 8 fixed discovery canvases, but falsely ignored 4/12 dim added-dot controls. Rejected: can miss actual punctuation changes. |
| Treat a light boxed prefix as a removable control icon | Geometry also accepted legitimate boxed letters/numbers. Rejected; shape/confidence alone cannot justify deleting meaningful content. |
| Local key-box/glyph polarity transforms | 528 paired cases examined. Some glyphs improved, but previously correct synthetic captions regressed and real prefixes remained unstable; outlined/clipped icons were not covered. Rejected. |
| Assume observed detector jitter is nondeterminism | Repeated exact-source replay of 8 selected triggers returned identical raw detections. Strict/nested guards rejected those geometry changes. No basis for global tolerance relaxation. |

## Latest owner run and rollback decision

The final stopped report covers 154.356s on 2026-11-20 at 23:56–23:59 Moscow time; feedback and rollback were recorded on November 21. It used source 0300108's verified precommit binaries, hold enabled and protected capture. There were 53434 lifecycle events, with no dropped lifecycle or presentation events. Engine-input recording is separately bounded and is not assumed complete.

There were 110 distinct pending-hold episodes (duplicate observations collapsed by track/generation): 21 ended with validated agreement to the same confirmed version; 89 ended in removal: 26 timeout, 15 text-version change, 25 speculative contradiction, 21 candidate disappearance, 2 association changes. These are authority outcomes, not proof of 21 prevented visible flickers.

Across the run there were 198 removals of published results: 33 source expiry, 26 hold timeout, 23 association, 15 text-version change, 70 speculative contradiction and 31 candidate disappearance. The user traversed multiple menus. True scene changes must be separated from false removals before comparison; these totals are not 198 bugs or a controlled comparison with the preceding 121s run. Individual latest-run removals have not yet been checked against exact source images. Earlier messenger occlusion must also be excluded from stationary-text comparisons.

## Next work and acceptance

1. Use existing exact OCR inputs and source identities to classify removals into real scene/text changes, wrong OCR on unchanged text, candidate geometry/loss, and source-expiry queue gaps. Do not substitute a nearby screenshot for a missing exact input.
2. Investigate the 33 source expiries and remaining ordinary-label/control-icon recognition errors with chronological per-track evidence. Preserve letters, numbers and punctuation in negative controls; avoid fixture text lists or absolute screen coordinates.
3. Require a demonstrated cause and regression-safe offline result before another production experiment. Keep changes reversible. Existing evidence is sufficient to start; no new owner run is needed just to re-triage it.
4. After a concrete fix, validate unchanged menus, actual label changes, page transitions, capture loss, stop/restart and mixed-zone independence with protected capture. Do not ask the owner to watch every overlay or use recording-mode blanking as baseline flicker evidence.
5. Retain deferred native-memory attribution and cold-import investigation below. Revisit memory earlier for actual pressure/failures. Transferable packaging, signing and release acceptance remain #30; provider capture-loss acceptance remains #34 and official credential evidence #57.

Preserve source provenance, exact normalized text/groups, two-second freshness, three OCR slots, minimum 500ms per-track start spacing, cache/provider boundaries and raw geometry. Permitted relative anchor jitter does not prove semantic equality. Source/authority events and WPF callbacks do not certify monitor presentation.

## Historical implementation and validation records

The dated records below describe their own snapshots. Test counts, planned next runs and previous acceptance statements are historical; the summary above controls current disposition.

## Association implementation — November 2026

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

## Protected-mode follow-up and chromatic texture — 2026-11-19

The owner completed a 125-second protected-mode run, briefly traversing two intermediate pages. Recording was off and there were no capture hide/restore events. Intermediate page changes were separated from stationary-page analysis. The previously unstable top-row label produced 94 identical OCR observations on the first stationary page, but another stationary page still exposed ordinary-label substitutions and a separate detector disappearance. This confirms that recording gaps are not the sole cause; it is not overall flicker acceptance or a controlled before/after timing comparison.

Twelve saved crop reconstructions matched capture delivery timestamps and reproduced their logged OCR, including three errors. Small chromatic background cells contaminated thin neutral text. The existing single horizontal, unprocessed, full-member recognition route now has a bounded preparation before its margin: require neutral brightest common-channel pixels, a dark border, chroma without prominent outliers, coverage between 8% and 40%, and at least eight disconnected chromatic components each no larger than a quarter of the transverse extent. It then derives a grayscale image from the minimum RGB channel. Prominent colored punctuation, long colored strokes, light backgrounds and unsupported routes retain their path. Pixel work remains capped at one million input pixels. Original request pixels, provenance, source geometry and all immediate invalidation/freshness rules remain unchanged; no additional OCR call or text substitution is introduced.

Native verification on 488 cases found no correct-to-incorrect regressions: the 12 matched crops improved from 9 to 12 correct, 96 synthetic color/text/punctuation cases from 56 to 60, and 11 sampled colored captions stayed correct. The earlier 297-case margin suite and 72 multilingual controls retained their scores. All returned block/word bounds remained within source crops. These are finite local probes, not a general OCR accuracy guarantee. Across the 12 matched crops, preparation alone took median 0.058 ms, max 0.089 ms and at most about 79 KB allocated per call in a warm local probe; native end-to-end medians varied by sample set (matched crops 36.7 to 40.6 ms). Actual game CPU/RAM and stability still require live verification.

Release build has zero warnings/errors; 1675/1675 tests pass, including 17 chromatic-texture regressions. Infrastructure ownership instructions are updated; Application/UI contracts, profile, runtime/model versions and styling are unchanged. Private frames/transcripts, synthetic probe inputs, native comparisons and timing evidence are ignored local-only outputs under work/live-review-20261118-1923. Geometry losses and source-expiry gaps remain open; the next owner run checks unchanged-label disappearance on the same pages with recording off, without requiring the owner to track individual overlays.


## Fresh cached association and rollback — 2026-11-19

Owner-approved ADR-045 enables the existing provisional association for a fresh strictly newer same-source cached delivery. First transition still clears translation and authority; independent OCR confirmation and all fixed-anchor/expanded-core/uniqueness guards remain. Application defaults to the old behavior; product live timing opts in unless the local menu-cached-association.disabled flag exists beside the executable. Stop translation, create the flag, then start translation to roll back; remove the flag to re-enable. Diagnostics record the actual run option. Private investigation outputs remain ignored local-only under work/live-review-20261119-0738. First-transition gaps, OCR errors and real-game acceptance remain open; this is not blanket retention of missing labels.

Validation: zero-warning Release build, 1679/1679 tests (four new enabled/rollback and same/changed-text cases), maintenance tooling and docs checks passed. These checks do not replace live acceptance.

## Preserve pending OCR position across speculative suppression — 2026-11-19

A protected transition run retained a published-label expiry despite normal detector timings. Its pending wait restarted without an intervening OCR start or visual renewal. The speculative unchanged-pixel path clears pending work before detection; if the detected crop then differs, re-offering OCR previously assigned a new FIFO position without renewing authority. The scheduler now suppresses the crop while preserving the unfulfilled request position. Validated visual renewal, actual dispatch, expired pending source and retired tracks release the position. It cannot start suppressed work or extend text freshness. No priority-by-text rules, additional concurrency, profile changes or OCR preparation changes are introduced.

A regression reproduced the old ordering failure before the fix; focused scheduler tests cover resumed ordering, suppressed work, current replacement pixels, fulfilled/expired positions and existing fairness/cancellation rules. Private source timelines remain ignored local-only under work/live-review-20261119-0809. This addresses one queue defect; OCR contradictions and real-game stability remain open. Deliver as a separate commit with a backup of the preceding package so rollback preserves ADR-045.

Validation: zero-warning Release build, 1682/1682 full-suite tests including three new scheduler cases, maintenance tooling and docs checks passed. Live validation is still required.

## Exact OCR inputs for remaining failures — 2026-11-19

Owner requested more frequent screenshots. Full capture minimum interval is250ms. Successful OCR calls additionally enqueue exact engine-entry pixels, settings/hints/source provenance and raw recognized blocks. Engine-internal transformations require the same engine build/models. Both engines share a bounded32-input/16MiB queue, separate min(256MiB, configuredFramesMiB) disk budget and20000-input cap per process. Disable/stop, queue limits and storage failures can leave gaps; bounded local diagnostics report losses/failures. No OCR/authority/queue-order algorithm changes. Private evidence remains local-only; existing fixes and rollback remain available.

Validation: zero-warning Release build and1689/1689 tests, including recording cadence, concurrent per-input hashes/settings/results, collection gates/generation, storage failure, oversize/cancellation and both engine registrations. Maintenance and documentation checks passed. Real-game disk/CPU impact remains to be measured; diagnostic sampling is bounded, not lossless.


## Preserve established OCR cadence across association — 2026-11-20

A provisional association previously restarted the visual-quiet wait even when the existing region had already established periodic recognition on a moving background. Restart now creates a new generation while retaining only the established periodic flag and last-start time. Pending pixels, queue position, source history, accepted OCR and text authority are not inherited. The 500ms start interval, three active slots including cancelled work, two-second freshness, fixed geometry guards and independent new-text confirmation remain required. Ordinary registration after disappearance/context change remains unchanged.

The deterministic moving-background regression fails under the former pipeline and passes with the fix for both unchanged and changed text. Scheduler tests cover cadence, minimum start interval, unpromoted quiet tracks, discarded pending frames, retired-slot accounting and stale-source rejection. This targets time to reconfirm after association, not OCR spelling errors or removal prevention. Deliver separately with the previous installed package backed up; rollback restores that package without removing earlier queue/diagnostic fixes. Real-menu improvement remains unverified until an owner run.

Validation: Release build with zero warnings/errors;1694/1694 tests passed, including five new cases. Maintenance tooling, documentation links and staged publication checks passed. UI/real-game performance remains pending; a preceding-package backup provides rollback.


## Reuse native initialization for validated menu OCR — 2026-11-20

The latest owner run kept the three OCR leases occupied for at least95%of the measured session; changing queue order alone cannot remove that service demand. Native profiling isolated repeated model initialization as a substantial cost. The adapter now retains up to three exclusively borrowed Tesseract instances only for eng, explicit Horizontal, StaticMenu, None preparation and SingleLine segmentation. Each call clears adaptive classifier state. Other routes keep their existing per-call initialization. Success returns the instance; faults/cancellation discard it. Model file metadata changes replace it, and adapter shutdown disposes idle instances while outstanding borrowers dispose on return. No additional OCR passes, slot count changes, identity relaxations or text-authority changes.

Private offline evidence under ignored work/live-review-20261119-2258 compares405 saved engine inputs through the full production recognizer. Fresh/reused instances reproduce the same405texts, blocks and words, including the logged outputs. With three concurrent calls, all405outputs also match; median observed call time37.8ms fresh versus9.1ms reused on this host. Sequential comparison32.3ms versus9.0ms. These are offline observations, not an end-to-end or real-game acceptance claim. The primary Tesseract FAQ recommends clearing adaptive state when reusing instances; library/global parallelism limits remain unchanged. A separate preceding-package backup provides rollback. Real-menu queue, expiry, memory and visibility validation remains pending.

Validation: zero-warning Release build,1706/1706 tests, maintenance and documentation checks passed. Twelve added cases cover route exclusions, exclusive reuse, failure discard, model replacement, disposal while borrowed and creation failure. See the [Tesseract reuse FAQ](https://github.com/tesseract-ocr/tessdoc/blob/main/FAQ.md) for adaptive-state guidance.

## Bounded contradiction experiment (ADR-046) — 2026-11-20

The owner approved retaining an already published translation through the first nonempty OCR contradiction for at most750ms, bounded by existing two-second source freshness. A second distinct newer contradiction, empty OCR, loss or geometry reassociation still clears immediately. Only a newer agreeing, detector-validated OCR result cancels pending. The existing geometry matcher permits bounded relative jitter; no fixed OCR grid is introduced. Scope is single-member Horizontal StaticMenu with None preparation and watermark Off. Visual reuse cannot suppress the next normal OCR while a contradiction is pending.

This can delay removal of genuinely changed text; real-game improvement remains unverified. The local marker menu-contradiction-hold.disabled beside the executable disables the experiment at the next live start without rolling back prior fixes. Application defaults false; the UI captures and exports its opt-in. Deterministic tests exercise the state machine and final pipeline snapshots. Source reports and research remain ignored local-only evidence. Next acceptance must include unchanged menu labels, genuine label changes and menu transitions, with overlay recording off.

Validation: zero-warning Release build,1724/1724 tests passed (18 added cases), maintenance tooling and documentation checks passed. The package retains a separate preceding-build backup. Real-game acceptance remains pending; passing pipeline snapshots do not certify physical monitor presentation.

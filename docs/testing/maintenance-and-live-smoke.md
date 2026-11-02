# Maintenance checks and repeatable live smoke

Scope: issue #59. These tools do not change OCR, provider, cache, freshness or runtime versions. Current delivery status is [Menu r15](../agents/menu-r15-status.md).

## Local publication check

The tracked [pre-commit hook](../../.githooks/pre-commit) runs `tools/check-publication.ps1` against the index. Enable it per checkout with `git config core.hooksPath .githooks`. Do not replace an existing custom hooks path without preserving its behavior.

It rejects local output/runtime directories, the two private handoffs, new artifact files and new common binary/image files, and checks staged whitespace. Existing approved fixtures remain allowed. This path guard is not a secret scanner: review the complete staged diff for private text and credentials. Explicit promotion of new fixture evidence follows [the evidence policy](../evidence-artifacts.md); review the guard when such promotion is approved, rather than treating a generic commit as approval.

## Diagnostic summary

Use PowerShell with explicit literal input files:

```powershell
$reports = @('C:\path\to\first-report.txt', 'C:\path\to\second-report.txt')
./tools/summarize-live-diagnostics.ps1 -LiteralPath $reports |
    Set-Content -LiteralPath ./work/live-summary.json -Encoding UTF8
```

The input paths are placeholders. `work/live-summary.json` is ignored local generated output, not a public fixture or CI dependency. Create the local work directory first if necessary. Reports are numbered in input order; file names, paths, IDs, OCR/translation text and error messages are not emitted.

Each input is a separate report snapshot. Several snapshots can describe the same session: do not sum them as independent sessions. Prefer one final report per known session. Event sequences are deduplicated within a report; unknown and out-of-order events are counted and excluded. Dropped events, report truncation and an unfinished empty-overlay interval are reported explicitly. Unsupported/no-event reports fail instead of inventing a clean session.

Timing distributions use nearest-rank P50/P95 on retained numeric events. OCR timing uses CandidateOcrCompleted; total candidate time uses CandidateWorkCompleted and is not OCR-only time. Provider queue and invocation timing are deduplicated by diagnostic request ID and available only where the report records those fields. Missing metrics are null/absent, never zero-filled. These diagnostics do not measure native import or prove app shutdown.

An empty-snapshot interval starts only after a nonempty publication and ends at the next nonempty publication. Initial empty state and the open final interval are excluded from completed intervals. This measures published snapshots, not actual screen visibility or a proven flicker bug; per-candidate loss while other text remains visible requires targeted lifecycle review.

## Repeatable UI scenarios

Run against the existing approved development package; do not rebuild/download runtime for this checklist. Use a disposable profile and synthetic on-screen text. Local screenshots and diagnostic reports remain private evidence. Computer Use can perform the UI steps, while lifecycle summaries supply timing evidence; neither substitutes for a real-game endurance run.

| Scenario | Steps | Required observation |
| --- | --- | --- |
| Background readiness | Open the app; use profile/settings controls during preparation. | UI responds, one preparation progresses through real stages, Ready freezes the duration. Record cold/warm conditions; do not label a warm restart cold. |
| Closing during preparation | Close while preparing, then reopen. | No late ready state or surviving worker from the closed app. If preparation is too fast, mark untested; synthetic lifecycle tests cover cancellation. |
| Light/Dark banner | Switch themes while idle/ready and inspect the banner and profile heading. | Readable status and stable layout. Error/retry needs a controlled test fixture; do not remove runtime or alter drivers to force it. |
| Stable menu | Show a fixed synthetic menu for 60 seconds. | Translations remain correct; record any disappearance time and correlate with lifecycle events. |
| Source loss and return | Hide the synthetic source, restore it, then replace one label. | Old translation is removed according to current freshness policy; updated text does not reuse stale source authority. |
| Mixed zones | Configure separate menu and dialogue zones; change one source at a time. | The unchanged zone remains independent; no stale result replaces changed text. |
| Stop/start | Stop live, verify hidden overlay, then start again. | Clean stop and subsequent translation; inspect restart/failure counters without assuming every new session is a runtime fault. |

Record build commit/package, scenario, observed result, local report number and untested conditions. Return the app to stopped live state and restore any settings changed for the test. Do not mark #30 accepted from this checklist.

## Proportionate test policy

The maintenance change adds no production behavior. Locally run the synthetic PowerShell maintenance checks, three Python protocol tests and docs validation; CI retains the full .NET regression suite. Do not repeat GPU bootstrap, native OCR probes, packaging or endurance for documentation-only changes.

The reviewed OCR startup tests are not duplicates: direct detector missing-runtime behavior differs from managed readiness; diagnostic startup deadlines differ from unbounded application startup; cancellation/drain differs from ordinary timeout; UI status wiring differs from worker stage propagation. Retain these tests. Remove a test only after identifying equivalent behavioral coverage, not merely a similar name or a long execution time. Source-string UI tests are candidates for replacement when equivalent runtime tests exist, not for unconditional deletion.

Synthetic tooling verification: `tests/maintenance-tools.tests.ps1`. It writes isolated ignored outputs beneath `work/maintenance-tests/` and checks private-text exclusion, locale decimals, request deduplication, empty intervals, invalid inputs and blocked publication paths. It needs no GPU, network or private data.

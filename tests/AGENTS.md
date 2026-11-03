# Test Instructions

These instructions apply to `tests/**`.

## Coverage Expectations

- Add focused tests for every behavior change.
- Prefer small unit tests for geometry, grouping, masking, caching, and source ordering.
- Add smoke/source tests when UI wiring, diagnostics export, hotkeys, or XAML behavior changes.

## Vertical CJK Regression Areas

When changing vertical CJK behavior, cover at least one relevant case:

- right-to-left column order;
- top-to-bottom order inside a column;
- wide horizontal OCR noise that should not bridge columns;
- halftone/body/background false positives;
- mask source blocks staying distinct from translation source groups.

## Stability

- DetectorRuntimeTests and DetectorTimingTests belong to the non-parallel DetectorProcessCollection because their real child-process timing assertions must not measure contention from unrelated suites. Keep request/startup deadlines and cancellation assertions intact; pure detector tests remain parallel.
- Keep tests deterministic and independent of live network services.
- Do not require real API keys or credentials.
- Tests that instantiate WPF windows or load theme dictionaries belong to the non-parallel `WpfResourceCollection`: pack-URI resource packages are shared across STA threads. Keep pure source and application tests outside this collection.
- `maintenance-tools.tests.ps1` validates tooling with synthetic reports and an isolated Git index under ignored `work/maintenance-tests/`. Keep it independent of GPU, private logs and credentials. Similar names alone do not establish redundant coverage; preserve timeout, cancellation and source-authority checks unless equivalent behavior is verified elsewhere.

## Calibration Sandbox

- `tests/GameTranslator.Tests/Calibration/**` may use approved fixture data to bypass individual runtime stages for offline golden-reference calibration.
- A passing calibration test is evidence for a future production change, not a production rule change by itself.
- Follow the nested `AGENTS.md` in that directory before adding or changing calibration tests.
- Follow `docs/testing/calibration-and-smoke-workflow.md` when deciding whether a generated screenshot, scorecard, smoke output, or full-screen frame should become a tracked fixture or remain local evidence.

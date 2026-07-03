# Calibration and Smoke Workflow

Status: active test-architecture workflow for issue #35.

This workflow keeps real OCR/overlay scenarios visible without letting generated evidence drift into production behavior or accidental commits.

## Evidence Buckets

| Bucket | Paths | Commit rule |
| --- | --- | --- |
| Tracked deterministic fixture | `artifacts/calibration/<fixture-id>/**` plus the test that consumes it | Commit only when the fixture is deterministic, reviewed, and required by a test or stable spec. |
| Generated calibration evidence | new `artifacts/calibration/<candidate-id>/**`, generated scorecards, contact sheets, placement maps | Keep local by default. Promote with `git add -f` only after deciding the exact fixture contract. |
| Manual smoke evidence | `artifacts/manual-smoke*/**`, `outputs/**`, `work/**`, debug reports, timestamped screenshots | Keep local unless a commit explicitly records a small, stable summary. |
| Runtime local dependency | `tessdata/**`, `TESSDATA_PREFIX` targets | Never commit model binaries; validate with `tools/check-tessdata.ps1`. |
| Build output | `bin/**`, `obj/**`, `logs/**` | Never use as a deterministic test input. |

## What Becomes A Tracked Fixture

Promote generated evidence into a tracked fixture only when all of these are true:

- the scenario represents a real regression or calibration question that should survive chat/context loss;
- the source image or manifest is fixed and deterministic;
- the OCR capture area is explicit, preferably the whole fixture frame for mixed full-screen scenarios;
- expected OCR/grouping/mask/overlay values are stored in machine-readable JSON or asserted in a focused test;
- visual evidence has been reviewed by the project owner or recorded as accepted calibration evidence;
- the fixture is consumed by a deterministic test under `tests/GameTranslator.Tests/Calibration/**`;
- the fixture does not require live network services, API keys, UI save dialogs, or system OCR language installation.

Screenshots and contact sheets are supporting evidence. They should not be the only source of accepted numeric values.

## Current Classification

| Artifact or scenario | Decision |
| --- | --- |
| `artifacts/calibration/full-screen-mixed-content-frame/**` | Tracked deterministic fixture; keep. It represents the full-image OCR capture scenario with mixed language, orientation, and text sizes. |
| `artifacts/calibration/vertical-long-translation-fit-frame/**` | Tracked deterministic fixture; keep. It asserts accepted vertical long-translation fit values through a focused calibration test. |
| `artifacts/calibration/mixed-worst-case-frame/**` | Candidate review package. Keep local until readable overlay rules and accepted values are chosen. |
| `artifacts/calibration/mixed-orientation-frame/**` | Candidate review package. Keep local until it has a focused fixture contract distinct from the full-screen mixed fixture. |
| Thai and multi-column Japanese generated calibration packages | Candidate review packages. Keep local until one or more scenarios are selected as stable regressions. |
| `artifacts/calibration/candidate-evidence.png`, `artifacts/calibration/scorecard.json`, `artifacts/calibration/real-ocr-sweep.json` | Generated sweep outputs. Keep local unless a small normalized subset becomes a fixture contract. |
| `artifacts/manual-smoke*/**`, `outputs/**`, `work/**` | Local manual smoke/debug evidence. Summarize in docs or issue comments; do not make CI depend on these paths. |

## Fixture Promotion Workflow

1. Capture or generate the real scenario as a fixed image or manifest.
2. Put exploratory output under a generated/local evidence path.
3. Review readability and accepted values visually.
4. Decide the minimal fixture contract: source frame, manifest, expected values, and optional review image.
5. Add or extract a focused calibration test that consumes only that contract.
6. Force-add only the approved fixture files if they are ignored by local evidence rules.
7. Run the focused test, `dotnet test` when code changed, and `tools/check-docs-mini.ps1 -Json` when docs changed.
8. Post a multi-line issue comment through a UTF-8 body file and verify it with `gh issue view --json comments`.

## Manual Smoke Workflow

Manual smoke is for user-like checks that exercise the real app, diagnostics export, or local OCR dependencies. It must not block input/output or require SaveFileDialog interaction when a silent output folder is available.

Use `outputs/**` or `artifacts/manual-smoke*/**` for run products. Keep summaries short and cite them as local evidence unless the project owner explicitly asks to commit a specific artifact.

Manual smoke can motivate a deterministic fixture, but it is not the fixture by itself. If a real manual frame shows worse behavior than a calibration image, extract the smallest reproducible frame and repeat the fixture promotion workflow.

## Production Boundary

Calibration tests may prove candidate OCR, grouping, fit, mask, or overlay rules. They do not change application behavior by themselves.

Production changes still require the normal approval path when they touch OCR interfaces, overlay placement, profile schema, translator behavior, diagnostics schema, architecture, or restricted governance areas.

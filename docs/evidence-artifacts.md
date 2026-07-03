# Evidence Artifact Policy

Status: active documentation hygiene rule.

This project uses generated screenshots, JSON scorecards, debug reports, and smoke summaries as review evidence. These files do not all have the same availability in a clean checkout, so documentation must say what kind of artifact a path refers to.

## Artifact Categories

- Tracked deterministic fixtures: committed files under `artifacts/calibration/**` that are used by deterministic tests or stable reference documentation. These may be used as repeatable review inputs.
- Generated calibration evidence: files written by calibration tests under `artifacts/calibration/**` for visual review. They may be untracked until the project owner explicitly approves committing them.
- Manual smoke evidence: files under `artifacts/manual-smoke-diagnostics/**`, `artifacts/manual-real-smoke-diagnostics/**`, and similar one-off diagnostic folders. Treat these as local review snapshots unless a commit explicitly tracks them.
- Ignored local outputs: `outputs/**` and `work/**`. These are local run products and harness workspaces. They are not expected to exist in a clean checkout and must not become deterministic CI inputs.
- Built binaries: files under `bin/**` or `obj/**`. They exist only after a local build and should be documented as build outputs, not source artifacts.

## Documentation Rules

- When citing an artifact path, state whether it is tracked, generated/reproducible, local-only, ignored output, or a build output.
- Do not make local-only or ignored artifacts required for deterministic CI.
- Prefer tracked fixture manifests and tests over screenshots when recording accepted numeric values.
- Before using a historical local path as current evidence, rerun the test or harness that generated it, or confirm that the file is intentionally tracked.
- Keep `work/**` and `outputs/**` out of normal commits unless the project owner explicitly asks to promote a harness or output.
- If a generated artifact should become part of the project record, commit it intentionally with the doc/test that consumes it; do not rely on an incidental dirty worktree.

## Audit Notes

Inline code paths in handoff documents are often historical breadcrumbs, not links. A documentation audit should distinguish:

- broken Markdown links, which should be fixed immediately;
- optional future-file mentions, such as a hypothetical `CONTEXT.md`;
- local generated evidence, which should be labeled with availability;
- shorthand file names that refer to a previously named artifact directory.

Run the mini-audit with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-docs-mini.ps1 -Json
```

Expected hygiene result:

- `markdownLinkProblemCount` is `0`;
- `actionableBacktickProblemCount` is `0`.

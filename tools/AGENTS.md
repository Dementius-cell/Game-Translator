# Tooling Instructions

These instructions apply to `tools/**`, including its standalone benchmark project and runtime lock files.

## Purpose and Ownership

- Reproducible bootstrap and verification of the packaged Paddle runtime, detector model and locked Tesseract packs.
- Local package build, integrity verification, finalization and archive scripts.
- Documentation and language-data checks.
- The standalone `ComicGeometryCandidateDetectorBenchmark` and its runner. Benchmark results are evidence, not production policy or an additional product module.

## Local Contracts

- Preserve the ADR-030 boundary: Paddle supplies detector bounds; Tesseract recognizes accepted crops. Tooling must not introduce a different recognizer, silent full-frame fallback or provider substitution.
- Keep runtime versions, official download locations, model identities and checksums consistent with the lock and generated manifest. Do not bypass a failed integrity check or label a skipped GPU probe as successful GPU validation.
- Keep on-demand user language installation distinct from the exact locked release-pack set. A catalog expansion does not silently expand or replace the release lock.
- Treat user-supplied input/output paths literally. Before recursive deletion, pruning or moving, resolve the target and verify that it stays inside the intended output directory. Account for junction/reparse-point targets; preserve source inputs, shared runtime/tessdata and ordinary Python bytecode caches.
- Keep uploaded pages, OCR transcripts, screenshots, benchmark results, models and binaries local-only. Use ignored output directories and follow `docs/evidence-artifacts.md`. Do not copy them into source publication or make private examples mandatory test inputs.
- A source update is not a request to replace an installed test build, create an archive, sign a package or publish a release. Follow the owner's authorized scope and existing release decisions; do not require another confirmation for actions already authorized.
- Report same-host clean-root verification as such; it is not evidence from a separate clean machine. Never claim a GPU, package or recovery check that did not run.

## Work Guidance

- Preserve explicit input/output parameters, error propagation and nonzero exit codes. Prefer validation-only paths before expensive packaging when available.
- Keep exploratory evidence under ignored `work/` or the script's documented local output location. Do not overwrite unrelated experiment results or broaden cleanup targets.
- Use native PowerShell path operations on Windows. Where RTK is available, use it for supported verbose external commands; use native output or `rtk proxy` when exact output is required for parsing.

## Verification

- For documentation-only edits, run the documentation mini-check; builds and runtime downloads are not required.
- For PowerShell changes, parse the modified scripts and run the affected validation or smoke path with isolated outputs. Check failure paths relevant to the change, such as missing inputs, checksum mismatch or existing output protection.
- For benchmark code changes, build the standalone project and exercise the changed behavior on synthetic or approved local inputs. Report output location and limitations; do not promote a benchmark heuristic into the application automatically.
- For runtime/model/lock changes, run applicable bootstrap, hash, model and GPU verification. For packaging changes, run relevant manifest, package-integrity and recovery checks. Record unrun hardware-dependent checks explicitly.

## Child Instruction Index

No nested `AGENTS.md` files are currently defined below this directory. Both tooling subdirectories inherit this file and the root instructions.

# NIGHTWIRE Episode Validator

A .NET command-line tool for validating unpacked NIGHTWIRE episode packages with JSON Schema and package-wide semantic checks.

## Requirements

- .NET 10 SDK (LTS).
- NuGet dependency: `JsonSchema.Net` 9.7.0 (Draft 2020-12 support).

## Run

From the repository root:

```bash
dotnet run --project tools/Nightwire.EpisodeValidator -- examples/Episode_001
```

Exit codes:
- `0`: validation passed.
- `1`: package validation failed.
- `2`: command usage or input directory error.

## Checks

- Required episode descriptor and manifest.
- Full JSON Schema Draft 2020-12 validation for the descriptor, manifest, and every typed document.
- Episode/manifest identity and default locale declaration.
- Manifest paths, duplicate/case-colliding paths, file existence, inventory completeness.
- File byte counts and SHA-256 digests.
- Asset ID uniqueness and asset-to-file references.
- Character, post, conversation, call, and minigame ID uniqueness and references.
- Conversation participants, senders, message IDs, supported message types, choice destinations, call invitations, and media references.
- Character post authors, comment authors, and media references.
- Call participants, prerecorded mode, required media types, subtitle locales/timing, allowlisted cue types/effects, cue timing, and matching completion events.
- Minigame configuration invariants, evidence references, and matching completion events.
- Episode entry event and supported event action references.
- Localization files for every declared locale and referenced localization-key coverage.
- Rejection of unsafe relative paths and symbolic links/reparse points.

The validator evaluates the checked-in Draft 2020-12 schemas for every typed package document, with format validation enabled, then performs package-wide semantic and integrity checks. The schema directory is discovered from the repository checkout; run the CLI from within the NIGHTWIRE repository so the matching `schemas/0.1` contract is available.

## Current limitations

- The CLI requires the matching repository `schemas/0.1` directory at validation time; it does not silently fall back to weaker validation if schemas are missing.
- This tool validates an unpacked directory. Archive extraction safety and signed release metadata belong to the future installer.
- It does not execute code from episode packages.


## Integration tests

Run from the repository root:

```bash
python -m unittest discover -s tests -v
```

The tests require Python 3.10+ and the .NET 10 SDK. They build the validator and exercise a valid package plus integrity, broken-reference, and path-traversal failures.

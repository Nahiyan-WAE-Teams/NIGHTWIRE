# NIGHTWIRE Episode Validator

A dependency-free .NET command-line tool for validating unpacked NIGHTWIRE episode packages.

## Requirements

- .NET 10 SDK (LTS).
- No third-party NuGet packages.

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
- Supported document schema version.
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

The validator checks the current v0.1 contract and package-level semantics. The JSON Schema files remain the machine-readable structural contract; this initial CLI does not implement the full JSON Schema Draft 2020-12 evaluation algorithm.

## Current limitations

- The CLI performs semantic checks for calls, minigames, and posts but does not execute the full JSON Schema Draft 2020-12 evaluator.
- This tool validates an unpacked directory. Archive extraction safety and signed release metadata belong to the future installer.
- It does not execute code from episode packages.


## Integration tests

Run from the repository root:

```bash
python -m unittest discover -s tests -v
```

The tests require Python 3.10+ and the .NET 10 SDK. They build the validator and exercise a valid package plus integrity, broken-reference, and path-traversal failures.

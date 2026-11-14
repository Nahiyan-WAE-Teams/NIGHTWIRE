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
- Character and conversation ID uniqueness and references.
- Conversation participants, senders, message IDs, supported message types, choice destinations, and media references.
- Episode entry event and supported event action references.
- Localization files for every declared locale and referenced localization-key coverage.
- Rejection of unsafe relative paths and symbolic links/reparse points.

The validator checks the current v0.1 contract and package-level semantics. The JSON Schema files remain the machine-readable structural contract; this initial CLI does not implement the full JSON Schema Draft 2020-12 evaluation algorithm.

## Current limitations

- Call and minigame definition schemas are not yet part of v0.1, so call/minigame action IDs are not resolved yet.
- Character post IDs are retained in the profile schema but post definitions are not validated yet.
- This tool validates an unpacked directory. Archive extraction safety and signed release metadata belong to the future installer.
- It does not execute code from episode packages.

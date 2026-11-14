# NIGHTWIRE

A data-driven episodic mystery game built with Unity.

NIGHTWIRE is designed around a reusable game runtime and independently downloadable episode packages. The installed application provides the engine and interface; episode packages provide authored story content, character data, events, localization, and media.

## Project principles

- **Content-driven:** new episodes should not require an application update when they use capabilities already supported by the runtime.
- **Package-first:** define and validate the episode format before implementing the episode loader.
- **Offline-capable:** a fully downloaded episode should be playable without a network connection, except for explicitly optional online features.
- **Immutable authored content:** downloaded episode files are read-only at runtime; player progress is stored separately.
- **Versioned and verifiable:** packages declare format/runtime compatibility and include asset integrity metadata.
- **Accessible:** respect system accessibility settings, text scaling, contrast, and reduced-motion preferences.

## Current development stage

The episode package contract, schemas, sample package, semantic validator, portable C# runtime core, and first verified episode-directory loader are in place as drafts. Current work is building the runtime foundation before Unity presentation.

See the [episode package specification](docs/architecture/EPISODE_PACKAGE_SPEC.md), [JSON Schema guide](docs/architecture/JSON_SCHEMA_GUIDE.md), [client runtime blueprint](docs/architecture/CLIENT_RUNTIME_BLUEPRINT.md), [public Moonvale reference research](docs/research/MOONVALE_PUBLIC_REFERENCE.md), [runtime core](src/Nightwire.Runtime/README.md), and [sample Episode 001](examples/Episode_001/episode.json). The v0.1 contracts include character posts, prerecorded calls, and declarative minigames.

## Planned technology

- Unity for the mobile application and presentation layer.
- C# for the runtime and content interpreter.
- JSON for authored episode definitions and manifests.
- Local media files referenced by stable asset IDs.

Current baseline: Unity 6.3 LTS (to be verified at editor setup), .NET Standard 2.1 runtime profile, C# 9.0, and System.Text.Json 10.0.15 for JSON parsing.

## Development status

A Unity scene-based playable game is not implemented yet. The portable C# runtime now includes domain/progress models, an allowlisted story-event interpreter, and an unpacked episode loader that verifies manifest inventory, file sizes, SHA-256 hashes, safe paths, and cross-file references. Unity presentation, archive extraction, signed release verification, and actual test execution remain pending.


## Validate an episode package

The initial package validator is available under `tools/Nightwire.EpisodeValidator`. With the .NET 10 SDK installed, run:

```bash
dotnet run --project tools/Nightwire.EpisodeValidator -- examples/Episode_001
```

The validator checks package integrity and cross-file references. See its [README](tools/Nightwire.EpisodeValidator/README.md) for the exact validation boundary.


## Validator tests

The standard-library Python integration tests build and exercise the validator:

```bash
python -m unittest discover -s tests -v
```

See [tests/README.md](tests/README.md). GitHub Actions remains intentionally deferred.

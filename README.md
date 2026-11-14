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

The episode package contract, initial schemas, sample package, and semantic validator are in place as drafts. Current work is defining the reusable client runtime before Unity implementation.

See the [episode package specification](docs/architecture/EPISODE_PACKAGE_SPEC.md), [JSON Schema guide](docs/architecture/JSON_SCHEMA_GUIDE.md), [client runtime blueprint](docs/architecture/CLIENT_RUNTIME_BLUEPRINT.md), [public Moonvale reference research](docs/research/MOONVALE_PUBLIC_REFERENCE.md), and [sample Episode 001](examples/Episode_001/episode.json). The v0.1 contracts include character posts, prerecorded calls, and declarative minigames.

## Planned technology

- Unity for the mobile application and presentation layer.
- C# for the runtime and content interpreter.
- JSON for authored episode definitions and manifests.
- Local media files referenced by stable asset IDs.

Exact Unity, .NET profile, and package dependency versions will be selected and verified before implementation.

## Development status

No playable game or Unity runtime is claimed yet. The next implementation milestone is the runtime domain and package-loading foundation; documentation remains a draft until implementation and tests establish otherwise.


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

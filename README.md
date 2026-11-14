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

The episode package contract, schemas, sample package, semantic validator, portable C# runtime core, verified loader, conversation/call/minigame interpreters, save system, asset resolver, HTTPS downloader, staged ZIP installer, signed release-catalogue verifier, and interrupted-install recovery are implemented as draft runtime foundations. Unity presentation and app integration remain future work.

See the [episode package specification](docs/architecture/EPISODE_PACKAGE_SPEC.md), [JSON Schema guide](docs/architecture/JSON_SCHEMA_GUIDE.md), [client runtime blueprint](docs/architecture/CLIENT_RUNTIME_BLUEPRINT.md), [public Moonvale reference research](docs/research/MOONVALE_PUBLIC_REFERENCE.md), [runtime core](src/Nightwire.Runtime/README.md), and [sample Episode 001](examples/Episode_001/episode.json). The v0.1 contracts include character posts, prerecorded calls, and declarative minigames.

## Planned technology

- Unity for the mobile application and presentation layer.
- C# for the runtime and content interpreter.
- JSON for authored episode definitions and manifests.
- Local media files referenced by stable asset IDs.

Current baseline: Unity 6.3 LTS (to be verified at editor setup), .NET Standard 2.1 runtime profile, C# 9.0, and System.Text.Json 10.0.15 for JSON parsing.

## Development status

A Unity scene-based playable game is not implemented yet. The portable C# runtime includes domain/progress models, allowlisted story/call/minigame interpreters, a verified episode loader, deterministic conversation runtime, versioned saves, unlocked asset access, HTTPS archive downloading, staged ZIP installation, ECDSA P-256 signed release-catalogue verification, and startup recovery for interrupted installs. Unity presentation, app-side trusted-key provisioning and sequence persistence, production signing infrastructure, cloud synchronization, encryption, save-format migrations, and actual test execution remain pending. The runtime test harness registers 33 cases; none are claimed as passing because execution remains deferred.


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

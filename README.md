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

The episode package contract, schemas, sample package, semantic validator, portable C# runtime core, verified loader, conversation/call/minigame interpreters, save system, asset resolver, HTTPS downloader, staged ZIP installer, signed release-catalogue verifier, interrupted-install recovery, the portable episode release manager, a UI-neutral episode library presentation adapter, an atomic per-episode session/install gate, and initial Unity UI Toolkit archive-screen assets are implemented as draft foundations. The first Unity UI Toolkit episode-library screen source is now present under `unity/`; scene composition, platform adapters, and full app integration remain future work.

See the [Unity episode library UI integration guide](docs/architecture/UNITY_EPISODE_LIBRARY_UI.md), [episode package specification](docs/architecture/EPISODE_PACKAGE_SPEC.md), [JSON Schema guide](docs/architecture/JSON_SCHEMA_GUIDE.md), [client runtime blueprint](docs/architecture/CLIENT_RUNTIME_BLUEPRINT.md), [public Moonvale reference research](docs/research/MOONVALE_PUBLIC_REFERENCE.md), [runtime core](src/Nightwire.Runtime/README.md), and [sample Episode 001](examples/Episode_001/episode.json). The v0.1 contracts include character posts, prerecorded calls, and declarative minigames.

## Planned technology

- Unity for the mobile application and presentation layer.
- C# for the runtime and content interpreter.
- JSON for authored episode definitions and manifests.
- Local media files referenced by stable asset IDs.

Current baseline: Unity 6.3 LTS (to be verified at editor setup), .NET Standard 2.1 runtime profile, C# 9.0, and System.Text.Json 10.0.15 for JSON parsing.

## Development status

A Unity scene-based playable game is not implemented yet. The portable C# runtime includes domain/progress models, allowlisted story/call/minigame interpreters, a verified episode loader, deterministic conversation runtime, versioned saves, unlocked asset access, HTTPS archive downloading, staged ZIP installation, ECDSA P-256 signed release-catalogue verification, startup recovery for interrupted installs, and the episode release manager for signed catalogue refresh, verified downloads, offline library discovery, and session-safe updates. Unity scene composition, app-side trusted-key provisioning and protected sequence storage, production signing infrastructure, cloud synchronization, encryption, save-format migrations, and actual test execution remain pending. The runtime test harness registers 36 cases; none are claimed as passing because execution remains deferred.


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


The release manager requires host-provided trusted signing keys, protected catalogue sequence storage, a numeric runtime version, and an atomic session/install lease adapter. The Unity UI Toolkit archive source is present, but scene integration and Android platform adapters are not complete. Build, runtime tests, validator tests, and CI remain deferred and have not been run.


The initial Unity episode archive screen source is under `unity/Assets/Nightwire`. It is not yet a complete Unity project: scene assets, runtime assembly import, System.Text.Json dependency packaging, Editor validation, and Android IL2CPP verification remain pending.


The initial Unity presentation source and setup boundary are documented in [unity/README.md](unity/README.md). It is not yet a complete Unity project or playable scene.

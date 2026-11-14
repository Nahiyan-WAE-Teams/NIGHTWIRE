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

The repository is being established. The first milestone is the episode package contract and its validation rules. Runtime implementation follows after that contract is reviewed.

See [the episode package specification](docs/architecture/EPISODE_PACKAGE_SPEC.md).

## Planned technology

- Unity for the mobile application and presentation layer.
- C# for the runtime and content interpreter.
- JSON for authored episode definitions and manifests.
- Local media files referenced by stable asset IDs.

Exact Unity, .NET profile, and package dependency versions will be selected and verified before implementation.

## Development status

No playable game or runtime implementation is claimed yet. Documentation in this repository describes the proposed architecture until implementation and tests establish otherwise.

# NIGHTWIRE Runtime Core

Portable domain/progression foundation for the Unity client.

## Compatibility decision

- Unity target baseline: Unity 6.3 LTS, subject to project installation verification.
- Runtime library target: .NET Standard 2.1.
- C# language level: 9.0.
- No UnityEngine dependency in this assembly.
- No third-party packages in the core.

Unity 6 documents .NET Standard 2.1 as its cross-platform API compatibility profile. Unity's release guidance recommends LTS for projects locking a production baseline. The current Unity 6 LTS line is 6.3; the Unity release archive should be checked again when creating the actual Unity project.

## Current implementation

- Episode runtime identity and known-reference index.
- Story event and action domain models.
- Player-owned progress state kept separate from immutable episode content.
- Deterministic trigger matching.
- Preflight validation of every action before mutating state.
- Allowlisted action execution for conversation navigation, notifications, asset unlocks, story flags, prerecorded calls, and minigames.
- Duplicate one-shot event suppression.

## Boundaries

This is a pure C# runtime core. It does not load JSON, access files, render Unity UI, play media, download packages, or implement cloud saves yet. Those responsibilities will be added in separate adapters/modules.

The episode loader must map validated package JSON into these domain models. The Unity presentation layer must consume typed runtime commands and must never execute arbitrary episode-provided code.

## Runtime tests

A dependency-free console test harness is available:

```bash
dotnet run --project tests/Nightwire.Runtime.Tests
```

It covers episode-start execution, one-shot idempotency, atomic rejection of invalid action batches, and typed asset/call commands. It has not been executed in this environment.

## Official compatibility references

- [Unity 6 release support](https://unity.com/releases/unity-6/support) — Unity identifies 6.3 LTS as its current LTS baseline and recommends LTS when locking a production version.
- [Unity .NET profile guidance](https://docs.unity3d.com/Manual/overview-of-dot-net-in-unity.html) — Unity recommends the .NET Standard API compatibility profile for new cross-platform projects.

The repository's core project targets netstandard2.1 to match Unity's documented profile. The actual Unity Editor installation and Android build compatibility still need to be verified when the Unity project is created.

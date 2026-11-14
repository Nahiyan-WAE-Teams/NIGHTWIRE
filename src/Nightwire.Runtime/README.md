# NIGHTWIRE Runtime Core

Portable domain/progression foundation for the Unity client.

## Compatibility decision

- Unity target baseline: Unity 6.3 LTS, subject to project installation verification.
- Runtime library target: .NET Standard 2.1.
- C# language level: 9.0.
- No UnityEngine dependency in this assembly.
- JSON parsing: System.Text.Json 10.0.15 (Microsoft-maintained; netstandard2.0-compatible package).

Unity 6 documents .NET Standard 2.1 as its cross-platform API compatibility profile. Unity's release guidance recommends LTS for projects locking a production baseline. The current Unity 6 LTS line is 6.3; the Unity release archive should be checked again when creating the actual Unity project.

## Current implementation

- Episode descriptor and manifest models.
- Verified unpacked-package loader with manifest inventory, file-size and SHA-256 checks.
- Safe relative-path checks, symlink/reparse-point rejection, unlisted-file rejection, and package/JSON size limits.
- Indexed character profiles, conversations, posts, calls, minigames, events, assets, and localization dictionaries.
- Cross-reference checks for characters, messages, choices, calls, minigames, assets, and event actions.
- Episode/runtime compatibility and default/supported locale checks.
- Episode runtime identity and known-reference index.
- Story event and action domain models.
- Player-owned progress state kept separate from immutable episode content.
- Deterministic trigger matching.
- Preflight validation of every action before mutating state.
- Allowlisted action execution for conversation navigation, notifications, asset unlocks, story flags, prerecorded calls, and minigames.
- Duplicate one-shot event suppression.

## Boundaries

The package loader verifies an already-unpacked episode directory and exposes cloned JSON documents plus a typed story-event runtime definition. It does not extract archives, download packages, authenticate publisher signatures, render Unity UI, play media, or implement cloud saves. Archive extraction and signed release verification remain installer responsibilities.

The loader performs package-integrity and cross-reference checks but is not a full JSON Schema Draft 2020-12 evaluator. Release validation must still run the package validator/schema layer before installation. The Unity presentation layer must consume typed runtime commands and must never execute arbitrary episode-provided code.

## Runtime tests

A dependency-free console test harness is available:

```bash
dotnet run --project tests/Nightwire.Runtime.Tests
```

It covers episode-start execution, one-shot idempotency, atomic rejection of invalid action batches, typed asset/call commands, sample package loading, tamper/unlisted-file rejection, and minimum-runtime compatibility. It has not been executed in this environment.

## Official compatibility references

- [Unity 6 release support](https://unity.com/releases/unity-6/support) — Unity identifies 6.3 LTS as its current LTS baseline and recommends LTS when locking a production version.
- [Unity .NET profile guidance](https://docs.unity3d.com/Manual/overview-of-dot-net-in-unity.html) — Unity recommends the .NET Standard API compatibility profile for new cross-platform projects.
- [System.Text.Json 10.0.15 on NuGet](https://www.nuget.org/packages/System.Text.Json/10.0.15) — Microsoft-maintained JSON parser package; NuGet lists .NET Standard 2.0 compatibility.

The repository's core project targets netstandard2.1 to match Unity's documented profile. The actual Unity Editor installation and Android build compatibility still need to be verified when the Unity project is created.

## Package loader

The loader entry point is:

```csharp
var package = new EpisodePackageLoader().Load(episodeDirectory);
var openingEvent = package.RuntimeDefinition.EntryEventId;
var openingConversation = package.RuntimeDefinition.ConversationIds;
```

The loader currently consumes the unpacked directory layout. It verifies every manifest-listed file before parsing content, then rejects unlisted files and unresolved cross-file references. It enforces configurable defaults of 512 MiB total package size and 8 MiB per JSON file. These limits can be changed through `EpisodePackageLoaderOptions`.

The loader does not authenticate who published a package. A future installer must verify a signed release index before treating downloaded content as trusted.


## Conversation runtime

`Conversations/ConversationRuntime` interprets the verified conversation documents in a loaded package. The host advances time explicitly through `Advance`; there are no background timers or Unity dependencies in the core.

- Authored `delay_seconds` values gate message delivery and can be advanced deterministically by the host.
- A delivered choice pauses progression until a valid option is selected; authored choice flags and relationship deltas update player-owned story state.
- `next_message_id` selects an explicit branch; an omitted property continues in authored order, while an explicit JSON null ends the conversation.
- Delivery, read state, selected options, the current/next message, and partially elapsed delay are stored in `EpisodeProgressState.ConversationProgress`, not in episode files.
- Marking a delivered message read emits the `message_completed` story trigger; selecting an option emits `choice_selected`.
- A bounded advancement pass yields safely on long branch cycles and preserves elapsed time for the next host tick.
- The runtime does not implement free-form player replies, live networking, or presentation/typing animations. UI code can render typing indicators using the same authored delay information without changing story state.

The host should serialize the player progress object using a versioned save format. Save serialization and migration are not yet implemented in this library.

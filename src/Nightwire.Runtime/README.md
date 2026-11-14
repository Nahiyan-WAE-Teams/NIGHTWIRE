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

The host should use the versioned `SaveStore` described below. Unity lifecycle integration and cloud synchronization are not yet implemented.


## Versioned save store

`Saves/SaveStore` persists player progress outside the episode package. Construct it with the game's persistent save root, then call `Save(state)` and `Load(episodeId, installedEpisodeVersion)`.

- Save format version 1 uses a typed payload and SHA-256 integrity checksum covering save metadata and exact serialized payload bytes.
- Conversation delivery/read state, selected options, partial delays, story flags, relationships, unlocked assets, completed events, notifications, and pending actions are persisted.
- A completed temporary file is flushed and atomically moved into place. The previous committed save is retained as `save.json.bak`.
- If the primary save is invalid, loading attempts the last-known-good backup, restores it to the primary path, and reports `RecoveredFromBackup`.
- Reads and writes enforce configurable file and collection limits; episode IDs are restricted before being used as directory names.
- Episode content version mismatches are rejected unless explicit `IEpisodeProgressMigration` steps are registered. Migrations run in order and may not change episode identity.
- Unknown save-format versions are rejected rather than guessed or silently rewritten.

The checksum detects accidental corruption; it is not an authenticity signature and does not protect against a hostile device owner modifying local files. The store is synchronous and expects the host to serialize concurrent access to a given episode save. Cloud sync, encryption/key management, cross-device conflict resolution, and save-format migrations remain future work.


## Runtime session coordinator

`Sessions/RuntimeSessionCoordinator` connects the verified package loader, player progress, story event engine, conversation runtime, and local save store into one portable session boundary.

- `StartNewSession` verifies the package, refuses to overwrite an existing primary or backup save, creates episode-owned progress, processes the authored `episode_started` event, and commits the initial save.
- `ResumeSession` loads the save for the installed episode version, honors explicit migration registrations and backup recovery, then checks that saved active conversations, pending calls/minigames, and message cursors still resolve in the installed content.
- Conversation begin/advance/read/choice operations are coordinated and saved after successful mutations. The host receives newly delivered message IDs and typed story commands to render.
- Call and minigame completion APIs verify the matching pending activity before routing the corresponding story trigger.
- `ProcessExternalTrigger` supports future host-owned gameplay events without allowing episode-authored arbitrary code.
- `SaveNow` is available for host lifecycle checkpoints; `CloseSession` releases the active package/progress references.
- The coordinator is single-session and synchronous. The Unity host should serialize calls on its game thread, invoke `SaveNow` at appropriate lifecycle boundaries, and render returned commands. Downloading, archive extraction, media playback, UI, background scheduling, and cloud synchronization remain outside this assembly.

The runtime test harness now includes session start, entry-event dispatch, resume of conversation progress, and overwrite protection cases. These tests are authored but have not been executed in this environment.


## Prerecorded call runtime

`Calls/CallRuntime` interprets a call as a deterministic timeline while the Unity presentation adapter owns actual audio/video decoding and playback.

- The host starts the call only after the story engine has queued the matching pending call.
- The host advances elapsed milliseconds from its media clock. The runtime emits each authored visual/speaker/overlay cue once, and subtitle entries at their authored start timestamps for the selected locale.
- Call duration is authoritative. Reaching it marks the call complete; the session coordinator then dispatches the matching `call_ended` trigger and persists completion.
- Elapsed time, fired cue IDs, fired subtitle IDs, and completion status are saved so resuming does not replay timeline events.
- The runtime does not decode media, synchronize an actual player clock, implement live calls, or render effects. The Unity adapter must keep the media player and runtime clock aligned and call `AdvanceCallPlayback` from that clock.

## Minigame runtime

`Minigames/MinigameRuntime` evaluates the three schema 0.1 game types without episode-authored executable code:

- `sequence_reconstruction`: validates the submitted ordered symbol sequence against the authored answer.
- `frequency_tuning`: validates a finite input against authored min/max, target, and tolerance.
- `evidence_matching`: validates item IDs and tracks correctly matched authored pairs.
- Attempt limits and optional time limits are enforced by the runtime. Successful solutions dispatch the matching `minigame_completed` story trigger; exhausted or timed-out games do not dispatch success.
- Attempts, elapsed time, sequence input, matched pairs, and solved state are persisted. Presentation, drag/drop controls, audio feedback, and animation remain Unity responsibilities.

The runtime test harness now includes call cue/subtitle timing, call completion, sequence puzzle evaluation, and activity save round-tripping. These tests are authored but have not been executed.

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

The package loader verifies an already-unpacked episode directory and exposes cloned JSON documents plus a typed story-event runtime definition. Archive extraction and staged installation are implemented separately in `Installation/`; streaming download is also implemented. Publisher signature verification, release-catalogue authentication, rendering/UI, media playback, and cloud saves remain outside the runtime core.

The loader performs package-integrity and cross-reference checks but is not a full JSON Schema Draft 2020-12 evaluator. Release validation must still run the package validator/schema layer before installation. The Unity presentation layer must consume typed runtime commands and must never execute arbitrary episode-provided code.

## Runtime tests

A dependency-free console test harness is available:

```bash
dotnet run --project tests/Nightwire.Runtime.Tests
```

The harness currently registers 37 test cases covering story events, package loading, conversations, saves/migrations, prerecorded call timelines, minigames, unlocked asset access, staged ZIP installation/update, ZIP traversal rejection, HTTPS download hash verification, signed release signature/sequence verification, interrupted-install startup recovery, signed release identity mismatch protection, and signed catalogue-to-offline-install orchestration, and per-episode session/install exclusion. Test/build execution remains deferred; no passing result is claimed.

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
- Session-level conversation ticks update partial delay in memory and write a save only when a message delivery boundary is crossed; the host calls `SaveNow` on lifecycle checkpoints to persist partial delay. This avoids disk writes every rendered frame.
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
- Conversation begin/read/choice operations are coordinated and saved after successful mutations. Conversation advance ticks keep partial delay in memory and save on delivery boundaries; hosts checkpoint with `SaveNow` on pause/close. The host receives newly delivered message IDs and typed story commands to render.
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


## Episode asset access

`Assets/EpisodeAssetResolver.cs` resolves stable asset IDs from a loaded episode package for the Unity presentation layer. It distinguishes package presence from player access: an asset must be declared in the manifest and present in `EpisodeProgressState.UnlockedAssetIds` before it can be opened. Story `unlock_asset` actions populate that player-owned set.

The resolver returns an `EpisodeAssetHandle` containing the stable ID, media type, MIME type, relative package path, byte length, and a read-only stream. It rechecks the canonical path, rejects symbolic links/reparse points and path traversal, enforces a configurable per-asset size limit, and recomputes the manifest SHA-256 before returning the stream. The original package file is never rewritten. The host must dispose each handle after media decoding or document viewing.

This resolver does not download assets, extract archives, decode media, render UI, or choose implicit default unlocks. In schema 0.1, assets remain locked until an authored event explicitly unlocks them; future public/default asset policies should be explicit package metadata rather than inferred from folder names.


## Episode download and installation

The portable installation layer is in `Installation/`:

- `EpisodePackageDownloader` streams HTTPS responses to a temporary `.part` file, enforces a configurable byte ceiling, reports byte progress, computes SHA-256, optionally checks a trusted release-catalogue digest, and only publishes the completed archive after validation. The built-in HTTP handler rejects redirects. If a host injects an `HttpClient`, it must configure redirects off as well.
- `EpisodePackageInstaller` accepts ZIP archives, rejects unsafe/duplicate paths, symlinks, unlisted files, missing manifest entries, excessive entry counts, oversized expanded content, and suspicious compression ratios. It extracts entry-by-entry into a staging directory, verifies every manifest-listed file's size and SHA-256, then runs the full episode package loader before publishing.
- Updates use same-volume directory moves: the old complete install is moved to a temporary backup, the validated staging directory becomes the active episode directory, and the backup is removed after success. If publishing fails, the installer attempts to restore the previous directory. This is rollback-capable staging, not a single atomic transaction across both directory moves.

The installer does not contact a server, discover releases, authenticate a publisher, or migrate player saves. The downloader's expected archive hash must come from a trusted release catalogue or signed metadata. Hashes inside an archive's own manifest prove consistency, not publisher authenticity. A signed release catalogue and key-rotation/revocation policy are required before production distribution. The application must not replace an episode while a runtime session is actively reading its files; the Unity host owns that lifecycle coordination.

The archive is a ZIP containing `episode.json`, `manifest.json`, and exactly the files declared by the manifest. `episode.json` must itself be listed and hashed by `manifest.json`; `manifest.json` is not self-listed.


## Signed release catalogues and startup recovery

`Installation/ReleaseCatalogueVerifier.cs` verifies a detached release catalogue envelope. The envelope carries `key_id`, fixed algorithm identifier `ECDSA-P256-SHA256`, Base64 `payload` bytes, and a Base64 IEEE-P1363 ECDSA signature. The signature covers the exact payload bytes; the verifier does not reserialize JSON before verification. Trusted P-256 public keys are supplied by the host from app-controlled configuration and are never accepted from the downloaded envelope.

The signed payload schema version 1 contains a monotonically increasing `sequence`, `issued_utc`, `expires_utc`, and release entries with episode ID/version, HTTPS archive URL, exact archive byte count, SHA-256, and minimum runtime version. Verification checks key trust, signature, duplicate/unknown JSON properties, schema version, time window, sequence floor, unique release identities, HTTPS URLs, sizes, SHA-256 digests, and numeric minimum-runtime compatibility. `ReleaseCatalogueAcceptanceService` coordinates verification with an `IReleaseCatalogueSequenceStore`, advancing the stored sequence only after signature and payload validation. The host supplies a durable platform-backed store (prefer protected storage such as Android Keystore-backed preferences) and serializes refresh operations across processes; the runtime does not claim that a plain editable file prevents local rollback. `EpisodePackageDownloader.DownloadReleaseAsync` consumes a verified release entry and checks both its signed size and SHA-256 before publishing the downloaded archive.

Key rotation requires a deliberate trust update: ship the next public key in an app update or introduce a separately specified root-signed keyset protocol. Never accept a replacement key merely because it appears in a catalogue signed by an untrusted key. The signing private key must remain in release infrastructure, not the client or repository.

Call `EpisodePackageInstaller.RecoverInterruptedInstalls` once at app startup, before opening episode sessions or beginning another install. It removes abandoned staging directories and restores the newest retained backup when an episode destination is missing. If the new destination already exists, obsolete backups are cleaned. Recovery reports warnings rather than deleting backups when a destination is unsafe. This is idempotent startup repair for the installer's two-move update sequence; the host must ensure no install is concurrently running.


## Episode release manager

`Installation/EpisodeReleaseManager.cs` is the portable orchestration boundary for the episode library:

- `HttpReleaseCatalogueTransport` fetches a bounded HTTPS catalogue envelope and rejects redirects. A custom transport must enforce the same HTTPS, size, and redirect policy.
- `EpisodeReleaseManager.RefreshCatalogueAsync` passes exact envelope bytes to the signed-catalogue acceptance service. The in-memory catalogue is replaced only after signature, expiry, schema, and rollback-sequence checks succeed.
- `GetLibrary` discovers installed episode directories and re-loads each package to determine whether it is valid and playable offline. It distinguishes available, installed/offline, update available, installed-but-unlisted, and invalid installs.
- `InstallOrUpdateAsync` downloads only a release from the accepted catalogue, checks signed archive size and SHA-256, checks the host runtime version, obtains an episode install lease, and installs the exact signed episode ID/version. The installer checks that identity while the archive is still staged, before replacing the prior install.
- Concurrent operations for the same episode are rejected. Progress and cancellation flow through the downloader.
- `RecoverAtStartup` must run before sessions and installs are opened.

The host must implement `IEpisodeInstallLeaseProvider` so lease acquisition is atomic with session start/close; a simple check of `RuntimeSessionCoordinator.IsActive` is not sufficient to prevent a race. The host also supplies a numeric runtime version, trusted release keys, protected sequence storage, persistent paths, and lifecycle ownership. The manager does not render UI, auto-update in the background, delete player saves, or permit replacing an episode while it is in use. Catalogue state is currently in memory and must be refreshed after app launch; offline play relies on already installed, locally verified packages.


## Episode library presentation boundary

`Installation/EpisodeLibraryPresentation.cs` exposes immutable UI snapshots, catalogue state, per-episode status, download byte progress, cancellation, and install/update operations without depending on Unity. Construct it on the Unity main thread so its `Progress<T>` and `Changed` callbacks return to that synchronization context. A UI host should subscribe while enabled and unsubscribe while disabled; it owns the presentation model lifecycle.

`Installation/EpisodeLibraryPresentation.cs` implements the manager's install lease provider and also exposes session leases. The host must acquire a session lease before starting/resuming `RuntimeSessionCoordinator`, hold it until after `CloseSession`, then dispose it. Pass the same coordinator instance to `EpisodeReleaseManager`. This serializes session-open and install-open operations atomically within one process.

Unity UI Toolkit integration source and setup notes are under `unity/Assets/Nightwire` and `docs/architecture/UNITY_EPISODE_LIBRARY_UI.md`. The view raises `PlayEpisodeRequested`; the host remains responsible for starting/resuming gameplay. Unity Editor import, runtime assembly dependency integration, play-mode validation, and Android IL2CPP verification have not been completed.

The release manager configures its default package loader with the host-supplied runtime version and shares that loader with its default installer. If a host injects a custom installer, it must also inject the matching loader configured for the same runtime version; mismatched custom dependencies are rejected at construction.

# NIGHTWIRE messenger screen

## Scope

The first messenger screen is a Unity UI Toolkit presentation adapter over the portable episode runtime. It is intentionally data-driven: the controller contains no episode-specific dialogue, contact names, or choice labels.

## Files

- `unity/Assets/Nightwire/UI/MessengerScreen.uxml` — screen structure.
- `unity/Assets/Nightwire/UI/MessengerScreen.uss` — dark messenger visual system and responsive message layout.
- `unity/Assets/Nightwire/Scripts/MessengerScreenController.cs` — conversation rendering and player choice interaction.
- `unity/Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — switches between archive and messenger after an episode opens.
- `unity/Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — owns the active session and its install-exclusion lease.

## Runtime flow

1. The player chooses an installed episode in the archive.
2. `EpisodeSessionHost` acquires the episode session lease, verifies the package, then resumes a save or starts a new session.
3. The entry event selects the opening conversation through its typed `open_conversation` action.
4. `MessengerScreenController` binds to the active session and calls `BeginConversation` if conversation progress does not exist yet.
5. Each Unity frame advances the portable conversation clock using elapsed frame time. The portable runtime delivers messages only when authored delays have elapsed.
6. The controller renders delivered messages, localizes their text keys, and marks them read. This routes the `message_completed` trigger back through the story engine.
7. When a choice message is waiting, the controller creates buttons from its authored options. Selecting one calls `SelectChoice`, which applies branch targets, story flags, and relationship deltas in the portable runtime.
8. The back-to-archive action saves and closes the session, releases its lease, and returns to the episode library.

## Content mapping

| Episode message type | Current presentation |
|---|---|
| `text`, `system` | Localized text bubble |
| `choice` | Localized prompt and authored choice buttons |
| `image` | Inline preview if the asset is unlocked, manifest-listed, hash-verified, and below the inline size limit |
| `audio`, `video`, `document` | Identified attachment placeholder |
| `call_invitation` | Incoming-call placeholder with call ID |

Character names and status are resolved from each character profile's localization keys. The episode title is resolved from the package descriptor. The locale is the episode default locale until a player language preference is wired into settings.

## Performance and persistence

Conversation delay ticks update elapsed delay in memory and do not write the save file on every rendered frame. A save is committed when a message delivery boundary is crossed. The application root calls `SaveCheckpoint` on `OnApplicationPause(true)`; leaving the messenger and normal application quit call `SaveAndClose`, which runs `SaveNow` before `CloseSession`. `CloseSession` itself deliberately does not save. Its `SessionClosed` notification is for host cleanup (including releasing the episode lease), not a final checkpoint.

The runtime serializes checkpoint commits against closure: if closure wins before a captured checkpoint reaches the commit gate, that stale checkpoint is rejected; if a synchronous save has already entered the gate, it completes before closure. The coordinator still requires all gameplay mutations to be serialized on Unity's main thread. A process can be terminated without a final lifecycle callback, so the host relies on the runtime's story-boundary saves as well as pause/close checkpoints; no callback-based design can promise a final save after abrupt termination.

Inline image textures are cached for the duration of the screen session and destroyed when the messenger unbinds. The image reader uses the runtime's asset resolver, which checks the asset's unlock state, manifest membership, path, size, and SHA-256 before returning a stream.

## Explicitly not implemented yet

- Audio message playback and waveform UI.
- Video message playback and full-screen viewer.
- Prerecorded call screen and synchronized subtitles/effects.
- Character profile and social feed.
- Media collection/gallery.
- Group-chat participant header and multi-avatar layout.
- Free-form player replies (the v0.1 package contract currently uses authored choices).
- Push notification and unread conversation inbox.
- Complete Unity project, scenes, Panel Settings, and verified device build.

These are separate presentation features. The episode package/runtime contract remains the source of story data and does not require a new app release for content using already-supported message types.


## Social feed and evidence tabs

The messenger presentation exposes three in-session tabs: Messages, Social Feed, and Evidence. The social feed is rendered from verified `CharacterPosts` in the loaded episode package, localizes author/body/comment keys, and resolves image attachments through the same unlocked-asset resolver used by chat messages. It does not fetch external social content.

The Evidence tab enumerates only assets explicitly unlocked in player progress. Images use the verified image cache; audio/video use the bounded verified-media playback path; text, Markdown, and JSON documents may be previewed only after opening the manifest-verified asset and enforcing a 1 MiB preview limit. Other document formats (including PDF and office documents) remain identified as verified archive attachments rather than being rendered as if a viewer existed. All authored episode files remain immutable.


## Group conversation presentation

Conversation headers inspect the complete participant list rather than treating the first non-player participant as the only contact. Multi-contact conversations receive a Group Chat header and a localized participant roster; each incoming message retains its sender label. The profile shortcut opens the first listed member's profile. Conversation delivery, choices, and progress remain on the same shared conversation runtime schema.

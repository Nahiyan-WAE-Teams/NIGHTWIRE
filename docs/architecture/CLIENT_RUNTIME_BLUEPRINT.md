# NIGHTWIRE Client Runtime Blueprint

- Status: Architecture draft
- Target: Unity mobile client
- Runtime language: C#
- Episode contract: v0.1 (draft; evolves with validator)
- Purpose: define reusable client systems before implementing individual screens.
- Initial code foundation: [portable C# runtime core](../../src/Nightwire.Runtime/README.md).

## 1. Runtime boundaries

The client has four layers:

1. **Presentation:** Unity scenes, prefabs, UI components, animation, audio/video presentation, accessibility.
2. **Application:** navigation, commands, notifications, story progression, media unlocks, player actions.
3. **Domain:** episode, character, conversation, message, call, minigame, event, evidence, and save models.
4. **Infrastructure:** package installation, JSON loading, manifest verification, local file access, persistence, optional cloud sync.

Presentation components must not parse episode files directly. Infrastructure must not decide story outcomes. Domain models must not depend on Unity scene objects.

## 2. Startup and episode lifecycle

1. Boot the application and load player preferences/save index.
2. Read the trusted local episode catalogue.
3. Show installed episodes and available episode downloads.
4. For a selected episode, check runtime compatibility and installed package integrity.
5. Open the episode through the domain loader only after package validation succeeds.
6. Restore the episode save and resume its current actionable state.
7. If not installed, download to a temporary location, verify release metadata and manifest, then atomically install.
8. Once installed, story playback and media access must not require a network connection.

The installer must reject archive path traversal, symlinks/reparse points, duplicate normalized paths, unexpected files, and size-limit violations. Package authenticity requires trusted signed release metadata; per-file SHA-256 alone only detects accidental corruption.

## 3. Core application modules

| Module | Responsibility | Episode data |
|---|---|---|
| Episode Catalog | Installed/available episode list and compatibility | Release catalogue, episode descriptor |
| Package Installer | Download, verify, stage, install, update and remove packages | Release metadata, manifest |
| Story Runtime | Evaluate allowlisted triggers/actions and persist progression | Events, entry event, story flags |
| Messenger | Timeline, direct/group conversations, delivery order, choices | Conversation/message records |
| Character Directory | Character list, profile, interests, links and posts | Character profiles, posts |
| Media Archive | Received media index, filtering, locked/unlocked state | Asset metadata and unlock state |
| Call Player | Prerecorded audio/video, subtitles, timed cues | Call definitions and media assets |
| App Launcher | In-game phone-style app navigation | Runtime capabilities; optional episode app config |
| Notification Router | Unread/actionable indicators and deep navigation | Event results and player state |
| Minigame Host | Dispatch to runtime-implemented puzzle types | Minigame definitions |
| Evidence Board | Review and connect unlocked evidence | Evidence records and relationship state |
| Player Profile | Player identity, avatar, preferences, achievements | Player save; selected profile cosmetics |
| Save Service | Versioned local state, checkpointing, migration | Runtime save schema |

## 4. Navigation model

Use a persistent phone-like application shell rather than one Unity scene per feature. The shell owns the current app, navigation stack, global notification state, and modal presentation.

Initial destinations:
- Messenger
- Contacts
- Media
- Calls
- Evidence
- Player Profile
- Settings

A story event can request a typed navigation command (for example, open a conversation, show a notification, or start a call). It must not name arbitrary Unity scenes, GameObjects, methods, or scripts.

## 5. Messenger behavior

The messenger renders a chronological timeline from typed message records:
- Text, image, video, audio, document, system, choice, and call invitation.
- Optional authored delay/typing presentation.
- Message delivery/read state in the player save.
- Choice selection and next-message routing.
- Direct and group participants.
- Media opened from chat can be revisited through the Media Archive when unlocked.

The engine owns timing and state. The UI only renders the current projection and sends typed player commands.

## 6. Character profiles and posts

Profile screens combine immutable authored data with mutable player state:
- Localized display name, biography, status, avatar, interests, and favorite links.
- Ordered authored posts and comments.
- Post media references.
- Optional profile/background cosmetics for the player.

The player may edit their own profile where supported. Those edits belong in save data, never in the downloaded episode package.

## 7. Media archive

The archive is an index, not a second copy of media files. Each entry records:
- Stable asset ID and type.
- Owning episode and optional source conversation/call/post.
- Localized title/caption keys and authored timestamp.
- Unlock/received state.
- Manifest path and integrity metadata.

Only unlocked/received items are exposed to the player. Original media remains unchanged. Large media should load on demand and be released when the viewer closes.

## 8. Prerecorded call playback

A call invitation opens a dedicated call surface. The call player:
- Resolves local media by asset ID.
- Plays audio/video using Unity-supported platform playback.
- Synchronizes localized subtitles against media time.
- Dispatches allowlisted visual/audio cues.
- Supports pause/resume and interruption recovery.
- Emits one completion command/event when playback finishes.
- Releases decoder, texture, and audio resources on close.

Calls are prerecorded episode content, not live VoIP. All required media must be installed before offline playback begins.

## 9. Story event execution

The story runtime accepts only schema-approved trigger/action types. It:
- Evaluates triggers from explicit runtime facts.
- Resolves IDs against the loaded episode.
- Applies state changes through a transaction/checkpoint boundary.
- Prevents duplicate completion events where the event is one-shot.
- Persists flags/unlocks/progression before presenting the next state.
- Rejects unsupported actions rather than attempting reflection or dynamic code execution.

No episode file may contain C#, Lua, JavaScript, arbitrary expressions, or Unity object references.

## 10. Minigame host and accessibility

The host dispatches a minigame definition to a built-in implementation selected by its type. Initial candidates:
- Frequency tuning
- Sequence reconstruction
- Evidence matching

Each implementation must support:
- Non-color-only visual distinctions.
- Clear labels/shapes/symbols and contrast.
- Touch targets appropriate for mobile.
- Optional hints and comprehensible error feedback.
- Reduced-motion preference.
- Save-safe start, retry, completion, and interruption behavior.

## 11. Save model

Keep authored episode data and player state separate.

The save envelope should include:
- Save schema version.
- Runtime compatibility version.
- Episode ID and installed episode version.
- Current event/conversation/message position.
- Story flags and relationship values.
- Selected choices and message delivery state.
- Unlocked media/evidence/achievements.
- Player profile edits and preferences.
- Active minigame state where safe to resume.
- Last committed checkpoint timestamp.

Writes should be atomic (write temporary, flush, replace) and recover from interrupted writes. Migrations must be explicit and tested. Cloud sync is optional and must not block local progress.

## 12. Performance and lifecycle

- Load only the active conversation/profile/media view.
- Cache small metadata; stream large media from local files.
- Pool repeated message cells.
- Avoid decoding every image/video at episode startup.
- Release video textures, audio sources, and large textures when no longer needed.
- Handle app pause/resume and Android process recreation.
- Respect OS back navigation and safe areas.
- Provide reduced-motion and text scaling preferences.

## 13. Implementation order

1. Runtime domain models and JSON loading.
2. Package index and safe local asset resolver.
3. Versioned local save service.
4. Story event executor and deterministic progression tests.
5. Phone shell and navigation.
6. Messenger timeline and choice handling.
7. Character profiles/posts and Media Archive.
8. Prerecorded call player.
9. App launcher, notification routing, evidence surfaces.
10. Minigame host and first puzzle implementation.
11. Package download/install/update UI.
12. Accessibility, lifecycle, performance, and end-to-end tests.

Do not build all screens as disconnected mockups. Each UI surface must be backed by the same domain state and navigation commands.


## 14. First Unity presentation integration

The first Unity-facing implementation is the UI Toolkit archive screen:
- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uxml` defines the named screen hierarchy.
- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uss` defines the dark archive visual system and restrained 140 ms interactions.
- `unity/Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` binds the view to the presentation model.
- `src/Nightwire.Runtime/Installation/EpisodeLibraryPresentation.cs` provides UI-neutral snapshots, progress, cancellation, and the atomic `EpisodeAccessCoordinator` session/install gate.

The screen renders installed, downloadable, update-available, unlisted, and invalid episode states. It refreshes the signed catalogue, starts verified downloads, displays byte progress, supports cancellation, and exposes `PlayEpisodeRequested` so the application shell can resume or start a session without coupling the view to save policy.

The composition root owns the release manager and presentation model. It must recover interrupted installs before exposing gameplay, provide trusted-key and protected-sequence adapters, and use the same `EpisodeAccessCoordinator` for session starts and installs. A session lease must remain held until the runtime session has saved and closed.

This is a source-level integration component only. A Unity project manifest, ProjectSettings, Panel Settings asset, scene, runtime DLL and dependency import, Android secure-storage adapter, and device verification are still outstanding. See [Unity integration notes](../../unity/README.md) and the [detailed UI integration guide](UNITY_EPISODE_LIBRARY_UI.md).

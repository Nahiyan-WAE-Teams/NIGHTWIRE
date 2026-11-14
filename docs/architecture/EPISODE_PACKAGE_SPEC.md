# NIGHTWIRE Episode Package Specification

- Status: Draft
- Format version: 0.1
- Last updated: 2026-11-14
- Scope: authored episode content and its relationship to runtime/player data

This document defines the first proposed package contract. It is deliberately a content format, not executable game code. It must be reviewed and tested with sample packages before the runtime loader is implemented.

## 1. Design goals

1. An episode is a self-contained, independently downloadable content package.
2. A compatible installed runtime can load a new episode without an application update.
3. A fully downloaded episode can play offline.
4. Story content is data; C# remains in the application runtime.
5. Every media reference resolves through a stable asset ID.
6. Authored package files are immutable during play.
7. Player progress is stored outside the episode package.
8. Package and schema versions are explicit and validated before content is loaded.

## 2. Package layout

Proposed unpacked layout:

```text
Episode_001/
├── episode.json
├── manifest.json
├── Characters/
│   ├── Player/
│   │   └── profile.json
│   ├── Anisha/
│   │   ├── profile.json
│   │   ├── Posts/
│   │   └── Conversations/
│   └── Anna/
│       ├── profile.json
│       ├── Posts/
│       └── Conversations/
├── GroupChats/
├── Events/
├── Calls/
├── Media/
│   ├── Images/
│   ├── Audio/
│   ├── Video/
│   └── Documents/
└── Localization/
    └── en.json
```

The package may be distributed as a ZIP-like archive or another transport container. The unpacked logical layout and JSON contracts are independent of the transport format.

Character-owned media may be placed under a character folder when it is truly private to that character. Shared assets belong in the episode-level Media directory. The manifest indexes all assets regardless of physical location.

## 3. episode.json

The episode descriptor defines identity, presentation metadata, compatibility, and entry points.

Example:

```json
{
  "schema_version": "0.1",
  "episode_id": "nightwire.ep001",
  "episode_version": "1.0.0",
  "title_key": "episode.title",
  "description_key": "episode.description",
  "minimum_runtime_version": "0.1.0",
  "entry_event": "event.episode_start",
  "default_locale": "en",
  "supported_locales": ["en"],
  "manifest": "manifest.json"
}
```

Rules:
- episode_id is stable and unique across the game.
- episode_version follows semantic versioning for content revisions.
- minimum_runtime_version is checked before loading.
- User-facing text is resolved through localization keys, not embedded in UI code.
- entry_event must exist in the event definitions.

## 4. manifest.json

The manifest is the authoritative inventory of package files and media assets.

Example:

```json
{
  "schema_version": "0.1",
  "episode_id": "nightwire.ep001",
  "files": [
    {
      "path": "Characters/Anisha/profile.json",
      "kind": "character_profile",
      "size_bytes": 512,
      "sha256": "REPLACE_WITH_REAL_SHA256"
    },
    {
      "path": "Media/Images/photo_001.jpg",
      "kind": "image",
      "size_bytes": 48210,
      "sha256": "REPLACE_WITH_REAL_SHA256"
    }
  ],
  "assets": [
    {
      "asset_id": "anisha.photo_001",
      "type": "image",
      "path": "Media/Images/photo_001.jpg",
      "mime_type": "image/jpeg"
    }
  ]
}
```

Rules:
- Example hashes and sizes above are placeholders, never valid release metadata.
- Paths must be relative, normalized, and remain inside the episode root.
- Reject absolute paths, traversal segments, duplicate paths, duplicate asset IDs, and unsupported file types.
- SHA-256 is used to detect corruption; it does not by itself prove who published a package.
- A future release pipeline should sign package metadata or publish a trusted signed release index.
- The manifest must not attempt to include its own hash. Package-level authenticity is handled by the release metadata/signature layer.

## 5. Character profiles

Character profile files define authored identity and references to their content.

Example:

```json
{
  "schema_version": "0.1",
  "character_id": "anisha",
  "display_name_key": "character.anisha.name",
  "bio_key": "character.anisha.bio",
  "avatar_asset_id": "anisha.avatar",
  "status_key": "character.anisha.status",
  "conversation_ids": ["anisha.day01"],
  "post_ids": ["anisha.post01"]
}
```

The player character's authored defaults may be packaged here, but player-edited name, avatar, and other mutable values belong in the save data.

## 6. Conversation and message model

A conversation is an ordered set of uniquely identified messages. The engine interprets message types supported by its runtime version.

Example:

```json
{
  "schema_version": "0.1",
  "conversation_id": "anisha.day01",
  "participants": ["player", "anisha"],
  "messages": [
    {
      "message_id": "anisha.day01.001",
      "type": "text",
      "sender_id": "anisha",
      "text_key": "chat.anisha.day01.001",
      "delay_seconds": 1.2
    },
    {
      "message_id": "anisha.day01.002",
      "type": "image",
      "sender_id": "anisha",
      "asset_id": "anisha.photo_001"
    },
    {
      "message_id": "anisha.day01.003",
      "type": "choice",
      "sender_id": "anisha",
      "prompt_key": "choice.anisha.day01.003.prompt",
      "options": [
        {
          "option_id": "ask_what",
          "text_key": "choice.anisha.day01.003.ask",
          "next_message_id": "anisha.day01.004"
        },
        {
          "option_id": "stay_quiet",
          "text_key": "choice.anisha.day01.003.quiet",
          "next_message_id": "anisha.day01.005"
        }
      ]
    }
  ]
}
```

Initial proposed message types:
- text
- image
- video
- audio
- document
- choice
- system
- call_invitation

Call sessions should be described in Calls/ and referenced by ID. This keeps call-specific timing, video/audio tracks, and UI behavior out of ordinary text-message definitions.

Rules:
- Every message_id is unique within the episode.
- Every sender_id must identify a declared participant or the system.
- Every asset_id must exist in the manifest.
- Every next_message_id must resolve, or be explicitly null for an end.
- Delays are presentation hints, not authoritative real-time timers.
- The runtime must not execute arbitrary code from episode data.

## 7. Events and story progression

Events describe when authored actions occur. Triggers and actions use a strict allowlist implemented by the runtime.

Example:

```json
{
  "schema_version": "0.1",
  "event_id": "event.episode_start",
  "trigger": {
    "type": "episode_started"
  },
  "actions": [
    {
      "type": "open_conversation",
      "conversation_id": "anisha.day01"
    }
  ]
}
```

Initial trigger candidates:
- episode_started
- message_completed
- choice_selected
- asset_unlocked
- call_ended
- minigame_completed

Initial action candidates:
- open_conversation
- send_message_sequence
- show_notification
- start_call
- unlock_asset
- set_story_flag
- adjust_relationship
- start_minigame

Every trigger and action must be schema-validated and supported by the runtime. Unknown action types fail validation rather than being silently executed.

## 8. Calls and minigames

Calls are separate definitions referenced by call_invitation messages or events. A call definition may specify:
- call_id
- participants
- presentation mode (audio or video)
- media asset IDs
- subtitle/localization track
- sequence of authored beats
- completion event

Minigames are also separate definitions. Episode data selects a supported minigame type and supplies validated configuration. It cannot supply executable scripts or native plugins.

## 9. Localization

All player-facing authored text should use stable localization keys. Locale files map keys to strings.

Example:

```json
{
  "episode.title": "The First Signal",
  "character.anisha.name": "Anisha",
  "chat.anisha.day01.001": "Are you awake?"
}
```

Missing localization keys must be detected during package validation. The runtime may fall back to the episode's default locale.

## 10. Player save data

Save data is separate from the episode package and is scoped by episode ID.

Proposed logical layout:

```text
GameData/
└── Saves/
    └── nightwire.ep001/
        ├── save_metadata.json
        ├── player_state.json
        ├── story_state.json
        ├── conversations/
        └── unlocked_media.json
```

Save data may contain:
- Player-customized profile values.
- Selected choices and story flags.
- Conversation delivery/read state and player replies.
- Relationship values.
- Unlocked evidence and media.
- Current event and episode progress.
- Save schema version and last-updated timestamp.

Authored episode files must never be modified to record player progress. Save migrations must be explicit and versioned.

## 11. Validation before runtime loading

A package validator must check:
1. JSON syntax and schema versions.
2. Episode identity and runtime compatibility.
3. Required files and manifest completeness.
4. Relative path safety and duplicate paths.
5. File sizes and SHA-256 hashes.
6. Asset IDs and media references.
7. Character, participant, conversation, event, and call references.
8. Localization key coverage.
9. Trigger/action allowlists and graph references.
10. Unsupported message types and feature requirements.

Validation should produce actionable errors with file paths and JSON locations. Invalid packages must not partially enter the playable content store.

## 12. Download and update lifecycle

Proposed lifecycle:
1. Obtain trusted episode release metadata.
2. Check runtime compatibility and available storage.
3. Download to a temporary location.
4. Verify archive structure, manifest, file sizes, and hashes.
5. Validate all schemas and references.
6. Atomically move the verified package into the installed episode store.
7. Mark the episode as available to play offline.

Updates should preserve player saves. A content update must declare its episode version and any required save migration. Interrupted downloads must not replace a previously valid installed package.

## 13. Compatibility policy

- schema_version describes the JSON contract.
- episode_version describes revisions to a particular episode.
- minimum_runtime_version declares the oldest compatible application runtime.
- New optional fields may be introduced compatibly.
- New required fields or changed semantics require a schema version change.
- The runtime must reject unsupported required features with a clear message.

## 14. Open decisions before implementation

These decisions remain intentionally unresolved:
- Whether players can type arbitrary free-form messages or only select authored responses.
- Whether group chats use the same conversation schema or a specialized wrapper.
- Exact archive/container format and compression settings.
- Whether optional cloud saves are included in the first release.
- Exact minigame configuration contracts.
- Whether episode updates can migrate existing save data automatically.

Do not implement assumptions for these open decisions without recording and reviewing the decision first.


## 15. Reference JSON Schemas and sample package

Draft JSON Schema files for format 0.1 are stored in `schemas/0.1/`:
- `episode.schema.json`
- `manifest.schema.json`
- `character-profile.schema.json`
- `conversation.schema.json`
- `event.schema.json`
- `localization.schema.json`

A small linked example package is stored in `examples/Episode_001/`. Its manifest includes the byte sizes and SHA-256 digests of each listed file; the manifest does not hash itself. The example intentionally has no media assets yet, so its asset list is empty.

These schemas validate individual document shapes. They do not replace package-level semantic validation. A future validator must additionally check cross-file references, unique IDs across the episode, localization key coverage, manifest completeness, asset-to-file correspondence, and the event/message graph.

See [JSON Schema Guide](JSON_SCHEMA_GUIDE.md) for the schema mapping and current validation boundary.

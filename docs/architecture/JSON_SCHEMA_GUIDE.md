# JSON Schema Guide

- Status: Draft
- Format version: 0.1

## Schema files

| Schema | Validates |
|---|---|
| `episode.schema.json` | Episode identity, version compatibility, locale declarations, entry event |
| `manifest.schema.json` | Package file inventory, hashes, and asset records |
| `character-profile.schema.json` | Character identity and profile references |
| `character-post.schema.json` | Authored character social posts and comments |
| `call.schema.json` | Prerecorded audio/video call media, subtitles, and timed presentation cues |
| `minigame.schema.json` | Declarative configuration for supported puzzle types |
| `conversation.schema.json` | Participants, message variants, choices, and asset references |
| `event.schema.json` | Supported event trigger and action shapes |
| `localization.schema.json` | Locale key/value dictionaries |
| `release-catalogue.schema.json` | Signed release catalogue payload metadata and archive references |

All schemas use JSON Schema Draft 2020-12. They are draft contracts and may change before the first production episode is authored.

## Example

The linked sample is in `examples/Episode_001/`. Start with `episode.json`, follow its `manifest.json`, then inspect the character profile, conversation, event, and English localization file.

The sample is intentionally small and contains no media assets. Its manifest records the byte size and SHA-256 digest of each package file except the manifest itself.

## Validation boundary

The Episode Validator evaluates these Draft 2020-12 schemas for the descriptor, manifest, and every typed document, then performs package-wide integrity and semantic checks. JSON Schema checks document shape and field constraints; it cannot, by itself, establish all package-wide relationships. The package validator additionally checks:

- Every manifest path exists, is unique, and stays within the package root.
- Every package file required by the format is represented in the manifest.
- File sizes and SHA-256 digests match actual bytes.
- Every asset ID is unique and its path maps to a manifest file.
- Every character, post, conversation, event, call, minigame, and message reference resolves.
- Call media, subtitle timing, participants, and completion events are consistent.
- Minigame configurations match their declared type and completion event.
- Post authors, comment authors, and media assets resolve.
- Message IDs and option IDs are unique in their defined scopes.
- All localization keys referenced by episode content exist in the declared locales.
- The entry event exists and event/message progression references are valid.
- Runtime-required feature declarations are supported by the target runtime.

Unknown trigger, action, or message types must fail validation. Episode data is declarative content; it must never be evaluated as C#, JavaScript, or another executable language.


## Command-line package validation

The repository includes a .NET 10 command-line validator at `tools/Nightwire.EpisodeValidator`, using `JsonSchema.Net` 9.7.0 for Draft 2020-12 evaluation.

Run it from the repository root:

```bash
dotnet run --project tools/Nightwire.EpisodeValidator -- examples/Episode_001
```

It evaluates the checked-in schemas for the descriptor, manifest, character profiles/posts, conversations, events, calls, minigames, and localization dictionaries. Format validation is enabled. It then verifies file inventory, byte sizes, SHA-256 digests, asset references, cross-document references, message flow, and localization coverage. The CLI locates `schemas/0.1` by walking upward from the episode directory, current working directory, and executable directory; run it from the repository checkout.


## Runtime loading boundary

The runtime loader is implemented in `src/Nightwire.Runtime/Loading/EpisodePackageLoader.cs`. It verifies package inventory, sizes, SHA-256 hashes, path safety, and selected cross-document references before creating runtime indexes. The portable runtime loader retains these security-critical checks independently; full Draft 2020-12 schema evaluation is performed by the release validator before publishing episode packages. Signed release-catalogue verification is implemented in `src/Nightwire.Runtime/Installation/ReleaseCatalogueVerifier.cs`; the app must provision trusted public keys and persist the highest accepted catalogue sequence. Production signing infrastructure and key rotation remain release-engineering responsibilities.

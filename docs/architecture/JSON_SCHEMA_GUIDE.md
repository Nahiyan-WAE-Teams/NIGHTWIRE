# JSON Schema Guide

- Status: Draft
- Format version: 0.1

## Schema files

| Schema | Validates |
|---|---|
| `episode.schema.json` | Episode identity, version compatibility, locale declarations, entry event |
| `manifest.schema.json` | Package file inventory, hashes, and asset records |
| `character-profile.schema.json` | Character identity and profile references |
| `conversation.schema.json` | Participants, message variants, choices, and asset references |
| `event.schema.json` | Supported event trigger and action shapes |
| `localization.schema.json` | Locale key/value dictionaries |

All schemas use JSON Schema Draft 2020-12. They are draft contracts and may change before the first production episode is authored.

## Example

The linked sample is in `examples/Episode_001/`. Start with `episode.json`, follow its `manifest.json`, then inspect the character profile, conversation, event, and English localization file.

The sample is intentionally small and contains no media assets. Its manifest records the byte size and SHA-256 digest of each package file except the manifest itself.

## Validation boundary

JSON Schema checks document shape and field constraints. It cannot, by itself, establish all package-wide relationships. The future package validator must also check:

- Every manifest path exists, is unique, and stays within the package root.
- Every package file required by the format is represented in the manifest.
- File sizes and SHA-256 digests match actual bytes.
- Every asset ID is unique and its path maps to a manifest file.
- Every character, conversation, event, call, minigame, and message reference resolves.
- Message IDs and option IDs are unique in their defined scopes.
- All localization keys referenced by episode content exist in the declared locales.
- The entry event exists and event/message progression references are valid.
- Runtime-required feature declarations are supported by the target runtime.

Unknown trigger, action, or message types must fail validation. Episode data is declarative content; it must never be evaluated as C#, JavaScript, or another executable language.


## Command-line package validation

The repository includes a dependency-free .NET 10 command-line validator at `tools/Nightwire.EpisodeValidator`.

Run it from the repository root:

```bash
dotnet run --project tools/Nightwire.EpisodeValidator -- examples/Episode_001
```

It verifies file inventory, byte sizes, SHA-256 digests, asset references, character/conversation/event references, message flow, and localization coverage. It does not yet run the full JSON Schema Draft 2020-12 evaluator; structural schema validation remains a separate step until a schema-validation library and its compatibility are selected.

# NIGHTWIRE automated tests

These tests exercise the .NET episode validator against a copied sample package and intentionally corrupted variants.

## Requirements

- Python 3.10+
- .NET 10 SDK

## Run

From the repository root:

```bash
python -m unittest discover -s tests -v
```

The Python integration suite builds the validator in Release configuration once, then checks:
- the sample package passes schema, integrity, and semantic validation;
- a modified file fails SHA-256 integrity validation;
- a descriptor missing a required schema property is rejected even when its manifest hash is refreshed;
- a choice pointing to a missing message fails semantic validation;
- a call invitation pointing to a missing call fails semantic validation;
- a minigame sequence containing an undeclared symbol fails semantic validation;
- a post whose author does not exist fails semantic validation;
- a path-traversal manifest entry is rejected.

The tests use only Python's standard library. They do not run GitHub Actions; CI remains deferred until the implementation and review milestone is reached.

## C# runtime tests

The portable runtime test harness covers story-event execution, package loading, conversation progression, versioned save persistence, including process-wide per-episode serialization, concurrent non-overwriting new-game creation, checkpoint-versus-close serialization, and rejection of stale progress after session closure, linked restore/backup temporary rejection, and stale temporary-file recovery, signed catalogue signature/rollback checks, duplicate JSON property and duplicate episode identity rejection, malformed minimum-runtime version rejection, explicit timestamp-offset enforcement, rejection of symlinked package, save, install, and download directory ancestors, cancellation-safe release-manager disposal during active and queued catalogue refreshes, including late transport completion and acceptance-vs-shutdown serialization before sequence persistence, and ZIP Unix/Windows link-entry rejection (including directory-shaped link entries), FIFO/special-file rejection, ZIP file/directory path collision rejection, and followed-redirect rejection for downloads and catalogue fetches:

```bash
dotnet run --project tests/Nightwire.Runtime.Tests
```

It verifies that the sample Episode 001 loads, modified files fail integrity checks, unlisted files are rejected, older runtimes are rejected, authored message delays are respected, read state is persisted separately, selected choices follow the correct branch, authored choice flags/relationship deltas persist, save state round-trips, corrupt primary saves recover from backup, episode identities remain isolated, and explicit episode migrations run, and modified save metadata is detected. These tests require the .NET 10 SDK and have not yet been executed in this development environment.

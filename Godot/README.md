# NIGHTWIRE Godot Edition

A separate GDScript implementation of NIGHTWIRE for Android. This project lives beside the Unity project and does not modify or depend on Unity files.

## Pinned toolchain

- Godot **4.7.2-stable** (standard, non-.NET build)
- GDScript
- Android export templates matching Godot 4.7.2
- Android SDK / JDK versions to be pinned in the dedicated Godot CI workflow after validating the official Godot 4.7.2 export requirements

Do not use Godot 4.8 beta for this port. The official archive lists 4.7.2 as stable and 4.8 as beta at the time this branch was created.

## Product requirement

This is a full NIGHTWIRE game port, not a sample project or a placeholder APK. Preserve the existing episode format and player-facing game design. Port all supported gameplay and presentation systems from the Unity implementation, and explicitly implement the Unity presentation gaps listed in `PORTING_STATUS.md`.

## Isolation

- Unity project remains under `unity/` and is not edited by this branch's Godot work.
- Godot source is confined to `Godot/`.
- Godot Android CI is a separate workflow.
- Do not merge this branch into `main` without the repository owner's explicit approval.

## Architecture direction

- Keep the existing JSON episode package contract and sample Episode 001 as the content source.
- Implement the runtime and presentation in GDScript; do not add a .NET dependency.
- Keep authored episode files immutable and player progress separate.
- Verify package manifests, file sizes, SHA-256 digests, references, and safe paths before exposing episode content.
- Keep UI code as a presentation layer; episode content must never execute arbitrary code.
- Persist conversation, choice, story, call, minigame, and unlock progress independently from episode assets.
- Use Godot-native scenes, Control nodes, resources, signals, audio/video players, and Android export.

## Planned structure

```
Godot/
  project.godot
  scenes/
  scripts/
    app/
    runtime/
    content/
    presentation/
    platform/
  data/
  tests/
  export/
  PORTING_STATUS.md
```

The structure will be populated as each complete system is implemented and reviewed. No placeholder scene is presented as a completed game.

## Build policy

Implementation and source review come before the first Godot CI run. CI will build the full Android project and run the Godot-side automated tests; a successful CI artifact must still be installed and smoke-tested on a physical Android device.

# NIGHTWIRE Unity project

The repository now contains a Unity 6.3 project scaffold, an editor-generated main scene, the episode archive and messenger UI Toolkit screens, an offline sample-episode bootstrap, and a separate Game UI source CI pipeline.

**Pinned editor:** Unity 6000.3.28f1. Do not use earlier 6000.3 patches; this is the patched LTS baseline selected after reviewing Unity's November 2026 security update.

## First-time setup

1. From the repository root, run `tools/prepare-unity-runtime.sh` (macOS/Linux) or `tools/Prepare-UnityRuntime.ps1` (Windows). This publishes the portable .NET Standard 2.1 runtime and stages `Nightwire.Runtime.dll`, its resolved dependencies, and `link.xml` into `unity/Assets/Plugins/Nightwire/`.
2. Open the `unity/` folder as a project in Unity **6000.3.28f1**.
3. The editor bootstrapper creates `NightwirePanelSettings.asset`, generates `NightwireMain.unity`, adds it to Build Settings, and packages `examples/Episode_001` into `Assets/StreamingAssets/Nightwire/Episode_001.zip`.
4. Select the generated `NightwireMain` scene and press Play. The bundled sample episode is installed into the app's persistent episode library on first launch. It is an offline development seed; it does not use the remote signed-catalogue path.
5. Keep the generated scene and Panel Settings asset under version control after Unity has imported them and created their `.meta` files.

The editor bootstrapper uses `NightwireDevelopmentPlatformServices`. This provider is deliberately offline, uses a local development sequence file, and is disabled in non-development players. It is for the sample scene only.

## Production Android configuration

Before creating a production Android build:

- Replace `NightwireDevelopmentPlatformServices` on the application root with `AndroidNightwirePlatformServices`.
- Configure the HTTPS release-catalogue URL and app-provisioned ECDSA P-256 public keys.
- Build `unity/Android/NightwireSecureStore` as an AAR and import it under `Assets/Plugins/Android/`.
- Preserve the StrongBox backup exclusions from the AAR manifest in the final merged Unity manifest.
- Use Android API 28+ and a StrongBox-capable device. Unsupported devices fail closed; there is no plaintext/software-key fallback.
- Keep the bundled sample seeder as a development-only feature or remove its component from the production scene. Production episode delivery must use the signed release catalogue and verified downloader/installer.
- Do not ship the development provider or an unsigned local episode seed as the production content-delivery mechanism.

## Unity and Android toolchain

- Unity Editor: **6000.3.28f1**.
- Gradle: **9.3.1**.
- Android Gradle Plugin: **9.1.1**.
- Android SDK Build Tools: **36.0.0**.
- Android NDK: **r27c**.
- Android JDK: **17**.

These versions follow Unity's official 6000.3 compatibility tables. The editor patch also includes the November 2026 security fixes.

## UI source layout

- `Assets/Nightwire/UI/EpisodeLibraryView.uxml` and `.uss` — dark episode archive.
- `Assets/Nightwire/UI/MessengerScreen.uxml` and `.uss` — conversation interface, message bubbles, image previews, and choice dock.
- `Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — archive presentation adapter.
- `Assets/Nightwire/Scripts/MessengerScreenController.cs` — package-driven text, choices, verified images, and verified audio/video playback.
- `Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — composition root, startup recovery, and session lifecycle.
- `Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — resume/new-session selection and per-episode session lease.
- `Assets/Nightwire/Scripts/AndroidNightwirePlatformServices.cs` — production Android release configuration and StrongBox bridge.
- `Assets/Nightwire/Scripts/NightwireDevelopmentPlatformServices.cs` — offline development-only provider.
- `Assets/Nightwire/Scripts/NightwireBundledEpisodeSeeder.cs` — first-run sample episode installer.\n- `Assets/Nightwire/Scripts/NightwireAtmosphereController.cs` — three procedural fog layers that drift independently across both archive and messenger screens.
- `Assets/Editor/NightwireProjectBootstrapper.cs` — generates the initial scene, panel settings, and bundled sample archive.
- `Assets/Nightwire/Scripts/Nightwire.Unity.asmdef` — presentation assembly boundary referencing `Nightwire.Runtime`.

## Current playable scope and remaining UI work

The sample vertical slice supports:

- Episode library presentation and installed/offline episode discovery.\n- Three layered, procedurally generated fog banks drifting at different speeds behind the interface; no external fog asset or network download is required.
- Episode session start/resume and save-backed return to archive.
- Timed, localized text conversations.
- Authored branching choices and relationship/story-flag effects.
- Inline previews of unlocked, manifest-verified images.
- Playback of unlocked, manifest-verified audio (MP3/WAV/OGG) and video attachments, copied to a bounded temporary cache and controlled in-message; unsupported audio formats are rejected rather than guessed.
- Offline installation of the bundled Episode 001 sample.

The following presentation features are not complete yet:

- Prerecorded call screen, synchronized subtitles, and visual effects.
- Document viewer, character profiles, social feed, and media gallery.
- Group-chat-specific participant layout.
- Player language settings and free-form replies.

The episode engine already has data contracts and runtime support for several of these content types; their Unity presentation is still outstanding. Do not mistake the existence of the runtime contract for a finished screen.

## Separate CI pipelines

- **Engine CI** builds the portable runtime and validator, validates the sample episode, and runs the 62 runtime regression tests plus validator integration tests.
- **Game UI CI** stages the runtime assemblies and checks Unity project metadata, UXML parsing, controller-to-UXML named-element contracts, stylesheet references, assembly references, verified audio/video integration contracts, and 12 UI source regression tests.

Game UI CI stages the runtime assemblies, validates UI source contracts, builds the Android StrongBox secure-store AAR with JDK 17 / Gradle 9.3.1 / Android SDK 36, verifies its JNI class and backup-exclusion resources, and uploads the AAR as the `nightwire-android-secure-store-aar` workflow artifact. It is still not a Unity Editor compile or Android player build. Unity Editor compilation, Play Mode testing, final manifest merge, and Android device/StrongBox verification require an actual Unity 6000.3.28f1 installation and device/toolchain execution.

See [episode library architecture](../docs/architecture/UNITY_EPISODE_LIBRARY_UI.md), [messenger architecture](../docs/architecture/MESSENGER_SCREEN.md), and [Android StrongBox notes](Android/NightwireSecureStore/README.md).

# NIGHTWIRE Unity presentation integration

Status: the episode archive and first episode-driven messenger screen have UI Toolkit source and controllers; this directory is **not yet a complete Unity project**.

## Canonical episode archive screen

Use these files as the single UI implementation:

- `Assets/Nightwire/UI/EpisodeLibraryView.uxml` — episode archive hierarchy and controls.
- `Assets/Nightwire/UI/MessengerScreen.uxml` — conversation screen hierarchy.
- `Assets/Nightwire/UI/EpisodeLibraryView.uss` — responsive dark archive styling.
- `Assets/Nightwire/UI/MessengerScreen.uss` — message bubbles, incoming/outgoing alignment, media preview, and choice styling.
- `Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — binds the archive visual tree to the presentation model.
- `Assets/Nightwire/Scripts/MessengerScreenController.cs` — renders package-driven conversations, localized text, choices, and verified inline image assets.
- `Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — composes release services, storage paths, library presentation, startup recovery, and session lifecycle.
- `Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — owns resume/new-session selection and the per-episode session lease.
- `Assets/Nightwire/Scripts/INightwirePlatformServices.cs` — platform provider contract.
- `Assets/Nightwire/Scripts/AndroidNightwirePlatformServices.cs` — Android provider for HTTPS catalogue configuration, app-provisioned public keys, and the StrongBox sequence-store bridge.
- `Assets/Nightwire/Scripts/Nightwire.Unity.asmdef` — Unity presentation assembly boundary referencing `Nightwire.Runtime`.

The view is backed by the UI-neutral `EpisodeLibraryPresentation` adapter in `src/Nightwire.Runtime/Installation/EpisodeLibraryPresentation.cs`. The same file contains `EpisodeAccessCoordinator`, the atomic per-episode gate shared by session starts and installs.

The archive screen supports signed catalogue refresh, installed/offline and downloadable states, update availability, download progress, cancellation, and a `PlayEpisodeRequested` event. The messenger screen advances the portable conversation runtime, displays only delivered messages, marks them read, renders localized story choices, and previews unlocked manifest-verified images. The back-to-archive action saves and closes the session before releasing its lease. The composition root owns the runtime services and the presentation model; the view does not own keys, network transport, storage, or saves.

## Importing the portable runtime

The runtime core currently lives at `../src/Nightwire.Runtime` and targets .NET Standard 2.1. Before importing this UI source into a Unity 6.3 LTS project:

1. Run `tools/prepare-unity-runtime.sh` (macOS/Linux) or `tools/Prepare-UnityRuntime.ps1` (Windows) from the repository root. The script runs `dotnet publish` for .NET Standard 2.1 and stages the runtime plus copied NuGet dependency assemblies and a linker-preservation file into `unity/Assets/Plugins/Nightwire/`.
2. Import the staged `unity/Assets/Plugins/Nightwire/` folder into the Unity project and confirm the assemblies are enabled for Editor and Android.
3. Import the `Assets/Nightwire/UI` and `Assets/Nightwire/Scripts` folders from this repository into the Unity project.
4. Create a Panel Settings asset and a GameObject with a `UIDocument` component. Assign `EpisodeLibraryView.uxml` as its Source Asset and the Panel Settings asset.
5. Assign `EpisodeLibraryView.uss` to the UXML document's Style Sheets list in UI Builder or the Inspector.
6. Attach `EpisodeLibraryViewController` to the same GameObject.
7. Attach `AndroidNightwirePlatformServices` for Android. Configure the HTTPS catalogue URL and trusted ECDSA P-256 public-key entries in the Inspector. Build `unity/Android/NightwireSecureStore` as an AAR and import it under `Assets/Plugins/Android/`. This provider requires Android API 28+ and StrongBox; unsupported devices fail closed. There is deliberately no plaintext/software-key fallback.
8. Attach `NightwireApplicationRoot`, assign the library view and platform provider, and set the runtime version. The root creates the release verifier/manager, save store, shared access coordinator, session host, and presentation model. It calls `RecoverAtStartup()` before refreshing the catalogue or allowing episode sessions.
9. Add a second `UIDocument` GameObject for `MessengerScreen.uxml`, assign `MessengerScreen.uss`, and attach `MessengerScreenController`. Assign it to `NightwireApplicationRoot` alongside the archive view. The root hides the messenger until an episode opens.
10. The root binds `EpisodeLibraryViewController` and handles `PlayEpisodeRequested`. `EpisodeSessionHost` acquires the shared session lease, resumes an existing save (including backup-only saves), or starts a new session only when no save exists. On success the root shows the messenger and hides the archive. The messenger back action saves/closes the session, releases the lease, and returns to the archive.

The host should resume an existing save or start a new session only when no save exists. The UI deliberately does not overwrite or reset player progress.

## Dependency and platform gates

The portable runtime references `System.Text.Json 10.0.15` through its .NET project. Unity's .NET Standard 2.1 API profile does not automatically restore arbitrary NuGet package dependencies from the .csproj. The repository now includes PowerShell and shell staging scripts that use `dotnet publish` to copy the resolved DLL dependency set and generate `link.xml`. This is a repeatable packaging step, not proof of Unity compatibility; compile and verify under Unity Mono and Android IL2CPP, including JSON reflection and cryptography.

The repository now contains an Android StrongBox-backed sequence-store implementation and Unity provider bridge. It rotates a StrongBox AES-GCM key on each accepted sequence and deletes the previous key before committing the new record, so replaying an older preferences snapshot should fail closed. This depends on the device's StrongBox security implementation; it is not protection against a fully compromised/rooted OS, and server-side sequence enforcement remains recommended. The AAR has not been built or device-tested. The repository still does not contain a complete Unity project, `ProjectSettings`, a scene, a Panel Settings asset, or verified runtime import. These are integration sources, not a claim that an Editor scene has run.

See [the detailed episode library integration guide](../docs/architecture/UNITY_EPISODE_LIBRARY_UI.md) and [Android StrongBox store build/test notes](Android/NightwireSecureStore/README.md). Unity Editor compilation, AAR build, play-mode tests, and Android device verification remain pending.

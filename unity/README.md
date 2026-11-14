# NIGHTWIRE Unity presentation integration

Status: first UI Toolkit episode archive assets and controller are present; this directory is **not yet a complete Unity project**.

## Canonical episode archive screen

Use these files as the single UI implementation:

- `Assets/Nightwire/UI/EpisodeLibraryView.uxml` — named screen hierarchy and controls.
- `Assets/Nightwire/UI/EpisodeLibraryView.uss` — responsive dark archive styling, button states, and restrained transitions.
- `Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — binds the visual tree to the presentation model.
- `Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — composes release services, storage paths, library presentation, startup recovery, and session lifecycle.
- `Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — owns resume/new-session selection and the per-episode session lease.
- `Assets/Nightwire/Scripts/INightwirePlatformServices.cs` — security-sensitive platform provider contract for catalogue URL, trusted public keys, and protected sequence storage.
- `Assets/Nightwire/Scripts/Nightwire.Unity.asmdef` — Unity presentation assembly boundary referencing `Nightwire.Runtime`.

The view is backed by the UI-neutral `EpisodeLibraryPresentation` adapter in `src/Nightwire.Runtime/Installation/EpisodeLibraryPresentation.cs`. The same file contains `EpisodeAccessCoordinator`, the atomic per-episode gate shared by session starts and installs.

The screen supports signed catalogue refresh, installed/offline and downloadable states, update availability, download progress, cancellation, and a `PlayEpisodeRequested` event. The composition root owns the runtime services and the presentation model; the view does not own keys, network transport, storage, or saves.

## Importing the portable runtime

The runtime core currently lives at `../src/Nightwire.Runtime` and targets .NET Standard 2.1. Before importing this UI source into a Unity 6.3 LTS project:

1. Build `src/Nightwire.Runtime/Nightwire.Runtime.csproj` with a compatible .NET SDK.
2. Import `Nightwire.Runtime.dll` into the Unity project's `Assets/Plugins/Nightwire/` folder and confirm it is enabled for Editor and Android.
3. Import the `Assets/Nightwire/UI` and `Assets/Nightwire/Scripts` folders from this repository into the Unity project.
4. Create a Panel Settings asset and a GameObject with a `UIDocument` component. Assign `EpisodeLibraryView.uxml` as its Source Asset and the Panel Settings asset.
5. Assign `EpisodeLibraryView.uss` to the UXML document's Style Sheets list in UI Builder or the Inspector.
6. Attach `EpisodeLibraryViewController` to the same GameObject.
7. Implement and attach a `NightwirePlatformServicesBehaviour` provider. It must supply the HTTPS catalogue URI, app-provisioned ECDSA P-256 public keys, and a rollback-resistant `IReleaseCatalogueSequenceStore`. There is deliberately no plaintext-file fallback.
8. Attach `NightwireApplicationRoot`, assign the library view and platform provider, and set the runtime version. The root creates the release verifier/manager, save store, shared access coordinator, session host, and presentation model. It calls `RecoverAtStartup()` before refreshing the catalogue or allowing episode sessions.
9. The root binds `EpisodeLibraryViewController` and handles `PlayEpisodeRequested`. `EpisodeSessionHost` acquires the shared session lease, resumes an existing save (including backup-only saves), or starts a new session only when no save exists. The lease is held until `SaveAndClose()` completes.

The host should resume an existing save or start a new session only when no save exists. The UI deliberately does not overwrite or reset player progress.

## Dependency and platform gates

The portable runtime references `System.Text.Json 10.0.15` through its .NET project. Unity's .NET Standard 2.1 API profile does not automatically restore arbitrary NuGet package dependencies from the .csproj. The exact System.Text.Json dependency set must be imported and tested under Unity's Mono and Android IL2CPP backends, including linker preservation and cryptography support.

The repository now contains composition-root and session-host source, but no concrete Android platform provider. The repository does not yet contain a complete Unity project, `ProjectSettings`, a scene, a Panel Settings asset, Android Player Settings, production Android secure-storage implementation, or a verified Unity import of the runtime assembly. The platform provider must fail closed until protected sequence storage and trusted release keys are configured. These files are integration source/assets, not a claim that an Editor scene has already been run.

See [the detailed episode library integration guide](../docs/architecture/UNITY_EPISODE_LIBRARY_UI.md). Unity Editor compilation, play-mode tests, and Android device verification remain pending.

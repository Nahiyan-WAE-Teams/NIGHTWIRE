# NIGHTWIRE Unity presentation integration

Status: first UI Toolkit episode archive assets and controller are present; this directory is **not yet a complete Unity project**.

## Canonical episode archive screen

Use these files as the single UI implementation:

- `Assets/Nightwire/UI/EpisodeLibraryView.uxml` — named screen hierarchy and controls.
- `Assets/Nightwire/UI/EpisodeLibraryView.uss` — responsive dark archive styling, button states, and restrained transitions.
- `Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — binds the visual tree to the presentation model.
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
7. In the composition root, create the trusted-key verifier, protected catalogue sequence store, HTTPS transport, release manager, and `EpisodeLibraryPresentation`. Call `RecoverAtStartup()` before opening any episode session or beginning any install.
8. Bind the presentation model with `EpisodeLibraryViewController.Bind(presentation)` on the Unity main thread and subscribe to `PlayEpisodeRequested`.
9. Before starting or resuming an episode, acquire a session lease from the same `EpisodeAccessCoordinator` instance supplied to the release manager. Keep the lease until the runtime session has saved and closed, then dispose it.

The host should resume an existing save or start a new session only when no save exists. The UI deliberately does not overwrite or reset player progress.

## Dependency and platform gates

The portable runtime references `System.Text.Json 10.0.15` through its .NET project. Unity's .NET Standard 2.1 API profile does not automatically restore arbitrary NuGet package dependencies from the .csproj. The exact System.Text.Json dependency set must be imported and tested under Unity's Mono and Android IL2CPP backends, including linker preservation and cryptography support.

The repository does not yet contain a complete Unity project, `ProjectSettings`, a scene, a Panel Settings asset, Android Player Settings, production Android secure-storage adapters, or a verified Unity import of the runtime assembly. These files are integration source/assets, not a claim that an Editor scene has already been run.

See [the detailed episode library integration guide](../docs/architecture/UNITY_EPISODE_LIBRARY_UI.md). Unity Editor compilation, play-mode tests, and Android device verification remain pending.

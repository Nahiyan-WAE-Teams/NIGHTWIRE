# Unity episode library integration

## Baseline checked

The current production baseline remains **Unity 6.3 LTS (6000.3)**. Unity identifies 6.3 LTS as its current LTS line and recommends LTS for production projects that are locking an engine version. The UI integration uses the runtime UI Toolkit workflow documented for Unity 6.3: a UXML document, a `UIDocument`, and a `MonoBehaviour` that binds controls during `OnEnable` and unregisters during `OnDisable`.

Official references:
- Unity 6.3 LTS support and recommendation: https://unity.com/releases/unity-6/support
- Unity 6.3 runtime UI setup: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-get-started-with-runtime-ui
- UI Document lifecycle and source asset: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-render-runtime-ui/uie-create-ui-document-component
- UI Toolkit versus uGUI feature comparison: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/ui-system-compare

UI Toolkit is selected for this archive screen because it is a multi-resolution, data-driven screen with reusable list rows and UI transitions. It is not a decision to force every in-game interface into UI Toolkit; cinematic/Timeline-heavy screens can use other Unity systems where appropriate.

## Files added

- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uxml` — screen hierarchy and named controls.
- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uss` — dark archive styling and responsive flex layout.
- `unity/Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — UI Toolkit view controller.
- `src/Nightwire.Runtime/Installation/EpisodeLibraryPresentation.cs` — UI-neutral snapshot/progress adapter.
- `src/Nightwire.Runtime/Installation/EpisodeAccessCoordinator.cs` — atomic per-episode session/install lease gate.

The visual direction uses near-black navy surfaces, restrained violet accents, compact archive metadata, progress feedback, and explicit offline-ready status. No episode-specific assets are hardcoded into the UI.

## Unity scene setup

1. Create a Unity 6.3 LTS project and import the runtime assembly and its verified dependencies.
2. Add `EpisodeLibraryView.uxml` and `EpisodeLibraryView.uss` to the Unity project. Assign the USS to the UXML document's Style Sheets list in UI Builder or the Inspector.
3. Create a Panel Settings asset and a GameObject with a `UIDocument` component. Assign the UXML as its Source Asset and the Panel Settings asset.
4. Add `EpisodeLibraryViewController` to the same GameObject.
5. In the game's composition root, create the trusted-key verifier, protected catalogue sequence store, acceptance service, HTTPS transport, install manager, and presentation model. Call `EpisodeLibraryViewController.Bind(presentation)` on the Unity main thread.
6. Subscribe to `PlayEpisodeRequested`. The host should load the installed directory using `RuntimeSessionCoordinator`, resume the player's save when one exists, or start a new session only when no save exists. The UI deliberately does not overwrite or reset player progress.
7. Call `EpisodeReleaseManager.RecoverAtStartup()` before opening any episode session or beginning any install.

The repository does not yet contain a complete Unity project, scene, Panel Settings asset, Android Player Settings, or a verified Unity import of the runtime assembly. These files are integration source/assets, not a claim that an Editor scene has already been run.

## Session/install exclusion contract

Use one shared `EpisodeAccessCoordinator` instance for both the library manager and session lifecycle:

- Before starting or resuming an episode, call `TryAcquireSessionLease(episodeId, out lease)`.
- If acquisition fails, do not open the session: an install/update already owns the episode.
- Keep the session lease for the entire time the episode is open.
- Close and save the `RuntimeSessionCoordinator` first, then dispose the session lease.
- Pass the same `EpisodeAccessCoordinator` instance to `EpisodeReleaseManager` as its `IEpisodeInstallLeaseProvider`.

This makes the session-start versus install-start decision atomic inside the process. Multi-process coordination is not implemented; the mobile game host should be a single process and serialize lifecycle calls on its main thread.

## UI behavior

- Refresh requests a signed catalogue refresh; a failed refresh leaves the last accepted catalogue visible.
- Episode rows distinguish downloadable, installed/offline, update available, installed-but-unlisted, and invalid local packages.
- Download progress shows received bytes and a percentage when the server reports a total.
- Cancel requests cancellation of the current download/install operation.
- Download/update buttons are enabled only for eligible signed releases.
- Installed verified episodes raise `PlayEpisodeRequested`; the host decides whether to resume or start.
- Offline-ready episodes remain listed from disk even when catalogue refresh is unavailable.

## Important dependency gate

The portable runtime currently references `System.Text.Json 10.0.15` through its .NET project. Unity's .NET Standard 2.1 API profile does not automatically restore arbitrary NuGet package dependencies from the .csproj. Before the Unity project is considered buildable, the exact System.Text.Json dependency set must be imported and tested under Unity's Mono and Android IL2CPP backends, including linker preservation and cryptography support. Do not mark the runtime assembly as integrated until that device compatibility check is complete.

The Unity UI files have not been compiled or executed in the Editor yet. Build, play-mode tests, and Android device verification remain pending.

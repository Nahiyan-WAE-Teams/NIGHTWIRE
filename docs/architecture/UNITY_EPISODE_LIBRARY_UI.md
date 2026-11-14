# Unity episode library integration

## Baseline checked

The current production baseline remains **Unity 6.3 LTS (6000.3)**. For Android plugin builds, match the Gradle/AGP versions to the exact Unity patch. Unity's current compatibility table lists Gradle 9.3.1 / AGP 9.1.1 for 6000.3.26f1 and later. Unity identifies 6.3 LTS as its current LTS line and recommends LTS for production projects that are locking an engine version. The UI integration uses the runtime UI Toolkit workflow documented for Unity 6.3: a UXML document, a `UIDocument`, and a `MonoBehaviour` that binds controls during `OnEnable` and unregisters during `OnDisable`.

Official references:
- Unity 6.3 LTS support and recommendation: https://unity.com/releases/unity-6/support
- Unity 6.3 runtime UI setup: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-get-started-with-runtime-ui
- Unity 6.3 Android Gradle/AGP compatibility: https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/introducing/gradle-overview/version-compatibility
- Android StrongBox AES key generation: https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec
- UI Document lifecycle and source asset: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-render-runtime-ui/uie-create-ui-document-component
- UI Toolkit versus uGUI feature comparison: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/ui-system-compare

UI Toolkit is selected for this archive screen because it is a multi-resolution, data-driven screen with reusable list rows and UI transitions. It is not a decision to force every in-game interface into UI Toolkit; cinematic/Timeline-heavy screens can use other Unity systems where appropriate.

## Files added

- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uxml` — screen hierarchy and named controls.
- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uss` — dark archive styling and responsive flex layout.
- `unity/Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — UI Toolkit view controller.
- `unity/Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — application composition root for release, save, library, and session services.
- `unity/Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — resume/new-session selection and lease lifetime.
- `unity/Assets/Nightwire/Scripts/INightwirePlatformServices.cs` — platform provider contract.
- `unity/Assets/Nightwire/Scripts/AndroidNightwirePlatformServices.cs` — Android provider and Java bridge.
- `unity/Android/NightwireSecureStore/` — StrongBox-backed encrypted sequence store Android library.
- `tools/prepare-unity-runtime.sh` and `tools/Prepare-UnityRuntime.ps1` — dependency staging scripts.
- `unity/Assets/Nightwire/Scripts/Nightwire.Unity.asmdef` — presentation assembly boundary referencing `Nightwire.Runtime`.
- `src/Nightwire.Runtime/Installation/EpisodeLibraryPresentation.cs` — UI-neutral snapshot/progress adapter and atomic per-episode session/install lease gate.

The visual direction uses near-black navy surfaces, restrained violet accents, compact archive metadata, progress feedback, and explicit offline-ready status. No episode-specific assets are hardcoded into the UI. Buttons use short 140 ms color/scale transitions; there are no looping effects or mandatory motion.

## Unity scene setup

1. Create a Unity 6.3 LTS project. Run the runtime staging script from the repository root, then import the staged runtime/dependency DLLs and generated `link.xml`.
2. Add `EpisodeLibraryView.uxml` and `EpisodeLibraryView.uss` to the Unity project. Assign the USS to the UXML document's Style Sheets list in UI Builder or the Inspector.
3. Create a Panel Settings asset and a GameObject with a `UIDocument` component. Assign the UXML as its Source Asset and the Panel Settings asset.
4. Add `EpisodeLibraryViewController` to the same GameObject.
5. In the game's composition root, create the trusted-key verifier, protected catalogue sequence store, acceptance service, HTTPS transport, install manager, and presentation model. Call `EpisodeLibraryViewController.Bind(presentation)` on the Unity main thread.
6. Subscribe to `PlayEpisodeRequested`. The host should load the installed directory using `RuntimeSessionCoordinator`, resume the player's save when one exists, or start a new session only when no save exists. The UI deliberately does not overwrite or reset player progress.
7. Call `EpisodeReleaseManager.RecoverAtStartup()` before opening any episode session or beginning any install.

The repository contains bootstrap/session-host integration source and an Android StrongBox provider implementation, but does not yet contain a complete Unity project, scene, Panel Settings asset, Android Player Settings, a built AAR, or a verified Unity import of the runtime assembly. These files are integration source/assets, not a claim that an Editor scene has already been run.

## Application composition and platform boundary

`NightwireApplicationRoot` is the Unity bootstrap component. Assign its episode-library view and a concrete `NightwirePlatformServicesBehaviour` provider, then set the runtime version. On startup it:

1. Obtains the HTTPS catalogue URI, trusted ECDSA P-256 public keys, and protected catalogue sequence store from the platform provider.
2. Creates persistent episode, download, and save directories under `Application.persistentDataPath`.
3. Creates one `EpisodeAccessCoordinator` and shares it with the release manager and `EpisodeSessionHost`.
4. Runs interrupted-install recovery before refreshing the catalogue.
5. Binds the library presentation to the UI and connects the Play action to session opening.
6. Saves active progress on application pause and saves/closes the session on application quit or root destruction.

The platform contract intentionally has no insecure default implementation. `AndroidNightwirePlatformServices` supplies the HTTPS URL and app-provisioned public keys, and bridges to `NightwireSecureStore`. The Android library requires API 28+ and StrongBox-backed AES keys. It rotates the key per sequence and deletes the previous key before committing the new encrypted record; an older preferences snapshot then references a deleted key and fails closed. This depends on the device's StrongBox guarantees and does not defend against a fully compromised/rooted OS. Server-side sequence enforcement remains recommended. No production catalogue URL or signing public key is fabricated in this repository.

`EpisodeSessionHost` acquires the session lease before loading the package. It resumes when either the primary save or its recovery backup exists; otherwise it starts a new session. A failed open releases the lease. On close it saves, closes the coordinator, and only then releases the lease. The host is single-session and must be called on the Unity main thread.

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

The portable runtime currently references `System.Text.Json 10.0.15` through its .NET project. NuGet now also lists `11.0.0` as a stable release (10 November 2026), and it still publishes a .NET Standard 2.0 asset. We are retaining the existing 10.0.15 pin for this Unity integration pass rather than taking a just-released major version before Mono/IL2CPP compatibility is verified; evaluate 11.0.0 during the dependency device-test milestone. Unity's .NET Standard 2.1 API profile does not automatically restore arbitrary NuGet package dependencies from the .csproj. The repository now includes shell and PowerShell scripts that run `dotnet publish`, copy the resolved DLL set into `unity/Assets/Plugins/Nightwire/`, and generate `link.xml` with preservation entries. This staging workflow has not been run here. Before the Unity project is considered buildable, compile the imported dependencies under Unity Mono and Android IL2CPP, including linker preservation and cryptography support.

The Unity UI files and Android AAR have not been compiled or executed yet. Build, play-mode tests, StrongBox replay tests, and Android IL2CPP verification remain pending.

UI Toolkit USS transitions are documented for Unity 6.3 and support short changes to color and transform properties: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-uss/properties/reference

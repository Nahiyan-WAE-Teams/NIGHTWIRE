# Unity episode library integration

## Baseline checked

The current production baseline is **Unity 6.3 LTS (6000.3.28f1)**. This patch includes the November 2026 fixes required by Unity's security advisory; do not pin earlier 6000.3 patches. For Android plugin builds, match the Gradle/AGP versions to the exact Unity patch. Unity's current compatibility table lists Gradle 9.3.1 / AGP 9.1.1 for 6000.3.28f1. Unity identifies 6.3 LTS as its current LTS line and recommends LTS for production projects that are locking an engine version. The official 6000.3 Android compatibility table specifies Gradle 9.3.1 / AGP 9.1.1 for 6000.3.26f1 and later; Android SDK build tools 36.0.0, NDK r27c, and JDK 17 are supported for 6000.3.28f1+. The UI integration uses the runtime UI Toolkit workflow documented for Unity 6.3: a UXML document, a `UIDocument`, and a `MonoBehaviour` that binds controls during `OnEnable` and unregisters during `OnDisable`.

Official references:
- Unity 6.3 LTS support and recommendation: https://unity.com/releases/unity-6/support
- Unity 6000.3.28f1 release: https://unity.com/releases/editor/whats-new/6000.3.28f1
- Unity November 2026 security update: https://unity.com/security/unity-emudhra-compromise
- Unity 6000.3 Android Gradle/AGP compatibility: https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/introducing/gradle-overview/version-compatibility
- Unity 6000.3 Android SDK/NDK/JDK compatibility: https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/getting-started/sdksetup/supported-dependency-versions
- Unity 6.3 runtime UI setup: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-get-started-with-runtime-ui
- Unity 6.3 Android Gradle/AGP compatibility: https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/introducing/gradle-overview/version-compatibility
- Android StrongBox AES key generation: https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec
- UI Document lifecycle and source asset: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-support-for-runtime-ui/uie-render-runtime-ui/uie-create-ui-document-component
- UI Toolkit versus uGUI feature comparison: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/ui-system-compare

UI Toolkit is selected for this archive screen because it is a multi-resolution, data-driven screen with reusable list rows and UI transitions. It is not a decision to force every in-game interface into UI Toolkit; cinematic/Timeline-heavy screens can use other Unity systems where appropriate.

## Files added

- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uxml` — archive hierarchy and named controls.
- `unity/Assets/Nightwire/UI/MessengerScreen.uxml` — messenger hierarchy, message list, choice dock, and archive navigation.
- `unity/Assets/Nightwire/UI/EpisodeLibraryView.uss` — dark archive styling and responsive flex layout.
- `unity/Assets/Nightwire/UI/MessengerScreen.uss` — incoming/outgoing bubbles, message attachments, and branching choice styles.
- `unity/Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — archive view controller.
- `unity/Assets/Nightwire/Scripts/MessengerScreenController.cs` — episode-driven messenger view controller.
- `unity/Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — application composition root for release, save, library, and session services.
- `unity/Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — resume/new-session selection and lease lifetime.
- `unity/Assets/Nightwire/Scripts/INightwirePlatformServices.cs` — platform provider contract.
- `unity/Assets/Nightwire/Scripts/AndroidNightwirePlatformServices.cs` — Android provider and Java bridge.
- `unity/Assets/Nightwire/Scripts/NightwireDevelopmentPlatformServices.cs` — offline-only editor/development provider.
- `unity/Assets/Nightwire/Scripts/NightwireBundledEpisodeSeeder.cs` — installs the bundled Episode 001 sample before library initialization.
- `unity/Assets/Editor/NightwireProjectBootstrapper.cs` — generates Panel Settings, the main scene, build-scene registration, and the sample ZIP on first project open.
- `unity/ProjectSettings/ProjectVersion.txt` and `unity/Packages/manifest.json` — Unity 6000.3.28f1 project baseline.
- `unity/Android/NightwireSecureStore/` — StrongBox-backed encrypted sequence store Android library.
- `tools/prepare-unity-runtime.sh` and `tools/Prepare-UnityRuntime.ps1` — dependency staging scripts.
- `unity/Assets/Nightwire/Scripts/Nightwire.Unity.asmdef` — presentation assembly boundary referencing `Nightwire.Runtime`.
- `src/Nightwire.Runtime/Installation/EpisodeLibraryPresentation.cs` — UI-neutral snapshot/progress adapter and atomic per-episode session/install lease gate.

The visual direction uses near-black navy surfaces, restrained violet accents, compact archive metadata, progress feedback, and explicit offline-ready status. No episode-specific assets are hardcoded into the UI. Buttons use short 140 ms color/scale transitions; there are no looping effects or mandatory motion.

## Unity project setup and generated scene

The repository now includes a Unity project scaffold under `unity/`:

- `ProjectSettings/ProjectVersion.txt` pins Unity 6000.3.28f1.
- `Packages/manifest.json` is the minimal project package manifest; UI Toolkit is part of the Editor runtime modules.
- `Assets/Editor/NightwireProjectBootstrapper.cs` creates the Panel Settings asset, `NightwireMain.unity`, Build Settings registration, and a ZIP of `examples/Episode_001` in StreamingAssets.
- The generated scene wires the episode library, messenger, application root, offline development provider, and bundled episode seeder.

First-time setup:

1. Run `tools/prepare-unity-runtime.sh` (macOS/Linux) or `tools/Prepare-UnityRuntime.ps1` (Windows) from the repository root before opening Unity. This stages the .NET Standard 2.1 runtime and its NuGet dependencies into `unity/Assets/Plugins/Nightwire/`.
2. Open the `unity/` folder in Unity 6000.3.28f1. The editor bootstrapper generates the initial scene and sample archive.
3. Open `Assets/Scenes/NightwireMain.unity` and press Play. The sample episode is installed into persistent storage on first launch and appears as an offline episode.
4. Commit the generated scene, Panel Settings asset, and Unity-generated `.meta` files after Unity imports them.

The generated scene intentionally uses `NightwireDevelopmentPlatformServices`. It disables remote catalogue refresh, uses a local development sequence file, and is disabled in non-development players. This provides a self-contained offline vertical slice for Editor/development testing. It is not the production release provider.

For a production Android scene, replace the development provider with `AndroidNightwirePlatformServices`, configure the HTTPS catalogue URL and trusted ECDSA P-256 public keys, import the built StrongBox AAR, and preserve the AAR's backup-exclusion manifest entries. The bundled sample seeder should be removed or disabled in production; remote episode delivery must use the signed catalogue path.

The Unity project scaffold and source contracts are present, but the generated scene has not yet been opened or executed in a Unity Editor in this environment. AAR build, Editor compilation, Play Mode tests, and Android device verification remain outstanding.

## Application composition and platform boundary

`NightwireApplicationRoot` is the Unity bootstrap component. Assign its episode-library view and a concrete `NightwirePlatformServicesBehaviour` provider, then set the runtime version. On startup it:

1. Obtains the HTTPS catalogue URI, trusted ECDSA P-256 public keys, and protected catalogue sequence store from the platform provider.
2. Creates persistent episode, download, and save directories under `Application.persistentDataPath`.
3. Creates one `EpisodeAccessCoordinator` and shares it with the release manager and `EpisodeSessionHost`.
4. Runs interrupted-install recovery before refreshing the catalogue.
5. Binds the library presentation to the UI and connects the Play action to session opening.
6. Saves active progress on application pause and saves/closes the session on application quit or root destruction.

The platform contract exposes an explicit `ShouldRefreshCatalogue` policy. `AndroidNightwirePlatformServices` enables signed HTTPS catalogue refresh, supplies the app-provisioned public keys, and bridges to `NightwireSecureStore`. The development provider disables remote refresh and uses a local-only sequence file solely for the bundled demo. The Android library requires API 28+ and StrongBox-backed AES keys. It rotates the key per sequence and deletes the previous key before committing the new encrypted record; an older preferences snapshot then references a deleted key and fails closed. This depends on the device's StrongBox guarantees and does not defend against a fully compromised/rooted OS. Server-side sequence enforcement remains recommended. No production catalogue URL or signing public key is fabricated in this repository.

`EpisodeSessionHost` acquires the session lease before loading the package. It resumes when either the primary save or its recovery backup exists; otherwise it starts a new session. A failed open releases the lease. On close it saves, closes the coordinator, and only then releases the lease. The host is single-session and must be called on the Unity main thread.

## Session/install exclusion contract

Use one shared `EpisodeAccessCoordinator` instance for both the library manager and session lifecycle:

- Before starting or resuming an episode, call `TryAcquireSessionLease(episodeId, out lease)`.
- If acquisition fails, do not open the session: an install/update already owns the episode.
- Keep the session lease for the entire time the episode is open.
- Close and save the `RuntimeSessionCoordinator` first, then dispose the session lease.
- Pass the same `EpisodeAccessCoordinator` instance to `EpisodeReleaseManager` as its `IEpisodeInstallLeaseProvider`.

This makes the session-start versus install-start decision atomic inside the process. Multi-process coordination is not implemented; the mobile game host should be a single process and serialize lifecycle calls on its main thread.

## Messenger screen behavior

- `MessengerScreenController` binds to the active `RuntimeSessionCoordinator` through `EpisodeSessionHost`; it does not own story state.
- It begins the active authored conversation when needed and advances its deterministic clock from Unity frame time. The runtime persists only at message-delivery boundaries, avoiding a save-file write on every rendered frame; partial delay is checkpointed on application pause/close.
- Only delivered messages are rendered. Marking a message read routes the `message_completed` trigger through the runtime.
- Choice buttons are created from the current message's authored `options` array and call `SelectChoice`; no dialogue or choice text is hardcoded in the screen.
- Text/system/choice messages are localized using the episode's default locale. Unlocked image assets are opened through `EpisodeAssetResolver` and checked against their manifest before inline preview. Audio/video attachments and call invitations currently render as identified placeholders; playback and dedicated call UI are not implemented.
- The back action saves and closes the session, releases the session lease, and returns to the archive.

## Archive UI behavior

- Refresh requests a signed catalogue refresh; a failed refresh leaves the last accepted catalogue visible.
- Episode rows distinguish downloadable, installed/offline, update available, installed-but-unlisted, and invalid local packages.
- Download progress shows received bytes and a percentage when the server reports a total.
- Cancel requests cancellation of the current download/install operation.
- Download/update buttons are enabled only for eligible signed releases.
- Installed verified episodes raise `PlayEpisodeRequested`; the host decides whether to resume or start.
- Offline-ready episodes remain listed from disk even when catalogue refresh is unavailable.

## Important dependency gate

The portable runtime currently references `System.Text.Json 10.0.15` through its .NET project. NuGet now also lists `11.0.0` as a stable release (10 November 2026), and it still publishes a .NET Standard 2.0 asset. We are retaining the existing 10.0.15 pin for this Unity integration pass rather than taking a just-released major version before Mono/IL2CPP compatibility is verified; evaluate 11.0.0 during the dependency device-test milestone. Unity's .NET Standard 2.1 API profile does not automatically restore arbitrary NuGet package dependencies from the .csproj. The repository includes shell and PowerShell scripts that run `dotnet publish`, copy the resolved DLL set into `unity/Assets/Plugins/Nightwire/`, and generate `link.xml` with preservation entries. The Game UI CI pipeline now executes the shell staging workflow successfully and verifies that the runtime assembly is present. This does not prove Unity compatibility: compile the imported dependencies under Unity Mono and Android IL2CPP, including linker preservation and cryptography support.

The Unity UI files and Android AAR have not been compiled or executed yet. Build, play-mode tests, StrongBox replay tests, and Android IL2CPP verification remain pending.

UI Toolkit USS transitions are documented for Unity 6.3 and support short changes to color and transform properties: https://docs.unity.com/en-us/engine/6000.3/manual/uitoolkits/uielements/uie-uss/properties/reference

## Separate CI status

- Engine CI: portable runtime build, 62 runtime regression tests, validator build, sample-package validation, and 8 validator integration tests pass in the separate Engine CI workflow.
- Game UI CI: runtime assembly staging, Unity project metadata, UXML parsing, stylesheet references, controller query-name contracts, assembly references, and 10 UI source regression tests pass in the separate Game UI workflow.
- Game UI CI does not launch Unity Editor or build an Android player. Editor compilation, Play Mode tests, AAR build, and Android IL2CPP/device verification are still required.

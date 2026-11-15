# NIGHTWIRE Unity project

The repository now contains a Unity 6.3 project scaffold, an editor-generated main scene, the episode archive and messenger UI Toolkit screens, an offline sample-episode bootstrap, and a separate Game UI source CI pipeline.

**Pinned editor:** Unity 6000.3.28f1. Do not use earlier 6000.3 patches; this is the patched LTS baseline selected after reviewing Unity's November 2026 security update.

## First-time setup

1. From the repository root, run `tools/prepare-unity-runtime.sh` (macOS/Linux) or `tools/Prepare-UnityRuntime.ps1` (Windows). This publishes the portable .NET Standard 2.1 runtime and stages `Nightwire.Runtime.dll`, its resolved dependencies, and `link.xml` into `unity/Assets/Plugins/Nightwire/`. The Windows script bootstraps the .NET 10 SDK if the builder does not already have it.
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
- `Assets/Nightwire/UI/MessengerScreen.uxml` and `.uss` — conversation interface, message bubbles, image previews, choice dock, and opened-conversation inbox.
- `Assets/Nightwire/Scripts/EpisodeLibraryViewController.cs` — archive presentation adapter.
- `Assets/Nightwire/Scripts/MessengerScreenController.cs` — package-driven text, choices, localized character profiles, episode contact directory, episode social feed, unlocked evidence library, bounded text-document previews, verified images, and verified audio/video playback.
- `Assets/Nightwire/Scripts/NightwireApplicationRoot.cs` — composition root, startup recovery, and session lifecycle.
- `Assets/Nightwire/Scripts/EpisodeSessionHost.cs` — resume/new-session selection and per-episode session lease.
- `Assets/Nightwire/Scripts/AndroidNightwirePlatformServices.cs` — production Android release configuration and StrongBox bridge.
- `Assets/Nightwire/Scripts/NightwireDevelopmentPlatformServices.cs` — offline development-only provider.
- `Assets/Nightwire/Scripts/NightwireBundledEpisodeSeeder.cs` — first-run sample episode installer.
- `Assets/Nightwire/Scripts/NightwireAtmosphereController.cs` — three procedural fog layers that drift independently across both archive and messenger screens.
- `Assets/Editor/NightwireProjectBootstrapper.cs` — generates the initial scene, panel settings, and bundled sample archive.
- `Assets/Nightwire/Scripts/Nightwire.Unity.asmdef` — presentation assembly boundary with explicit UI Toolkit, Video, Audio, and Unity Purchasing references. The portable `Nightwire.Runtime.dll` is staged as a precompiled plugin and auto-referenced by Unity.

## Current playable scope and remaining UI work

The Episode 001 story package now includes three character profiles (Anisha, Rafi, and Mira), three linked conversations, branching relationship choices, story flags, and a concluding investigation notification. The playable UI vertical slice supports:

- Episode library presentation and installed/offline episode discovery.\n- Three layered, procedurally generated fog banks drifting at different speeds behind the interface; no external fog asset or network download is required.
- Episode session start/resume and save-backed return to archive.
- Data-driven investigation screens for frequency tuning, sequence reconstruction, and evidence matching, using the portable minigame runtime and persisted attempt/timer state.
- Episode-local conversation inbox with participant/group identity, last-message previews, unread counts, completion state, and safe switching between already-opened threads without unlocking future story content.
- Timed, localized text conversations.
- Authored branching choices and relationship/story-flag effects.
- Inline previews of unlocked, manifest-verified images.
- Episode-authored social feed with localized post bodies, character names, comments, and verified image attachments.
- Evidence library listing only unlocked manifest assets, with image/audio/video playback and bounded UTF-8 text/Markdown/JSON previews; other document formats remain archive-only.
- Playback of unlocked, manifest-verified audio (MP3/WAV/OGG) and video attachments, copied to a bounded temporary cache and controlled in-message; unsupported audio formats are rejected rather than guessed.
- Prerecorded audio/video call presentation driven by the runtime timeline, including localized timed subtitles, speaker focus, allowlisted visual-effect cues, resumable elapsed position, and automatic call-completion story triggers.
- Offline installation of the bundled Episode 001 sample.

The following presentation features are not complete yet:

- Full call recording authoring/QA tooling and real-device synchronization validation.
- Full document viewer for PDF/office formats, plus advanced searchable media gallery filters.
- Group conversations identify the group channel, show a localized participant roster, and retain sender names on individual message bubbles.
- Player language settings and free-form replies.

The episode engine already has data contracts and runtime support for several of these content types; their Unity presentation is still outstanding. Do not mistake the existence of the runtime contract for a finished screen.

## Monetization baseline

Unity In-App Purchasing 5.5.0 is pinned in `Packages/manifest.json`. `NightwireIapService` uses the IAP 5.5 `PurchasingController` and local product catalogue. It deliberately disables purchase initiation until a production `INightwirePurchaseFulfillment` provider is assigned; pending orders are not confirmed unless the trusted provider reports durable verification and fulfillment. No client-side purchase success or currency grant is fabricated. The product-first economy and production release gates are documented in [monetization and store setup](../docs/monetization/ECONOMY_AND_STORE.md). Play Console products, the trusted backend verifier/ledger, refund handling, and any rewarded-ad provider still need configuration and end-to-end testing.

## Unity Build Automation cloud build setup

The Unity Build Automation target must run the runtime staging hook before Unity starts compiling scripts. In the target's **Advanced Settings → Script hooks → Pre-Build Script**, set the repository-relative path to:

```text
tools/prepare-unity-runtime.sh
```

The hook runs after repository checkout and before the Unity Editor process. On the Windows 11 24H2 builder it invokes the PowerShell staging script, which installs the .NET 10 SDK if necessary, publishes the portable runtime, and copies the runtime plus NuGet dependencies into `unity/Assets/Plugins/Nightwire/`. Do not omit this hook: without it, the presentation assembly cannot resolve `Nightwire.Runtime` or `System.Text.Json`.

The Unity package manifest explicitly includes the built-in Audio, UIElements, and Video modules, and the presentation assembly explicitly references those module assemblies plus `Unity.Purchasing`.

## Separate CI pipelines

- **Engine CI** builds the portable runtime and validator, validates the sample episode, and runs the 62 runtime regression tests plus validator integration tests.
- **Game UI CI** stages the runtime assemblies and checks Unity project metadata, UXML parsing, controller-to-UXML named-element contracts, stylesheet references, assembly references, verified audio/video integration contracts, and 25 UI source regression tests.

Game UI CI stages the runtime assemblies, validates UI source contracts, builds the Android StrongBox secure-store AAR with JDK 17 / Gradle 9.3.1 / Android SDK 36, verifies its JNI class and backup-exclusion resources, and uploads the AAR as the `nightwire-android-secure-store-aar` workflow artifact. It is still not a Unity Editor compile or Android player build. Unity Editor compilation, Play Mode testing, final manifest merge, and Android device/StrongBox verification require an actual Unity 6000.3.28f1 installation and device/toolchain execution.

See [episode library architecture](../docs/architecture/UNITY_EPISODE_LIBRARY_UI.md), [messenger architecture](../docs/architecture/MESSENGER_SCREEN.md), and [Android StrongBox notes](Android/NightwireSecureStore/README.md).


## Manual Android APK build

The repository includes a separate, manually triggered Unity Editor build workflow at `.github/workflows/android-apk.yml`. It uses GameCI Unity Builder v4 with the pinned Unity 6000.3.28f1 editor and a project-owned build method to generate a development APK (not an AAB). The method explicitly prepares the generated scene and bundled sample archive, then enables `BuildOptions.Development` so the offline development provider and sample seeder are available in this test build. It is not a production build. The workflow is deliberately manual so ordinary source/engine CI runs do not consume Unity build minutes.

For a Unity Personal license, configure these repository Actions secrets before running it:

- `UNITY_LICENSE`: the complete contents of the Unity Personal `.ulf` license file activated through Unity Hub.
- `UNITY_EMAIL`: the Unity account email associated with that license.
- `UNITY_PASSWORD`: the Unity account password, stored only as a GitHub Actions secret.

Then open **Actions → NIGHTWIRE Android APK → Run workflow**. A successful run uploads the `nightwire-android-apk` artifact. Do not commit license material or credentials. The first build is also an integration check: Unity must import the project, run the editor bootstrapper, compile all scripts/packages, generate the initial scene, and produce the Android player successfully.

The current workflow uses Unity's default Android signing configuration for a development/test APK. It is not a Play Store release-signed artifact. Production signing requires a separately provisioned Android keystore and password/alias secrets; never put those values in source control or chat. A successful CI artifact still needs installation and device smoke testing before release.


Call presentation advances its subtitle/effect timeline from the actual AudioSource or VideoPlayer playback position, and pauses/resumes call media with the application lifecycle. Physical-device decoder synchronization remains a release verification item.

The messenger now includes an episode-scoped contact directory with localized character names, statuses, and biographies. Contact browsing does not unlock conversations or alter story progress.


The Android build method is in `Assets/Editor/NightwireAndroidBuild.cs`. It calls the editor bootstrapper explicitly before collecting enabled scenes, then builds `build/Android/NIGHTWIRE.apk` with development flags. Android player settings are explicitly IL2CPP / ARM64, with minimum API 28 and target API 36. The workflow allows generated scene, Panel Settings, and StreamingAssets files created by the bootstrapper during the build. A successful workflow still needs physical-device smoke testing.

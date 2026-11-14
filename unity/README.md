# NIGHTWIRE Unity presentation integration

Status: first UI Toolkit screen source added; this directory is **not yet a complete Unity project**.

## Episode library screen

`Assets/Nightwire/Scripts/UI/EpisodeLibraryScreen.cs` provides the first runtime-backed episode library surface:

- NIGHTWIRE dark archive styling built with UI Toolkit.
- Refresh action that requests and accepts a signed release catalogue through `EpisodeReleaseManager`.
- Episode cards for available, installed/offline, update-available, unlisted, and invalid packages.
- Download/update action with byte progress and cancellation on screen destruction.
- Open action exposed as `OpenEpisodeRequested`; the application composition root must connect it to the session coordinator.

The screen deliberately does not create or own the release manager. Signing keys, protected anti-rollback storage, HTTP transport, save storage, and session/install exclusion are application infrastructure and must be injected by the host.

## Importing the portable runtime

The runtime core currently lives at `../src/Nightwire.Runtime` and targets .NET Standard 2.1. Before importing this UI source into a Unity 6.3 LTS project:

1. Build `src/Nightwire.Runtime/Nightwire.Runtime.csproj` with a compatible .NET SDK.
2. Copy the resulting `Nightwire.Runtime.dll` into the Unity project's `Assets/Plugins/Nightwire/` folder.
3. Confirm the plugin is enabled for the intended Android and Editor platforms.
4. Add this directory's `Assets/Nightwire/Scripts/UI` contents to the Unity project's Assets tree.
5. Create a scene with a `UIDocument`, assign a Panel Settings asset, and attach `EpisodeLibraryScreen` to the same GameObject.
6. In the application composition root, construct the release manager using the trusted-key verifier, protected sequence store, HTTPS catalogue transport, persistent install/download paths, and an atomic install/session lease provider. Call `RecoverAtStartup()` before allowing sessions to start.
7. Call `Configure(manager)` on the screen and subscribe to `OpenEpisodeRequested`. The handler should resume an existing save or start a new session through `RuntimeSessionCoordinator`; do not overwrite an existing save.
8. Refresh the catalogue when online. The library can still list locally verified installed packages while offline.

The `IEpisodeInstallLeaseProvider` implementation must share synchronization with session start/close. A non-atomic `IsActive` check is not sufficient to protect an update from racing with a new session.

## Current boundary

This source is a Unity UI Toolkit integration component, not a ready-to-build Unity application. The repository still needs a Unity project manifest, ProjectSettings, scene/Panel Settings assets, Android platform configuration, production secure-storage and transport adapters, and device verification. No Unity Editor build or Android test has been run.

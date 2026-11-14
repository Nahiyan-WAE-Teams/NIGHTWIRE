# NIGHTWIRE Android StrongBox sequence store

This Android library implements `IReleaseCatalogueSequenceStore` through the Java bridge called by `AndroidNightwirePlatformServices`.

## Security design

- Requires Android API 28 or later and a device with StrongBox Keymaster support.
- Generates non-exportable AES-256 keys in `AndroidKeyStore` with StrongBox explicitly required.
- Encrypts the accepted sequence with AES-GCM and binds ciphertext to the app namespace and key alias using authenticated additional data.
- Serializes load and update operations at the Java class boundary so concurrent callers cannot race sequence reads, key rotation, and preference commits.
- Validates the stored key alias against the generated UUID format before accessing AndroidKeyStore.
- Rotates the key for every accepted sequence. It deletes the previous hardware key before committing the new preference record. Replaying an old preferences snapshot then points to a key that has been deleted.
- If StrongBox is unavailable, a key is missing, a record is corrupt, or persistence fails, the provider throws. It never silently falls back to plaintext or software-backed keys.

This protects the sequence against ordinary app-data preference edits/restores, subject to the Android StrongBox implementation and device security state. It is not a substitute for a server-side monotonic release sequence or platform attestation against a fully compromised/rooted device. Catalogue signatures remain mandatory. A failed read must block accepting a new catalogue; do not reset the sequence to zero as a recovery shortcut.

## Build

The library is an Android Gradle library. The project is aligned to the Unity 6.3 LTS toolchain documented for Unity 6000.3.26f1 and later: Gradle 9.3.1 / Android Gradle Plugin 9.1.1. Check the exact compatibility table for the Unity Editor patch in use before building.

From this directory, with a compatible JDK and Gradle installation:

```bash
gradle assembleRelease
```

The output AAR is under `build/outputs/aar/`. Import the AAR into the Unity project under `Assets/Plugins/Android/`. Ensure the Unity Android min SDK is at least API 28. The Unity C# bridge expects this exact Java class and static method contract:

- `com.nightwire.securestore.NightwireSequenceStore.loadSequence(Context, String): long`
- `com.nightwire.securestore.NightwireSequenceStore.saveSequence(Context, String, long): void`

The application manifest must not back up this preference file to another device. Even if an old encrypted preference snapshot is restored, the StrongBox key should be unavailable on a different device and the provider will fail closed. Do not add a backup exclusion that causes the sequence to be silently reset; inability to read the sequence must require an explicit recovery/reprovisioning path.

## Device verification still required

No AAR has been built or imported yet. Verify on at least one StrongBox-capable Android device and one device without StrongBox. The first should pass persistence/rotation/replay tests; the second must fail closed. Test process death between key deletion and preference commit, corrupt preferences, reinstall/restore behavior, Android backup behavior, and IL2CPP JNI calls before production release.

using System;
using System.Collections.Generic;
using Nightwire.Runtime.Installation;
using UnityEngine;

namespace Nightwire.Unity
{
    /// <summary>
    /// Android release configuration. StrongBox support is mandatory: unsupported devices
    /// fail closed instead of silently using ordinary preferences or software-only keys.
    /// </summary>
    public sealed class AndroidNightwirePlatformServices : NightwirePlatformServicesBehaviour
    {
        [Serializable]
        private sealed class PublicKeyEntry
        {
            public string keyId;
            [TextArea] public string subjectPublicKeyInfoBase64;
        }

        [SerializeField] private string catalogueUrl;
        [SerializeField] private string sequenceNamespace = "production";
        [SerializeField] private PublicKeyEntry[] trustedKeys = Array.Empty<PublicKeyEntry>();

        public override bool ShouldRefreshCatalogue => true;

        public override Uri CatalogueUri
        {
            get
            {
                if (!Uri.TryCreate(catalogueUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidOperationException("A valid HTTPS release-catalogue URL must be configured.");
                return uri;
            }
        }

        public override IReadOnlyList<TrustedReleaseKey> GetTrustedReleaseKeys()
        {
            var result = new List<TrustedReleaseKey>();
            foreach (var entry in trustedKeys ?? Array.Empty<PublicKeyEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.keyId) ||
                    string.IsNullOrWhiteSpace(entry.subjectPublicKeyInfoBase64))
                    continue;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(entry.subjectPublicKeyInfoBase64); }
                catch (FormatException exception)
                {
                    throw new InvalidOperationException("A trusted release public key is not valid Base64.", exception);
                }
                result.Add(new TrustedReleaseKey { KeyId = entry.keyId, SubjectPublicKeyInfo = bytes });
            }
            if (result.Count == 0)
                throw new InvalidOperationException("No trusted ECDSA P-256 public keys are configured.");
            return result;
        }

        public override IReleaseCatalogueSequenceStore CreateCatalogueSequenceStore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrWhiteSpace(sequenceNamespace))
                throw new InvalidOperationException("A catalogue sequence namespace is required.");
            return new AndroidStrongBoxSequenceStore(sequenceNamespace);
#else
            throw new PlatformNotSupportedException("Android StrongBox sequence storage is available only in an Android player build.");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidStrongBoxSequenceStore : IReleaseCatalogueSequenceStore
        {
            private const string JavaClass = "com.nightwire.securestore.NightwireSequenceStore";
            private readonly string _namespace;

            public AndroidStrongBoxSequenceStore(string storeNamespace) { _namespace = storeNamespace; }

            public long LoadHighestAcceptedSequence()
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var store = new AndroidJavaClass(JavaClass))
                    return store.CallStatic<long>("loadSequence", activity, _namespace);
            }

            public void SaveHighestAcceptedSequence(long sequence)
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var store = new AndroidJavaClass(JavaClass))
                    store.CallStatic("saveSequence", activity, _namespace, sequence);
            }
        }
#endif
    }
}

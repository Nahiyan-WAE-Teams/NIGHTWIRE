using System;
using System.Collections.Generic;
using Nightwire.Runtime.Installation;
using UnityEngine;

namespace Nightwire.Unity
{
    /// <summary>
    /// Platform-owned security and release configuration boundary.
    /// Implementations must obtain sequence state from rollback-resistant storage
    /// and return only app-provisioned ECDSA P-256 public keys.
    /// </summary>
    public interface INightwirePlatformServices
    {
        Uri CatalogueUri { get; }
        IReadOnlyList<TrustedReleaseKey> GetTrustedReleaseKeys();
        IReleaseCatalogueSequenceStore CreateCatalogueSequenceStore();
    }

    /// <summary>
    /// Attach a platform-specific implementation to the application bootstrap object.
    /// The base class intentionally provides no insecure storage or key defaults.
    /// </summary>
    public abstract class NightwirePlatformServicesBehaviour : MonoBehaviour, INightwirePlatformServices
    {
        public abstract Uri CatalogueUri { get; }
        public abstract IReadOnlyList<TrustedReleaseKey> GetTrustedReleaseKeys();
        public abstract IReleaseCatalogueSequenceStore CreateCatalogueSequenceStore();
    }
}

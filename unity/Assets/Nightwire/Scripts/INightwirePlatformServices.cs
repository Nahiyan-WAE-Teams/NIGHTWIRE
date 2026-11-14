using System;
using System.Collections.Generic;
using Nightwire.Runtime.Installation;
using UnityEngine;

namespace Nightwire.Unity
{
    /// <summary>
    /// Platform-owned release configuration boundary.
    /// A production provider must fail closed unless it can protect the accepted sequence
    /// against preference rollback and return app-provisioned ECDSA P-256 public keys.
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

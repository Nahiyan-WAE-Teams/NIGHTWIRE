using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Nightwire.Runtime.Installation;
using UnityEngine;

namespace Nightwire.Unity
{
    /// <summary>
    /// Offline-only provider for the bundled Episode 001 development scene.
    /// It deliberately does not refresh a remote catalogue and is disabled in non-development players.
    /// Replace this component with AndroidNightwirePlatformServices for a production Android build.
    /// </summary>
    public sealed class NightwireDevelopmentPlatformServices : NightwirePlatformServicesBehaviour
    {
        private static readonly Uri OfflineCatalogue = new Uri("https://offline.invalid/nightwire/releases.json");

        public override Uri CatalogueUri => OfflineCatalogue;
        public override bool ShouldRefreshCatalogue => false;

        public override IReadOnlyList<TrustedReleaseKey> GetTrustedReleaseKeys()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                return new[]
                {
                    new TrustedReleaseKey
                    {
                        KeyId = "nightwire-development-ephemeral",
                        SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo()
                    }
                };
            }
#else
            throw new PlatformNotSupportedException(
                "The offline development provider is disabled in release players. Configure AndroidNightwirePlatformServices.");
#endif
        }

        public override IReleaseCatalogueSequenceStore CreateCatalogueSequenceStore()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return new DevelopmentSequenceStore(Path.Combine(
                Application.persistentDataPath, "Nightwire", "development-catalogue-sequence.txt"));
#else
            throw new PlatformNotSupportedException(
                "The offline development provider is disabled in release players. Configure AndroidNightwirePlatformServices.");
#endif
        }

        private sealed class DevelopmentSequenceStore : IReleaseCatalogueSequenceStore
        {
            private readonly string _path;
            public DevelopmentSequenceStore(string path) { _path = path; }

            public long LoadHighestAcceptedSequence()
            {
                if (!File.Exists(_path)) return 0;
                string value = File.ReadAllText(_path);
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) ||
                    sequence < 0)
                    throw new InvalidDataException("Development catalogue sequence file is invalid.");
                return sequence;
            }

            public void SaveHighestAcceptedSequence(long sequence)
            {
                if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
                string directory = Path.GetDirectoryName(_path);
                if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Development sequence path is invalid.");
                Directory.CreateDirectory(directory);
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, sequence.ToString(CultureInfo.InvariantCulture));
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
        }
    }
}

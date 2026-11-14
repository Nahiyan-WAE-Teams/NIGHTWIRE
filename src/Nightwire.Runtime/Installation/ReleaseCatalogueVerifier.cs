using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nightwire.Runtime.Installation
{
    public sealed class TrustedReleaseKey
    {
        public string KeyId { get; set; } = string.Empty;
        // DER SubjectPublicKeyInfo for an ECDSA P-256 public key. Private keys never ship in the client.
        public byte[] SubjectPublicKeyInfo { get; set; } = Array.Empty<byte>();
    }

    public sealed class EpisodeReleaseEntry
    {
        public string EpisodeId { get; internal set; } = string.Empty;
        public string EpisodeVersion { get; internal set; } = string.Empty;
        public Uri ArchiveUrl { get; internal set; } = null!;
        public long ArchiveSizeBytes { get; internal set; }
        public string ArchiveSha256 { get; internal set; } = string.Empty;
        public string MinimumRuntimeVersion { get; internal set; } = string.Empty;
    }

    public sealed class VerifiedReleaseCatalogue
    {
        public long Sequence { get; internal set; }
        public DateTime IssuedUtc { get; internal set; }
        public DateTime ExpiresUtc { get; internal set; }
        public string SigningKeyId { get; internal set; } = string.Empty;
        public IReadOnlyList<EpisodeReleaseEntry> Releases { get; internal set; } = Array.Empty<EpisodeReleaseEntry>();
    }

    public sealed class ReleaseCatalogueResult
    {
        public bool Success { get; internal set; }
        public VerifiedReleaseCatalogue? Catalogue { get; internal set; }
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
    }

    /// <summary>
    /// Verifies a detached ECDSA P-256/SHA-256 signature over the exact UTF-8 payload bytes.
    /// Trusted public keys are embedded/provisioned independently from the downloaded envelope.
    /// </summary>
    public sealed class ReleaseCatalogueVerifier
    {
        private readonly Dictionary<string, byte[]> _trustedKeys;
        private readonly long _maximumEnvelopeBytes;
        private readonly long _maximumPayloadBytes;
        private readonly int _maximumReleases;

        public ReleaseCatalogueVerifier(IEnumerable<TrustedReleaseKey> trustedKeys,
            long maximumEnvelopeBytes = 2 * 1024 * 1024,
            long maximumPayloadBytes = 1024 * 1024,
            int maximumReleases = 256)
        {
            if (trustedKeys == null) throw new ArgumentNullException(nameof(trustedKeys));
            if (maximumEnvelopeBytes <= 0 || maximumPayloadBytes <= 0 || maximumReleases <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumEnvelopeBytes));
            _maximumEnvelopeBytes = maximumEnvelopeBytes;
            _maximumPayloadBytes = maximumPayloadBytes;
            _maximumReleases = maximumReleases;
            _trustedKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var key in trustedKeys)
            {
                if (key == null || string.IsNullOrWhiteSpace(key.KeyId) ||
                    key.SubjectPublicKeyInfo == null || key.SubjectPublicKeyInfo.Length == 0 ||
                    !_trustedKeys.TryAdd(key.KeyId, (byte[])key.SubjectPublicKeyInfo.Clone()))
                    throw new ArgumentException("Trusted release keys must have unique IDs and valid public key bytes.", nameof(trustedKeys));
            }
        }

        public ReleaseCatalogueResult Verify(byte[] envelopeBytes, DateTime utcNow, long minimumSequence = 0)
        {
            try
            {
                if (envelopeBytes == null || envelopeBytes.Length == 0 || envelopeBytes.Length > _maximumEnvelopeBytes)
                    return Error("envelope_size_invalid", "Release catalogue envelope is empty or too large.");
                if (utcNow.Kind != DateTimeKind.Utc)
                    return Error("clock_must_be_utc", "Verification time must be supplied as UTC.");

                using var envelope = JsonDocument.Parse(envelopeBytes);
                var root = envelope.RootElement;
                string keyId = String(root, "key_id");
                string algorithm = String(root, "algorithm");
                if (!string.Equals(algorithm, "ECDSA-P256-SHA256", StringComparison.Ordinal))
                    return Error("signature_algorithm_unsupported", "Release catalogue signature algorithm is not supported.");
                if (!_trustedKeys.TryGetValue(keyId, out var publicKey))
                    return Error("signing_key_untrusted", "Release catalogue was signed by an unknown key.");

                byte[] payload = Convert.FromBase64String(String(root, "payload"));
                byte[] signature = Convert.FromBase64String(String(root, "signature"));
                if (payload.Length == 0 || payload.Length > _maximumPayloadBytes || signature.Length != 64)
                    return Error("signed_payload_invalid", "Signed payload or ECDSA signature has an invalid size.");

                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
                    if (bytesRead != publicKey.Length || ecdsa.KeySize != 256)
                        return Error("trusted_key_invalid", "Trusted key must be a complete ECDSA P-256 SubjectPublicKeyInfo key.");
                    if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
                        return Error("signature_invalid", "Release catalogue signature verification failed.");
                }

                using var payloadDocument = JsonDocument.Parse(payload);
                var data = payloadDocument.RootElement;
                if (Integer(data, "schema_version") != 1)
                    return Error("catalogue_schema_unsupported", "Release catalogue schema version is unsupported.");
                long sequence = Long(data, "sequence");
                if (sequence < 1 || sequence < minimumSequence)
                    return Error("catalogue_rollback", "Release catalogue sequence is older than the minimum accepted sequence.");

                DateTime issued = DateTime.Parse(String(data, "issued_utc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                DateTime expires = DateTime.Parse(String(data, "expires_utc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                if (expires <= issued || issued > utcNow.AddMinutes(10) || expires <= utcNow)
                    return Error("catalogue_expired_or_invalid", "Release catalogue is expired or has an invalid validity window.");

                var releasesElement = data.GetProperty("releases");
                if (releasesElement.ValueKind != JsonValueKind.Array || releasesElement.GetArrayLength() > _maximumReleases)
                    return Error("release_count_invalid", "Release catalogue contains an invalid number of releases.");
                var releases = new List<EpisodeReleaseEntry>();
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in releasesElement.EnumerateArray())
                {
                    string episodeId = String(item, "episode_id");
                    string version = String(item, "episode_version");
                    string identity = episodeId + "@" + version;
                    if (!SafeId(episodeId) || !SafeVersion(version) || !identities.Add(identity))
                        return Error("release_identity_invalid", "Release catalogue contains an unsafe or duplicate release identity.");
                    if (!Uri.TryCreate(String(item, "archive_url"), UriKind.Absolute, out var url) ||
                        url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo))
                        return Error("release_url_invalid", "Every release archive URL must be HTTPS and contain no embedded credentials.");
                    long size = Long(item, "archive_size_bytes");
                    string hash = String(item, "archive_sha256");
                    if (size <= 0 || !IsSha256(hash))
                        return Error("release_archive_metadata_invalid", "Release archive size or SHA-256 is invalid.");
                    releases.Add(new EpisodeReleaseEntry
                    {
                        EpisodeId = episodeId, EpisodeVersion = version, ArchiveUrl = url,
                        ArchiveSizeBytes = size, ArchiveSha256 = hash.ToLowerInvariant(),
                        MinimumRuntimeVersion = String(item, "minimum_runtime_version")
                    });
                }

                return new ReleaseCatalogueResult
                {
                    Success = true,
                    Catalogue = new VerifiedReleaseCatalogue
                    {
                        Sequence = sequence, IssuedUtc = issued, ExpiresUtc = expires,
                        SigningKeyId = keyId, Releases = releases.AsReadOnly()
                    }
                };
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException ||
                exception is CryptographicException || exception is ArgumentException || exception is InvalidOperationException ||
                exception is KeyNotFoundException || exception is OverflowException)
            {
                return Error("catalogue_invalid", exception.Message);
            }
        }

        private static string String(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
                throw new FormatException("Required catalogue field '" + name + "' is missing.");
            return value.GetString()!;
        }

        private static long Long(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || !value.TryGetInt64(out long result))
                throw new FormatException("Required integer catalogue field '" + name + "' is invalid.");
            return result;
        }

        private static int Integer(JsonElement element, string name) => checked((int)Long(element, name));
        private static bool SafeId(string value) => value.Length <= 64 && value != "." && value != ".." &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-');
        private static bool SafeVersion(string value) => value.Length <= 64 &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '+' || c == '-' );
        private static bool IsSha256(string value) => value.Length == 64 &&
            value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        private static ReleaseCatalogueResult Error(string code, string message) =>
            new ReleaseCatalogueResult { Success = false, ErrorCode = code, Error = message };
    }
}

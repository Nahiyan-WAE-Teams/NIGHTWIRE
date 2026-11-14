using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Nightwire.Runtime.Domain
{
    public sealed class EpisodePackageDescriptor
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string EpisodeId { get; set; } = string.Empty;
        public string EpisodeVersion { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string? DescriptionKey { get; set; }
        public string MinimumRuntimeVersion { get; set; } = string.Empty;
        public string EntryEventId { get; set; } = string.Empty;
        public string DefaultLocale { get; set; } = string.Empty;
        public List<string> SupportedLocales { get; } = new List<string>();
    }

    public sealed class EpisodeManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    public sealed class EpisodeManifestAsset
    {
        public string AssetId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
    }

    public sealed class EpisodeManifest
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string EpisodeId { get; set; } = string.Empty;
        public List<EpisodeManifestFile> Files { get; } = new List<EpisodeManifestFile>();
        public List<EpisodeManifestAsset> Assets { get; } = new List<EpisodeManifestAsset>();
    }

    /// <summary>
    /// Fully verified, immutable view of an unpacked episode package.
    /// JsonElement values are cloned and remain valid after loading completes.
    /// </summary>
    public sealed class LoadedEpisodePackage
    {
        public string RootDirectory { get; internal set; } = string.Empty;
        public EpisodePackageDescriptor Descriptor { get; internal set; } = new EpisodePackageDescriptor();
        public EpisodeManifest Manifest { get; internal set; } = new EpisodeManifest();
        public EpisodeRuntimeDefinition RuntimeDefinition { get; internal set; } = new EpisodeRuntimeDefinition();

        public Dictionary<string, JsonElement> CharacterProfiles { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Conversations { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> CharacterPosts { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Calls { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> Minigames { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public Dictionary<string, JsonElement> EventDocuments { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> Localization { get; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public Dictionary<string, EpisodeManifestAsset> AssetsById { get; } = new Dictionary<string, EpisodeManifestAsset>(StringComparer.Ordinal);
    }

    public sealed class EpisodePackageLoadException : Exception
    {
        public string ErrorCode { get; }

        public EpisodePackageLoadException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public EpisodePackageLoadException(string errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    public sealed class EpisodePackageLoaderOptions
    {
        public string RuntimeVersion { get; set; } = "0.1.0";
        public long MaximumPackageBytes { get; set; } = 536870912;
        public int MaximumJsonFileBytes { get; set; } = 8388608;
        public int MaximumJsonDepth { get; set; } = 64;
    }
}

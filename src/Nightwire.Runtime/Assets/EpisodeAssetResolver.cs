using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Nightwire.Runtime.Domain;

namespace Nightwire.Runtime.Assets
{
    /// <summary>
    /// Opens only manifest-declared episode assets that the player has explicitly unlocked.
    /// The returned stream is read-only and the authored package file is never modified.
    /// </summary>
    public sealed class EpisodeAssetResolver
    {
        private readonly LoadedEpisodePackage _package;
        private readonly EpisodeProgressState _progress;
        private readonly long _maximumAssetBytes;

        public EpisodeAssetResolver(LoadedEpisodePackage package, EpisodeProgressState progress,
            long maximumAssetBytes = 268435456)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            if (maximumAssetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAssetBytes));
            if (!string.Equals(package.Descriptor.EpisodeId, progress.EpisodeId, StringComparison.Ordinal))
                throw new ArgumentException("Progress state belongs to a different episode.", nameof(progress));
            _maximumAssetBytes = maximumAssetBytes;
        }

        public bool IsUnlocked(string assetId) =>
            !string.IsNullOrWhiteSpace(assetId) &&
            _package.AssetsById.ContainsKey(assetId) &&
            _progress.UnlockedAssetIds.Contains(assetId);

        public EpisodeAssetOpenResult Open(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId))
                return Fail("asset_id_required", "An asset ID is required.");
            if (!_package.AssetsById.TryGetValue(assetId, out var asset))
                return Fail("asset_not_found", $"Asset '{assetId}' is not declared by this episode.");
            if (!_progress.UnlockedAssetIds.Contains(assetId))
                return Fail("asset_locked", $"Asset '{assetId}' has not been unlocked.");

            var file = _package.Manifest.Files.FirstOrDefault(x =>
                string.Equals(x.Path, asset.Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Kind, "media", StringComparison.Ordinal));
            if (file == null)
                return Fail("asset_manifest_entry_missing", $"Asset '{assetId}' has no matching media file entry.");

            FileStream? stream = null;
            try
            {
                string root = Path.GetFullPath(_package.RootDirectory);
                EnsureNotLink(root, "episode root");
                string fullPath = ResolvePath(root, asset.Path);
                EnsurePathHasNoLinks(root, fullPath);
                stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != file.SizeBytes)
                    return DisposeAndFail(stream, "asset_size_mismatch", $"Asset '{assetId}' no longer matches its verified manifest size.");
                if (stream.Length > _maximumAssetBytes)
                    return DisposeAndFail(stream, "asset_too_large", $"Asset '{assetId}' exceeds the configured asset size limit.");

                using (var sha = SHA256.Create())
                {
                    string digest = ToHex(sha.ComputeHash(stream));
                    if (!string.Equals(digest, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        return DisposeAndFail(stream, "asset_hash_mismatch", $"Asset '{assetId}' changed after package verification.");
                }

                stream.Position = 0;
                var handle = new EpisodeAssetHandle(asset.AssetId, asset.Type, asset.MimeType,
                    asset.Path, stream.Length, stream);
                stream = null;
                return new EpisodeAssetOpenResult { Success = true, Asset = handle };
            }
            catch (FileNotFoundException)
            {
                return DisposeAndFail(stream, "asset_file_missing", $"Asset file for '{assetId}' is missing.");
            }
            catch (DirectoryNotFoundException)
            {
                return DisposeAndFail(stream, "asset_file_missing", $"Asset directory for '{assetId}' is missing.");
            }
            catch (UnauthorizedAccessException exception)
            {
                return DisposeAndFail(stream, "asset_access_denied", exception.Message);
            }
            catch (IOException exception)
            {
                return DisposeAndFail(stream, "asset_read_failed", exception.Message);
            }
            catch (ArgumentException exception)
            {
                return DisposeAndFail(stream, "asset_path_invalid", exception.Message);
            }
        }

        private static string ResolvePath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal) ||
                relativePath.Contains("\\") || relativePath.Contains(":") || relativePath.Contains("\0"))
                throw new ArgumentException("Asset path is not a safe relative path.");
            foreach (string segment in relativePath.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new ArgumentException("Asset path contains an unsafe segment.");

            string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Asset path escapes the episode root.");
            return full;
        }

        private static void EnsurePathHasNoLinks(string root, string fullPath)
        {
            string relative = fullPath.Substring(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = root;
            foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                    throw new FileNotFoundException("Asset path component is missing.", current);
                EnsureNotLink(current, current);
            }
        }

        private static void EnsureNotLink(string path, string description)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException($"Symbolic links and reparse points are not allowed: {description}.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                    break;
                current = parent;
            }
        }

        private static EpisodeAssetOpenResult DisposeAndFail(FileStream? stream, string code, string message)
        {
            stream?.Dispose();
            return Fail(code, message);
        }

        private static EpisodeAssetOpenResult Fail(string code, string message) =>
            new EpisodeAssetOpenResult { Success = false, ErrorCode = code, Error = message };

        private static string ToHex(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    public sealed class EpisodeAssetOpenResult
    {
        public bool Success { get; internal set; }
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
        public EpisodeAssetHandle? Asset { get; internal set; }
    }

    public sealed class EpisodeAssetHandle : IDisposable
    {
        public string AssetId { get; }
        public string Type { get; }
        public string MimeType { get; }
        public string RelativePath { get; }
        public long SizeBytes { get; }
        public Stream Content { get; }

        internal EpisodeAssetHandle(string assetId, string type, string mimeType,
            string relativePath, long sizeBytes, Stream content)
        {
            AssetId = assetId;
            Type = type;
            MimeType = mimeType;
            RelativePath = relativePath;
            SizeBytes = sizeBytes;
            Content = content;
        }

        public void Dispose() => Content.Dispose();
    }
}

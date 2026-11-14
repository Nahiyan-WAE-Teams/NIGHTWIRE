using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Loading;

namespace Nightwire.Runtime.Installation
{
    public sealed class EpisodeInstallerOptions
    {
        public long MaximumArchiveBytes { get; set; } = 536870912;
        public long MaximumExpandedBytes { get; set; } = 536870912;
        public int MaximumEntries { get; set; } = 20000;
        public double MaximumCompressionRatio { get; set; } = 200;
    }

    public sealed class EpisodeInstallResult
    {
        public bool Success { get; internal set; }
        public bool UpdatedExisting { get; internal set; }
        public string? EpisodeId { get; internal set; }
        public string? EpisodeVersion { get; internal set; }
        public string? InstallDirectory { get; internal set; }
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
    }

    /// <summary>
    /// Installs verified ZIP episode packages using a staging directory and rollback-capable directory moves.
    /// ZIP entries are treated as untrusted input; no archive path is extracted directly.
    /// </summary>
    public sealed class EpisodePackageInstaller
    {
        private readonly EpisodeInstallerOptions _options;
        private readonly EpisodePackageLoader _loader;

        public EpisodePackageInstaller(EpisodeInstallerOptions? options = null, EpisodePackageLoader? loader = null)
        {
            _options = options ?? new EpisodeInstallerOptions();
            _loader = loader ?? new EpisodePackageLoader();
            if (_options.MaximumArchiveBytes <= 0 || _options.MaximumExpandedBytes <= 0 ||
                _options.MaximumEntries < 2 || _options.MaximumCompressionRatio < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Installer limits must be positive.");
        }

        public EpisodeInstallResult InstallArchive(string archivePath, string installRoot)
        {
            string? staging = null;
            string? backup = null;
            try
            {
                if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                    throw Fail("archive_not_found", "Episode archive does not exist.");
                var archiveInfo = new FileInfo(archivePath);
                if (archiveInfo.Length <= 0 || archiveInfo.Length > _options.MaximumArchiveBytes)
                    throw Fail("archive_size_invalid", "Episode archive is empty or exceeds the configured archive limit.");

                string root = Path.GetFullPath(installRoot);
                Directory.CreateDirectory(root);
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    throw Fail("unsafe_install_root", "Install root cannot be a symbolic link or reparse point.");

                staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                string episodeId;
                string episodeVersion;
                ExtractVerifiedArchive(archivePath, staging, out episodeId, out episodeVersion);

                LoadedEpisodePackage loaded;
                try { loaded = _loader.Load(staging); }
                catch (EpisodePackageLoadException exception)
                { throw Fail("package_validation_failed", exception.Message); }

                episodeId = loaded.Descriptor.EpisodeId;
                episodeVersion = loaded.Descriptor.EpisodeVersion;
                string destination = Path.Combine(root, episodeId);
                EnsureSafeEpisodeId(episodeId);
                bool updated = Directory.Exists(destination);
                if (updated && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    throw Fail("unsafe_install_destination", "Existing episode destination cannot be a symbolic link or reparse point.");
                if (File.Exists(destination))
                    throw Fail("install_destination_conflict", "Episode destination exists as a file.");

                if (updated)
                {
                    backup = Path.Combine(root, ".backup-" + episodeId + "-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(destination, backup);
                }

                try
                {
                    Directory.Move(staging, destination);
                    staging = null;
                }
                catch
                {
                    if (backup != null && Directory.Exists(backup) && !Directory.Exists(destination))
                        Directory.Move(backup, destination);
                    backup = null;
                    throw;
                }

                if (backup != null && Directory.Exists(backup))
                {
                    try { Directory.Delete(backup, true); }
                    catch { /* A valid new install remains active; stale backup can be cleaned later. */ }
                }

                return new EpisodeInstallResult
                {
                    Success = true, UpdatedExisting = updated, EpisodeId = episodeId,
                    EpisodeVersion = episodeVersion, InstallDirectory = destination
                };
            }
            catch (EpisodeInstallException exception)
            {
                return Error(exception.ErrorCode, exception.Message);
            }
            catch (InvalidDataException exception) { return Error("invalid_archive", exception.Message); }
            catch (JsonException exception) { return Error("invalid_package_json", exception.Message); }
            catch (UnauthorizedAccessException exception) { return Error("install_access_denied", exception.Message); }
            catch (IOException exception) { return Error("install_io_failed", exception.Message); }
            catch (Exception exception) { return Error("install_failed", exception.Message); }
            finally
            {
                if (staging != null && Directory.Exists(staging))
                {
                    try { Directory.Delete(staging, true); } catch { }
                }
                if (backup != null && Directory.Exists(backup))
                {
                    // Keep the previous complete install if cleanup or rollback could not finish.
                }
            }
        }

        private void ExtractVerifiedArchive(string archivePath, string staging, out string episodeId, out string episodeVersion)
        {
            using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read, false))
            {
                if (zip.Entries.Count > _options.MaximumEntries)
                    throw Fail("too_many_archive_entries", "Archive contains too many entries.");

                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName;
                    if (name.EndsWith("/", StringComparison.Ordinal))
                    {
                        ValidateArchivePath(name.Substring(0, name.Length - 1));
                        continue;
                    }
                    ValidateArchivePath(name);
                    if (!entries.TryAdd(name, entry))
                        throw Fail("duplicate_archive_path", "Archive contains duplicate or case-colliding paths.");
                    int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixMode == 0xA000)
                        throw Fail("archive_symlink_rejected", "Symbolic links are not permitted in episode archives.");
                }

                if (!entries.TryGetValue("episode.json", out var descriptorEntry) ||
                    !entries.TryGetValue("manifest.json", out var manifestEntry))
                    throw Fail("package_metadata_missing", "Archive must contain episode.json and manifest.json.");

                using var descriptorDoc = ReadBoundedJson(descriptorEntry, 1024 * 1024);
                using var manifestDoc = ReadBoundedJson(manifestEntry, 8 * 1024 * 1024);
                var descriptor = descriptorDoc.RootElement;
                var manifest = manifestDoc.RootElement;
                episodeId = RequiredString(descriptor, "episode_id");
                episodeVersion = RequiredString(descriptor, "episode_version");
                EnsureSafeEpisodeId(episodeId);
                if (!string.Equals(RequiredString(descriptor, "manifest"), "manifest.json", StringComparison.Ordinal))
                    throw Fail("invalid_manifest_name", "Format 0.1 requires manifest.json.");

                var expected = new Dictionary<string, ExpectedFile>(StringComparer.OrdinalIgnoreCase);
                long totalExpanded = 0;
                foreach (var item in manifest.GetProperty("files").EnumerateArray())
                {
                    string path = RequiredString(item, "path");
                    ValidateArchivePath(path);
                    long size = item.GetProperty("size_bytes").GetInt64();
                    string hash = RequiredString(item, "sha256");
                    if (size < 0 || !IsSha256(hash) || !expected.TryAdd(path, new ExpectedFile(size, hash)))
                        throw Fail("invalid_manifest_inventory", "Manifest contains an invalid or duplicate file entry.");
                    checked { totalExpanded += size; }
                    if (totalExpanded > _options.MaximumExpandedBytes)
                        throw Fail("expanded_size_limit", "Manifest expanded size exceeds the configured limit.");
                }

                if (!expected.ContainsKey("episode.json"))
                    throw Fail("descriptor_not_in_manifest", "episode.json must be listed and hashed by manifest.json.");
                var allFiles = new HashSet<string>(expected.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (string name in entries.Keys)
                    if (!allFiles.Contains(name) && !string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase))
                        throw Fail("unlisted_archive_entry", $"Archive contains an unlisted file '{name}'.");
                foreach (string path in expected.Keys)
                    if (!entries.ContainsKey(path))
                        throw Fail("archive_file_missing", $"Archive is missing manifest file '{path}'.");

                long actualExpanded = manifestEntry.Length;
                foreach (var pair in expected)
                {
                    var entry = entries[pair.Key];
                    if (entry.Length != pair.Value.Size)
                        throw Fail("archive_entry_size_mismatch", $"Archive entry size does not match manifest for '{pair.Key}'.");
                    checked { actualExpanded += entry.Length; }
                    if (actualExpanded > _options.MaximumExpandedBytes)
                        throw Fail("expanded_size_limit", "Archive expanded size exceeds the configured limit.");
                    if (entry.CompressedLength == 0 && entry.Length > 0 ||
                        entry.CompressedLength > 0 && (double)entry.Length / entry.CompressedLength > _options.MaximumCompressionRatio)
                        throw Fail("compression_ratio_limit", $"Archive entry '{pair.Key}' exceeds the compression-ratio limit.");
                }

                WriteEntry(manifestEntry, Path.Combine(staging, "manifest.json"), 8 * 1024 * 1024, null);
                foreach (var pair in expected)
                {
                    string destination = ResolveUnderRoot(staging, pair.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    WriteEntry(entries[pair.Key], destination, pair.Value.Size, pair.Value.Sha256);
                }
            }
        }

        private static void WriteEntry(ZipArchiveEntry entry, string destination, long maximumBytes, string? expectedHash)
        {
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var sha = expectedHash == null ? null : SHA256.Create();
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maximumBytes) throw Fail("entry_expanded_limit", "Archive entry expanded beyond its allowed size.");
                output.Write(buffer, 0, read);
                sha?.TransformBlock(buffer, 0, read, buffer, 0);
            }
            output.Flush();
            if (expectedHash != null)
            {
                sha!.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (total != maximumBytes || !string.Equals(ToHex(sha.Hash!), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw Fail("archive_entry_hash_mismatch", $"Archive entry failed size or SHA-256 verification: '{entry.FullName}'.");
            }
        }

        private static JsonDocument ReadBoundedJson(ZipArchiveEntry entry, int maxBytes)
        {
            if (entry.Length > maxBytes) throw Fail("metadata_too_large", $"'{entry.FullName}' exceeds the metadata size limit.");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            CopyBounded(stream, memory, maxBytes);
            return JsonDocument.Parse(memory.ToArray());
        }

        private static void CopyBounded(Stream input, Stream output, long maxBytes)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes) throw Fail("metadata_too_large", "Metadata expanded beyond its size limit.");
                output.Write(buffer, 0, read);
            }
        }

        private static string ResolveUnderRoot(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw Fail("archive_path_escape", "Archive entry escapes staging directory.");
            return full;
        }

        private static void ValidateArchivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") || path.Contains(":") || path.Contains("\0"))
                throw Fail("unsafe_archive_path", $"Unsafe archive path '{path}'.");
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal))
                    throw Fail("unsafe_archive_path", $"Unsafe archive path segment in '{path}'.");
                foreach (char c in segment)
                    if (!IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                        throw Fail("unsafe_archive_path", $"Unsupported archive path character in '{path}'.");
                string deviceName = segment.Split('.')[0].ToUpperInvariant();
                if (deviceName == "CON" || deviceName == "PRN" || deviceName == "AUX" || deviceName == "NUL" ||
                    (deviceName.Length == 4 && (deviceName.StartsWith("COM", StringComparison.Ordinal) ||
                    deviceName.StartsWith("LPT", StringComparison.Ordinal)) && deviceName[3] >= '1' && deviceName[3] <= '9'))
                    throw Fail("unsafe_archive_path", $"Reserved filesystem name in '{path}'.");
            }
        }

        private static bool IsAsciiLetterOrDigit(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

        private static void EnsureSafeEpisodeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 ||
                id.Any(c => !IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-') ||
                id == "." || id == "..")
                throw Fail("unsafe_episode_id", "Episode ID is not safe for use as an installation directory.");
        }

        private static string RequiredString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
                throw Fail("required_metadata_missing", $"Required string '{name}' is missing.");
            return value.GetString()!;
        }

        private static bool IsSha256(string value) =>
            value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        private static string ToHex(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        private static EpisodeInstallException Fail(string code, string message) => new EpisodeInstallException(code, message);
        private static EpisodeInstallResult Error(string code, string message) =>
            new EpisodeInstallResult { Success = false, ErrorCode = code, Error = message };

        private sealed class ExpectedFile
        {
            public long Size { get; }
            public string Hash { get; }
            public ExpectedFile(long size, string hash) { Size = size; Hash = hash; }
        }
    }

    public sealed class EpisodeInstallException : Exception
    {
        public string ErrorCode { get; }
        public EpisodeInstallException(string code, string message) : base(message) { ErrorCode = code; }
    }
}

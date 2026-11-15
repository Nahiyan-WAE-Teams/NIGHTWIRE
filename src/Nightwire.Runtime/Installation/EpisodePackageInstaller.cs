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
        private static readonly object InstallRootLocksGate = new object();
        private static readonly Dictionary<string, object> InstallRootLocks =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public EpisodePackageInstaller(EpisodeInstallerOptions? options = null, EpisodePackageLoader? loader = null)
        {
            _options = options ?? new EpisodeInstallerOptions();
            _loader = loader ?? new EpisodePackageLoader();
            if (_options.MaximumArchiveBytes <= 0 || _options.MaximumExpandedBytes <= 0 ||
                _options.MaximumEntries < 2 || _options.MaximumCompressionRatio < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Installer limits must be positive.");
        }

        /// <summary>
        /// Run once during startup, before opening episode sessions or starting another install.
        /// Restores the newest backup when an update was interrupted after moving the old install.
        /// </summary>
        public EpisodeInstallRecoveryResult RecoverInterruptedInstalls(string installRoot)
        {
            string root = Path.GetFullPath(installRoot);
            lock (GetInstallRootLock(root))
                return RecoverInterruptedInstallsCore(root);
        }

        private EpisodeInstallRecoveryResult RecoverInterruptedInstallsCore(string installRoot)
        {
            var restored = new List<string>();
            var warnings = new List<string>();
            int removedStaging = 0;
            int removedBackups = 0;
            string root = Path.GetFullPath(installRoot);
            EnsureNoLinkedAncestors(root);
            Directory.CreateDirectory(root);
            EnsureNoLinkedAncestors(root);

            foreach (string staging in Directory.GetDirectories(root, ".install-*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if ((File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0) continue;
                    Directory.Delete(staging, true);
                    removedStaging++;
                }
                catch (Exception exception) { warnings.Add("Could not remove staging directory: " + exception.Message); }
            }

            var backupsByEpisode = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string backup in Directory.GetDirectories(root, ".backup-*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(backup);
                if (name.Length <= 40 || !name.StartsWith(".backup-", StringComparison.Ordinal)) continue;
                string suffix = name.Substring(name.Length - 33);
                if (suffix[0] != '-' || !Guid.TryParseExact(suffix.Substring(1), "N", out _)) continue;
                string episodeId = name.Substring(8, name.Length - 8 - 33);
                try { EnsureSafeEpisodeId(episodeId); }
                catch { continue; }
                if ((File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0) continue;
                if (!backupsByEpisode.TryGetValue(episodeId, out var list))
                    backupsByEpisode.Add(episodeId, list = new List<string>());
                list.Add(backup);
            }

            foreach (var pair in backupsByEpisode)
            {
                string destination = Path.Combine(root, pair.Key);
                var backups = pair.Value.OrderByDescending(Directory.GetLastWriteTimeUtc).ToList();
                try
                {
                    if (File.Exists(destination))
                    {
                        warnings.Add("Episode destination is a file; backup retained for '" + pair.Key + "'.");
                        continue;
                    }
                    bool destinationExists = Directory.Exists(destination);
                    if (destinationExists && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    {
                        warnings.Add("Episode destination is a symbolic link or reparse point; backup retained for '" + pair.Key + "'.");
                        continue;
                    }

                    bool destinationValid = false;
                    if (destinationExists)
                    {
                        try
                        {
                            LoadedEpisodePackage installed = _loader.Load(destination);
                            destinationValid = string.Equals(installed.Descriptor.EpisodeId, pair.Key, StringComparison.Ordinal);
                        }
                        catch { destinationValid = false; }
                    }

                    if (!destinationExists || !destinationValid)
                    {
                        string? recoverableBackup = null;
                        foreach (string candidate in backups)
                        {
                            try
                            {
                                LoadedEpisodePackage backupPackage = _loader.Load(candidate);
                                if (!string.Equals(backupPackage.Descriptor.EpisodeId, pair.Key, StringComparison.Ordinal))
                                    throw new InvalidDataException("Backup episode identity does not match its directory.");
                                recoverableBackup = candidate;
                                break;
                            }
                            catch (Exception exception)
                            {
                                warnings.Add("Retained backup is not a valid episode package for '" + pair.Key + "': " + exception.Message);
                            }
                        }

                        if (recoverableBackup == null)
                        {
                            if (destinationExists && !destinationValid)
                                warnings.Add("Episode destination is invalid and no valid backup is available for '" + pair.Key + "'.");
                            continue;
                        }

                        string? quarantine = null;
                        if (destinationExists)
                        {
                            quarantine = Path.Combine(root, ".invalid-" + pair.Key + "-" + Guid.NewGuid().ToString("N"));
                            Directory.Move(destination, quarantine);
                        }
                        try
                        {
                            Directory.Move(recoverableBackup, destination);
                            restored.Add(pair.Key);
                            backups.Remove(recoverableBackup);
                            if (quarantine != null && Directory.Exists(quarantine))
                            {
                                try { Directory.Delete(quarantine, true); }
                                catch (Exception exception) { warnings.Add("Recovered episode but could not remove invalid destination quarantine: " + exception.Message); }
                            }
                        }
                        catch
                        {
                            if (quarantine != null && Directory.Exists(quarantine) && !Directory.Exists(destination))
                                Directory.Move(quarantine, destination);
                            throw;
                        }
                    }

                    foreach (string obsolete in backups)
                    {
                        try { Directory.Delete(obsolete, true); removedBackups++; }
                        catch (Exception exception) { warnings.Add("Could not remove obsolete backup: " + exception.Message); }
                    }
                }
                catch (Exception exception)
                {
                    warnings.Add("Could not recover episode '" + pair.Key + "': " + exception.Message);
                }
            }

            return new EpisodeInstallRecoveryResult
            {
                RestoredEpisodeIds = restored.AsReadOnly(),
                RemovedStagingDirectories = removedStaging,
                RemovedObsoleteBackups = removedBackups,
                Warnings = warnings.AsReadOnly()
            };
        }

        public EpisodeInstallResult InstallArchive(string archivePath, string installRoot, string? expectedEpisodeId = null, string? expectedEpisodeVersion = null)
        {
            try
            {
                string root = Path.GetFullPath(installRoot);
                lock (GetInstallRootLock(root))
                    return InstallArchiveCore(archivePath, root, expectedEpisodeId, expectedEpisodeVersion);
            }
            catch (Exception exception)
            {
                return Error("install_failed", exception.Message);
            }
        }

        private EpisodeInstallResult InstallArchiveCore(string archivePath, string installRoot, string? expectedEpisodeId, string? expectedEpisodeVersion)
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
                EnsureNoLinkedAncestors(root);
                Directory.CreateDirectory(root);
                EnsureNoLinkedAncestors(root);

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
                if (expectedEpisodeId != null && !string.Equals(episodeId, expectedEpisodeId, StringComparison.Ordinal))
                    throw Fail("release_episode_identity_mismatch", "Package episode ID does not match the signed release entry.");
                if (expectedEpisodeVersion != null && !string.Equals(episodeVersion, expectedEpisodeVersion, StringComparison.Ordinal))
                    throw Fail("release_episode_version_mismatch", "Package episode version does not match the signed release entry.");
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
                var explicitDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName;
                    bool isDirectory = name.EndsWith("/", StringComparison.Ordinal);
                    int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                    bool windowsReparsePoint = (entry.ExternalAttributes & 0x400) != 0;

                    // Inspect metadata before skipping directory records. A ZIP symlink can
                    // carry a trailing slash and must not bypass the link/special-file checks.
                    if (unixMode == 0xA000 || windowsReparsePoint)
                        throw Fail("archive_symlink_rejected", "Symbolic links and reparse points are not permitted in episode archives.");
                    if (isDirectory)
                    {
                        if (unixMode != 0 && unixMode != 0x4000)
                            throw Fail("archive_special_file_rejected", "Directory entries must have directory metadata.");
                        if (entry.Length != 0)
                            throw Fail("archive_directory_payload_rejected", "Directory entries cannot contain file data.");
                        string directoryName = name.Substring(0, name.Length - 1);
                        ValidateArchivePath(directoryName);
                        if (!explicitDirectories.Add(directoryName))
                            throw Fail("duplicate_archive_path", "Archive contains duplicate or case-colliding directory paths.");
                        continue;
                    }
                    if (unixMode != 0 && unixMode != 0x8000)
                        throw Fail("archive_special_file_rejected", "Only regular files and directories are permitted in episode archives.");
                    ValidateArchivePath(name);
                    if (!entries.TryAdd(name, entry))
                        throw Fail("duplicate_archive_path", "Archive contains duplicate or case-colliding paths.");
                }

                // Reject file/directory collisions before any archive content is extracted.
                foreach (string directory in explicitDirectories)
                {
                    if (entries.ContainsKey(directory))
                        throw Fail("archive_path_type_conflict", $"Archive path '{directory}' is declared as both a file and a directory.");
                }
                foreach (string path in entries.Keys.Concat(explicitDirectories))
                {
                    int separator = path.IndexOf('/');
                    while (separator >= 0)
                    {
                        string parent = path.Substring(0, separator);
                        if (entries.ContainsKey(parent))
                            throw Fail("archive_path_type_conflict", $"File '{parent}' cannot also be a parent directory.");
                        separator = path.IndexOf('/', separator + 1);
                    }
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

        internal static object GetInstallRootLock(string root)
        {
            string canonicalRoot = Path.GetFullPath(root);
            lock (InstallRootLocksGate)
            {
                if (!InstallRootLocks.TryGetValue(canonicalRoot, out object? gate))
                {
                    gate = new object();
                    InstallRootLocks.Add(canonicalRoot, gate);
                }
                return gate;
            }
        }

        private static void EnsureNoLinkedAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw Fail("unsafe_install_root", "Install root or one of its parent directories is a symbolic link or reparse point.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                    break;
                current = parent;
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

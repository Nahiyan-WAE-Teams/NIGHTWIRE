using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.IO;

namespace Nightwire.Runtime.Saves
{
    public sealed class SaveStoreOptions
    {
        public long MaximumSaveBytes { get; set; } = 16 * 1024 * 1024;
        public int MaximumNotifications { get; set; } = 1000;
        public int MaximumCollectionEntries { get; set; } = 100000;
    }

    public sealed class SaveLoadResult
    {
        public EpisodeProgressState State { get; internal set; } = new EpisodeProgressState();
        public bool RecoveredFromBackup { get; internal set; }
        public bool Migrated { get; internal set; }
        public string SavedAtUtc { get; internal set; } = string.Empty;
    }

    public sealed class SaveStoreException : Exception
    {
        public string ErrorCode { get; }
        public SaveStoreException(string code, string message) : base(message) { ErrorCode = code; }
        public SaveStoreException(string code, string message, Exception inner) : base(message, inner) { ErrorCode = code; }
    }

    /// <summary>
    /// Versioned local save persistence with checksums, bounded reads, atomic replacement, and backup recovery.
    /// Save operations are serialized per episode across instances in this process; hosts must coordinate other processes.
    /// </summary>
    public sealed class SaveStore
    {
        public const int CurrentSaveFormatVersion = 1;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            MaxDepth = 64
        };

        private readonly string _root;
        private readonly SaveStoreOptions _options;
        private readonly Dictionary<string, IEpisodeProgressMigration> _migrations =
            new Dictionary<string, IEpisodeProgressMigration>(StringComparer.Ordinal);
        private static readonly object EpisodeLockRegistryGate = new object();
        private static readonly Dictionary<string, object> EpisodeLocks =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public SaveStore(string saveRoot, SaveStoreOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(saveRoot)) throw new ArgumentException("Save root is required.", nameof(saveRoot));
            _root = Path.GetFullPath(saveRoot);
            _options = options ?? new SaveStoreOptions();
            if (_options.MaximumSaveBytes <= 0 || _options.MaximumNotifications < 0 || _options.MaximumCollectionEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Save limits must be positive.");
            EnsureNotLink(_root, "save root or one of its parent directories");
            Directory.CreateDirectory(_root);
            EnsureNotLink(_root, "save root or one of its parent directories");
        }

        public void RegisterMigration(IEpisodeProgressMigration migration)
        {
            if (migration == null) throw new ArgumentNullException(nameof(migration));
            if (string.IsNullOrWhiteSpace(migration.EpisodeId) ||
                string.IsNullOrWhiteSpace(migration.FromEpisodeVersion) ||
                string.IsNullOrWhiteSpace(migration.ToEpisodeVersion) ||
                string.Equals(migration.FromEpisodeVersion, migration.ToEpisodeVersion, StringComparison.Ordinal))
                throw new ArgumentException("Migration identity and distinct source/target versions are required.", nameof(migration));
            string key = MigrationKey(migration.EpisodeId, migration.FromEpisodeVersion);
            lock (_migrations)
            {
                if (_migrations.ContainsKey(key)) throw new InvalidOperationException("A migration already exists for this episode source version.");
                _migrations.Add(key, migration);
            }
        }

        public string GetSavePath(string episodeId)
        {
            EnsureNotLink(_root, "save root");
            string episodeDirectory = Path.Combine(_root, SafeEpisodeDirectory(episodeId));
            EnsureNotLink(episodeDirectory, "episode save directory");
            return Path.Combine(episodeDirectory, "save.json");
        }

        /// <summary>Writes progress while serializing all in-process access to this episode's save files.</summary>
        public void Save(EpisodeProgressState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            string path = GetSavePath(state.EpisodeId);
            lock (GetEpisodeLock(path)) SaveCore(state);
        }

        /// <summary>Creates the first save only; a concurrent or existing save is never overwritten.</summary>
        public void SaveNew(EpisodeProgressState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            string path = GetSavePath(state.EpisodeId);
            lock (GetEpisodeLock(path))
            {
                EnsureNotLink(path, "primary save");
                EnsureNotLink(path + ".bak", "backup save");
                if (File.Exists(path) || File.Exists(path + ".bak"))
                    throw new SaveStoreException("save_exists",
                        "A save already exists for this episode. Resume it instead of overwriting player progress.");
                SaveCore(state);
            }
        }

        private void SaveCore(EpisodeProgressState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ValidateState(state);
            string directory = Path.GetDirectoryName(GetSavePath(state.EpisodeId))!;
            Directory.CreateDirectory(directory);
            EnsureNotLink(directory, "episode save directory");
            string path = Path.Combine(directory, "save.json");
            string temp = path + ".tmp";
            string backup = path + ".bak";
            string backupTemp = backup + ".tmp";
            EnsureNotLink(path, "primary save");
            EnsureNotLink(temp, "temporary save");
            EnsureNotLink(backup, "backup save");
            EnsureNotLink(backupTemp, "temporary backup save");

            var payload = SavePayload.FromState(state);
            byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            string savedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            string checksum = ComputeEnvelopeChecksum(CurrentSaveFormatVersion, state.EpisodeId,
                state.EpisodeVersion, savedAtUtc, payloadBytes);
            var envelope = new SaveEnvelope
            {
                SaveFormatVersion = CurrentSaveFormatVersion,
                EpisodeId = state.EpisodeId,
                EpisodeVersion = state.EpisodeVersion,
                SavedAtUtc = savedAtUtc,
                Payload = payload,
                EnvelopeSha256 = checksum
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            if (bytes.LongLength > _options.MaximumSaveBytes)
                throw new SaveStoreException("save_too_large", "Serialized save exceeds the configured size limit.");

            try
            {
                // Build and flush the new primary candidate before touching either committed file.
                if (File.Exists(temp)) File.Delete(temp);
                EnsureNotLink(temp, "temporary save");
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                // Never copy directly over the last-known-good backup: a crash during a
                // direct copy could truncate it. Publish a fully flushed backup candidate first.
                if (File.Exists(path))
                {
                    if (File.Exists(backupTemp)) File.Delete(backupTemp);
                    EnsureNotLink(backupTemp, "temporary backup save");
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(backupTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                    EnsureNotLink(backupTemp, "temporary backup save");
                    EnsureNotLink(backup, "backup save");
                    RuntimeFileSystem.MoveFileReplacing(backupTemp, backup);
                }

                // Commit the new primary last. Until this rename succeeds, the old primary
                // remains authoritative and the previous backup remains available.
                EnsureNotLink(temp, "temporary save");
                EnsureNotLink(path, "primary save");
                RuntimeFileSystem.MoveFileReplacing(temp, path);
            }
            catch (Exception exception) when (!(exception is SaveStoreException))
            {
                TryDelete(temp);
                TryDelete(backupTemp);
                throw new SaveStoreException("save_write_failed", "Could not commit the save file. The previous committed save remains available where possible.", exception);
            }
        }

        public SaveLoadResult Load(string episodeId, string installedEpisodeVersion)
        {
            string path = GetSavePath(episodeId);
            lock (GetEpisodeLock(path)) return LoadCore(episodeId, installedEpisodeVersion);
        }

        private SaveLoadResult LoadCore(string episodeId, string installedEpisodeVersion)
        {
            if (string.IsNullOrWhiteSpace(installedEpisodeVersion))
                throw new ArgumentException("Installed episode version is required.", nameof(installedEpisodeVersion));
            string path = GetSavePath(episodeId);
            string backup = path + ".bak";
            EnsureNotLink(path, "primary save");
            EnsureNotLink(backup, "backup save");
            if (!File.Exists(path) && !File.Exists(backup))
                throw new SaveStoreException("save_not_found", $"No save exists for episode '{episodeId}'.");

            SaveEnvelope? envelope = null;
            bool recovered = false;
            Exception? primaryError = null;
            if (File.Exists(path))
            {
                try { envelope = ReadAndVerify(path, episodeId); }
                catch (Exception exception) { primaryError = exception; }
            }
            if (envelope == null && File.Exists(backup))
            {
                try { envelope = ReadAndVerify(backup, episodeId); recovered = true; }
                catch (Exception backupError)
                {
                    throw new SaveStoreException("save_and_backup_invalid",
                        "The primary save and its backup are both invalid.",
                        new AggregateException(primaryError ?? backupError, backupError));
                }
            }
            if (envelope == null)
                throw new SaveStoreException("save_invalid", "The save file is invalid.", primaryError ?? new InvalidDataException());
            ValidatePayload(envelope.Payload!);
            EpisodeProgressState state;
            try { state = envelope.Payload!.ToState(envelope.EpisodeId, envelope.EpisodeVersion); }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException || exception is NullReferenceException)
            { throw new SaveStoreException("save_payload_invalid", "Save payload contains invalid or duplicate entries.", exception); }
            ValidateState(state);
            if (recovered) RestoreBackup(backup, path);
            bool migrated = MigrateToInstalledVersion(state, installedEpisodeVersion);
            ValidateState(state);
            return new SaveLoadResult
            {
                State = state,
                RecoveredFromBackup = recovered,
                Migrated = migrated,
                SavedAtUtc = envelope.SavedAtUtc
            };
        }

        private SaveEnvelope ReadAndVerify(string path, string expectedEpisodeId)
        {
            EnsureNotLink(path, "save file");
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > _options.MaximumSaveBytes)
                throw new SaveStoreException("save_size_invalid", "Save file is empty or exceeds the configured size limit.");
            byte[] bytes = File.ReadAllBytes(path);
            SaveEnvelope? envelope;
            try { envelope = JsonSerializer.Deserialize<SaveEnvelope>(bytes, JsonOptions); }
            catch (JsonException exception) { throw new SaveStoreException("save_json_invalid", "Save JSON is malformed.", exception); }
            if (envelope == null || envelope.Payload == null) throw new SaveStoreException("save_envelope_invalid", "Save envelope is incomplete.");
            if (envelope.SaveFormatVersion != CurrentSaveFormatVersion)
                throw new SaveStoreException("save_format_unsupported", $"Save format {envelope.SaveFormatVersion} is not supported.");
            if (!string.Equals(envelope.EpisodeId, expectedEpisodeId, StringComparison.Ordinal))
                throw new SaveStoreException("episode_id_mismatch", "Save belongs to a different episode.");
            if (string.IsNullOrWhiteSpace(envelope.EpisodeVersion) || string.IsNullOrWhiteSpace(envelope.SavedAtUtc) ||
                string.IsNullOrWhiteSpace(envelope.EnvelopeSha256))
                throw new SaveStoreException("save_envelope_invalid", "Save metadata is incomplete.");

            using (JsonDocument document = JsonDocument.Parse(bytes))
            {
                if (!document.RootElement.TryGetProperty("payload", out var payloadElement))
                    throw new SaveStoreException("save_envelope_invalid", "Save payload is missing.");
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadElement.GetRawText());
                string expectedChecksum = ComputeEnvelopeChecksum(envelope.SaveFormatVersion, envelope.EpisodeId,
                    envelope.EpisodeVersion, envelope.SavedAtUtc, payloadBytes);
                if (!string.Equals(expectedChecksum, envelope.EnvelopeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new SaveStoreException("save_checksum_mismatch", "Save envelope checksum does not match.");
            }
            return envelope;
        }

        private static void RestoreBackup(string backupPath, string primaryPath)
        {
            string restoreTemp = primaryPath + ".restore.tmp";
            try
            {
                EnsureNotLink(backupPath, "backup save");
                EnsureNotLink(restoreTemp, "temporary backup restore save");
                EnsureNotLink(primaryPath, "primary save");
                if (File.Exists(restoreTemp)) File.Delete(restoreTemp);
                EnsureNotLink(restoreTemp, "temporary backup restore save");
                using (var input = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(restoreTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                EnsureNotLink(restoreTemp, "temporary backup restore save");
                EnsureNotLink(primaryPath, "primary save");
                RuntimeFileSystem.MoveFileReplacing(restoreTemp, primaryPath);
            }
            catch (SaveStoreException)
            {
                throw;
            }
            catch (Exception exception)
            {
                TryDelete(restoreTemp);
                throw new SaveStoreException("save_backup_restore_failed",
                    "The backup was readable but could not be restored to the primary save path.", exception);
            }
        }

        private bool MigrateToInstalledVersion(EpisodeProgressState state, string targetVersion)
        {
            if (string.Equals(state.EpisodeVersion, targetVersion, StringComparison.Ordinal)) return false;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            bool migrated = false;
            while (!string.Equals(state.EpisodeVersion, targetVersion, StringComparison.Ordinal))
            {
                if (!visited.Add(state.EpisodeVersion))
                    throw new SaveStoreException("migration_cycle", "Episode save migration contains a version cycle.");
                IEpisodeProgressMigration? migration;
                lock (_migrations)
                    _migrations.TryGetValue(MigrationKey(state.EpisodeId, state.EpisodeVersion), out migration);
                if (migration == null)
                    throw new SaveStoreException("migration_required",
                        $"Save is for episode version '{state.EpisodeVersion}', but '{targetVersion}' is installed. An explicit migration is required.");
                if (!string.Equals(migration.EpisodeId, state.EpisodeId, StringComparison.Ordinal) ||
                    !string.Equals(migration.FromEpisodeVersion, state.EpisodeVersion, StringComparison.Ordinal))
                    throw new SaveStoreException("migration_identity_changed",
                        "Registered migration identity no longer matches the save's current episode and source version.");
                if (string.IsNullOrWhiteSpace(migration.ToEpisodeVersion) ||
                    string.Equals(migration.ToEpisodeVersion, state.EpisodeVersion, StringComparison.Ordinal))
                    throw new SaveStoreException("migration_target_invalid",
                        "Episode migration must declare a non-empty target version different from its source.");

                string nextVersion = migration.ToEpisodeVersion;
                try { migration.Migrate(state); }
                catch (SaveStoreException) { throw; }
                catch (Exception exception)
                {
                    throw new SaveStoreException("migration_failed",
                        $"Migration from '{state.EpisodeVersion}' to '{nextVersion}' failed.", exception);
                }

                if (!string.Equals(state.EpisodeId, migration.EpisodeId, StringComparison.Ordinal))
                    throw new SaveStoreException("migration_identity_changed", "Episode migrations cannot change the save's episode identity.");
                state.EpisodeVersion = nextVersion;
                migrated = true;
                if (visited.Count > 32) throw new SaveStoreException("migration_limit", "Too many sequential save migrations.");
            }
            return migrated;
        }

        private void ValidateState(EpisodeProgressState state)
        {
            if (string.IsNullOrWhiteSpace(state.EpisodeId) || string.IsNullOrWhiteSpace(state.EpisodeVersion))
                throw new SaveStoreException("save_state_invalid", "Episode ID and version are required.");
            if (state.Notifications.Count > _options.MaximumNotifications ||
                state.ConversationProgress.Count > _options.MaximumCollectionEntries ||
                state.CallProgress.Count > _options.MaximumCollectionEntries || state.MinigameProgress.Count > _options.MaximumCollectionEntries ||
                state.CompletedEventIds.Count > _options.MaximumCollectionEntries ||
                state.UnlockedAssetIds.Count > _options.MaximumCollectionEntries ||
                state.StoryFlags.Count > _options.MaximumCollectionEntries ||
                state.RelationshipScores.Count > _options.MaximumCollectionEntries)
                throw new SaveStoreException("save_state_limit", "Save state exceeds a configured collection limit.");
            if (state.StoryFlags.Any(pair => pair.Value == null ||
                (pair.Value.Kind == StoryFlagValueKind.Number &&
                 (double.IsNaN(pair.Value.NumberValue) || double.IsInfinity(pair.Value.NumberValue)))))
                throw new SaveStoreException("save_flag_invalid", "Story flags contain an invalid value.");
            if (state.Notifications.Any(x => x == null || string.IsNullOrWhiteSpace(x.NotificationId) ||
                string.IsNullOrWhiteSpace(x.TitleKey) || string.IsNullOrWhiteSpace(x.BodyKey)))
                throw new SaveStoreException("save_notification_invalid", "Notifications contain invalid data.");
            foreach (var pair in state.CallProgress)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.ElapsedMilliseconds < 0 || pair.Value.FiredCueIds.Count > _options.MaximumCollectionEntries || pair.Value.FiredSubtitleIds.Count > _options.MaximumCollectionEntries)
                    throw new SaveStoreException("save_call_invalid", "Call progress is invalid.");
            }
            foreach (var pair in state.MinigameProgress)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.AttemptsUsed < 0 || double.IsNaN(pair.Value.ElapsedSeconds) || double.IsInfinity(pair.Value.ElapsedSeconds) || pair.Value.ElapsedSeconds < 0 || pair.Value.SequenceInput.Count > 32 || pair.Value.MatchedItemIds.Count > _options.MaximumCollectionEntries)
                    throw new SaveStoreException("save_minigame_invalid", "Minigame progress is invalid.");
            }
            foreach (var pair in state.ConversationProgress)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    throw new SaveStoreException("save_conversation_invalid", "Conversation progress IDs cannot be empty.");
                if (pair.Value == null || pair.Value.DeliveredMessageIds.Count > _options.MaximumCollectionEntries ||
                    pair.Value.ReadMessageIds.Count > _options.MaximumCollectionEntries ||
                    pair.Value.SelectedOptions.Count > _options.MaximumCollectionEntries ||
                    double.IsNaN(pair.Value.ElapsedDelaySeconds) || double.IsInfinity(pair.Value.ElapsedDelaySeconds) ||
                    pair.Value.ElapsedDelaySeconds < 0)
                    throw new SaveStoreException("save_conversation_invalid", $"Conversation progress '{pair.Key}' is invalid.");
            }
        }

        private void ValidatePayload(SavePayload payload)
        {
            if (payload.Conversations == null || payload.Calls == null || payload.Minigames == null || payload.CompletedEventIds == null || payload.UnlockedAssetIds == null ||
                payload.StoryFlags == null || payload.RelationshipScores == null || payload.Notifications == null)
                throw new SaveStoreException("save_payload_invalid", "Save payload collections cannot be null.");
            int total = payload.Conversations.Count + payload.Calls.Count + payload.Minigames.Count + payload.CompletedEventIds.Count + payload.UnlockedAssetIds.Count +
                payload.StoryFlags.Count + payload.RelationshipScores.Count;
            if (total > _options.MaximumCollectionEntries || payload.Notifications.Count > _options.MaximumNotifications)
                throw new SaveStoreException("save_payload_limit", "Save payload exceeds configured collection limits.");
            if (payload.Conversations.Any(x => x == null || string.IsNullOrWhiteSpace(x.ConversationId) ||
                x.DeliveredMessageIds == null || x.ReadMessageIds == null || x.SelectedOptions == null ||
                double.IsNaN(x.ElapsedDelaySeconds) || double.IsInfinity(x.ElapsedDelaySeconds) || x.ElapsedDelaySeconds < 0))
                throw new SaveStoreException("save_payload_invalid", "Conversation save data is incomplete or invalid.");
            if (payload.Calls.Any(x => x == null || string.IsNullOrWhiteSpace(x.CallId) || x.ElapsedMilliseconds < 0 ||
                x.FiredCueIds == null || x.FiredSubtitleIds == null) ||
                payload.Minigames.Any(x => x == null || string.IsNullOrWhiteSpace(x.MinigameId) || x.AttemptsUsed < 0 ||
                double.IsNaN(x.ElapsedSeconds) || double.IsInfinity(x.ElapsedSeconds) || x.ElapsedSeconds < 0 ||
                x.SequenceInput == null || x.MatchedItemIds == null || x.SequenceInput.Count > 32))
                throw new SaveStoreException("save_payload_invalid", "Call or minigame save data is incomplete or invalid.");
            if (payload.CompletedEventIds.Any(string.IsNullOrWhiteSpace) || payload.UnlockedAssetIds.Any(string.IsNullOrWhiteSpace) ||
                payload.StoryFlags.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) ||
                    (x.Kind == StoryFlagValueKind.Number && (double.IsNaN(x.NumberValue) || double.IsInfinity(x.NumberValue)))) ||
                payload.Notifications.Any(x => x == null || string.IsNullOrWhiteSpace(x.NotificationId) ||
                    string.IsNullOrWhiteSpace(x.TitleKey) || string.IsNullOrWhiteSpace(x.BodyKey)))
                throw new SaveStoreException("save_payload_invalid", "Save payload contains an empty or invalid identifier/value.");
        }

        private static string MigrationKey(string episodeId, string fromVersion) => episodeId + "\n" + fromVersion;
        private static void EnsureNotLink(string path, string description)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new SaveStoreException("save_path_unsafe",
                            "Symbolic links and reparse points are not allowed in " + description + ".");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                    break;
                current = parent;
            }
        }

        private static object GetEpisodeLock(string savePath)
        {
            string key = Path.GetFullPath(savePath);
            lock (EpisodeLockRegistryGate)
            {
                if (!EpisodeLocks.TryGetValue(key, out var episodeLock))
                {
                    episodeLock = new object();
                    EpisodeLocks.Add(key, episodeLock);
                }
                return episodeLock;
            }
        }

        private static string SafeEpisodeDirectory(string episodeId)
        {
            if (string.IsNullOrWhiteSpace(episodeId) || episodeId.Length > 128 ||
                episodeId.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')) ||
                episodeId == "." || episodeId == "..")
                throw new SaveStoreException("episode_id_invalid", "Episode ID must contain only letters, digits, dots, underscores, and hyphens.");
            return episodeId;
        }

        private static string ComputeEnvelopeChecksum(int formatVersion, string episodeId,
            string episodeVersion, string savedAtUtc, byte[] payloadBytes)
        {
            byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new ChecksumMetadata
            {
                SaveFormatVersion = formatVersion,
                EpisodeId = episodeId,
                EpisodeVersion = episodeVersion,
                SavedAtUtc = savedAtUtc
            }, JsonOptions);
            byte[] input = new byte[4 + metadata.Length + payloadBytes.Length];
            input[0] = (byte)(metadata.Length >> 24);
            input[1] = (byte)(metadata.Length >> 16);
            input[2] = (byte)(metadata.Length >> 8);
            input[3] = (byte)metadata.Length;
            Buffer.BlockCopy(metadata, 0, input, 4, metadata.Length);
            Buffer.BlockCopy(payloadBytes, 0, input, 4 + metadata.Length, payloadBytes.Length);
            return Sha256(input);
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2")));
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        private sealed class ChecksumMetadata
        {
            public int SaveFormatVersion { get; set; }
            public string EpisodeId { get; set; } = string.Empty;
            public string EpisodeVersion { get; set; } = string.Empty;
            public string SavedAtUtc { get; set; } = string.Empty;
        }

        private sealed class SaveEnvelope
        {
            public SaveEnvelope() { }
            public int SaveFormatVersion { get; set; }
            public string EpisodeId { get; set; } = string.Empty;
            public string EpisodeVersion { get; set; } = string.Empty;
            public string SavedAtUtc { get; set; } = string.Empty;
            public SavePayload? Payload { get; set; }
            public string EnvelopeSha256 { get; set; } = string.Empty;
        }

        private sealed class SavePayload
        {
            public SavePayload() { }
            public string? ActiveConversationId { get; set; }
            public string? PendingCallId { get; set; }
            public string? PendingMinigameId { get; set; }
            public List<ConversationPayload> Conversations { get; set; } = new List<ConversationPayload>();
            public List<CallPayload> Calls { get; set; } = new List<CallPayload>();
            public List<MinigamePayload> Minigames { get; set; } = new List<MinigamePayload>();
            public List<string> CompletedEventIds { get; set; } = new List<string>();
            public List<string> UnlockedAssetIds { get; set; } = new List<string>();
            public List<StoryFlagPayload> StoryFlags { get; set; } = new List<StoryFlagPayload>();
            public Dictionary<string, long> RelationshipScores { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
            public List<RuntimeNotification> Notifications { get; set; } = new List<RuntimeNotification>();

            public static SavePayload FromState(EpisodeProgressState state)
            {
                return new SavePayload
                {
                    ActiveConversationId = state.ActiveConversationId,
                    PendingCallId = state.PendingCallId,
                    PendingMinigameId = state.PendingMinigameId,
                    Conversations = state.ConversationProgress.Select(pair => ConversationPayload.From(pair.Key, pair.Value)).ToList(),
                    Calls = state.CallProgress.Select(pair => CallPayload.From(pair.Key, pair.Value)).ToList(),
                    Minigames = state.MinigameProgress.Select(pair => MinigamePayload.From(pair.Key, pair.Value)).ToList(),
                    CompletedEventIds = state.CompletedEventIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    UnlockedAssetIds = state.UnlockedAssetIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    StoryFlags = state.StoryFlags.OrderBy(x => x.Key, StringComparer.Ordinal)
                        .Select(pair => StoryFlagPayload.From(pair.Key, pair.Value)).ToList(),
                    RelationshipScores = new Dictionary<string, long>(state.RelationshipScores, StringComparer.Ordinal),
                    Notifications = state.Notifications.Select(CloneNotification).ToList()
                };
            }

            public EpisodeProgressState ToState(string episodeId, string episodeVersion)
            {
                var state = new EpisodeProgressState { EpisodeId = episodeId, EpisodeVersion = episodeVersion,
                    ActiveConversationId = ActiveConversationId, PendingCallId = PendingCallId, PendingMinigameId = PendingMinigameId };
                foreach (var item in Conversations) state.ConversationProgress.Add(item.ConversationId, item.ToState());
                foreach (var item in Calls) state.CallProgress.Add(item.CallId, item.ToState());
                foreach (var item in Minigames) state.MinigameProgress.Add(item.MinigameId, item.ToState());
                foreach (string id in CompletedEventIds) state.CompletedEventIds.Add(id);
                foreach (string id in UnlockedAssetIds) state.UnlockedAssetIds.Add(id);
                foreach (var flag in StoryFlags) state.StoryFlags.Add(flag.Name, flag.ToValue());
                foreach (var score in RelationshipScores) state.RelationshipScores.Add(score.Key, score.Value);
                foreach (var notification in Notifications) state.Notifications.Add(CloneNotification(notification));
                return state;
            }
        }


        private sealed class CallPayload
        {
            public string CallId { get; set; } = string.Empty;
            public long ElapsedMilliseconds { get; set; }
            public bool IsComplete { get; set; }
            public List<string> FiredCueIds { get; set; } = new List<string>();
            public List<string> FiredSubtitleIds { get; set; } = new List<string>();
            public static CallPayload From(string id, CallProgressState state) => new CallPayload
            {
                CallId = id, ElapsedMilliseconds = state.ElapsedMilliseconds, IsComplete = state.IsComplete,
                FiredCueIds = state.FiredCueIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                FiredSubtitleIds = state.FiredSubtitleIds.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };
            public CallProgressState ToState()
            {
                var state = new CallProgressState { ElapsedMilliseconds = ElapsedMilliseconds, IsComplete = IsComplete };
                foreach (string id in FiredCueIds) state.FiredCueIds.Add(id);
                foreach (string id in FiredSubtitleIds) state.FiredSubtitleIds.Add(id);
                return state;
            }
        }

        private sealed class MinigamePayload
        {
            public string MinigameId { get; set; } = string.Empty;
            public int AttemptsUsed { get; set; }
            public bool IsComplete { get; set; }
            public double ElapsedSeconds { get; set; }
            public List<string> SequenceInput { get; set; } = new List<string>();
            public List<string> MatchedItemIds { get; set; } = new List<string>();
            public static MinigamePayload From(string id, MinigameProgressState state) => new MinigamePayload
            {
                MinigameId = id, AttemptsUsed = state.AttemptsUsed, IsComplete = state.IsComplete, ElapsedSeconds = state.ElapsedSeconds,
                SequenceInput = new List<string>(state.SequenceInput),
                MatchedItemIds = state.MatchedItemIds.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };
            public MinigameProgressState ToState()
            {
                var state = new MinigameProgressState { AttemptsUsed = AttemptsUsed, IsComplete = IsComplete, ElapsedSeconds = ElapsedSeconds };
                state.SequenceInput.AddRange(SequenceInput);
                foreach (string id in MatchedItemIds) state.MatchedItemIds.Add(id);
                return state;
            }
        }

        private sealed class ConversationPayload
        {
            public ConversationPayload() { }
            public string ConversationId { get; set; } = string.Empty;
            public string? CurrentMessageId { get; set; }
            public string? NextMessageId { get; set; }
            public string? WaitingChoiceMessageId { get; set; }
            public double ElapsedDelaySeconds { get; set; }
            public bool IsComplete { get; set; }
            public List<string> DeliveredMessageIds { get; set; } = new List<string>();
            public List<string> ReadMessageIds { get; set; } = new List<string>();
            public Dictionary<string, string> SelectedOptions { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

            public static ConversationPayload From(string id, ConversationProgressState state) => new ConversationPayload
            {
                ConversationId = id, CurrentMessageId = state.CurrentMessageId, NextMessageId = state.NextMessageId,
                WaitingChoiceMessageId = state.WaitingChoiceMessageId, ElapsedDelaySeconds = state.ElapsedDelaySeconds,
                IsComplete = state.IsComplete,
                DeliveredMessageIds = state.DeliveredMessageIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ReadMessageIds = state.ReadMessageIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                SelectedOptions = new Dictionary<string, string>(state.SelectedOptions, StringComparer.Ordinal)
            };

            public ConversationProgressState ToState()
            {
                var state = new ConversationProgressState { CurrentMessageId = CurrentMessageId, NextMessageId = NextMessageId,
                    WaitingChoiceMessageId = WaitingChoiceMessageId, ElapsedDelaySeconds = ElapsedDelaySeconds, IsComplete = IsComplete };
                foreach (string id in DeliveredMessageIds) state.DeliveredMessageIds.Add(id);
                foreach (string id in ReadMessageIds) state.ReadMessageIds.Add(id);
                foreach (var pair in SelectedOptions) state.SelectedOptions.Add(pair.Key, pair.Value);
                return state;
            }
        }

        private sealed class StoryFlagPayload
        {
            public StoryFlagPayload() { }
            public string Name { get; set; } = string.Empty;
            public StoryFlagValueKind Kind { get; set; }
            public bool BooleanValue { get; set; }
            public double NumberValue { get; set; }
            public string? StringValue { get; set; }
            public static StoryFlagPayload From(string name, StoryFlagValue value) => new StoryFlagPayload
            {
                Name = name, Kind = value.Kind, BooleanValue = value.BooleanValue,
                NumberValue = value.NumberValue, StringValue = value.StringValue
            };
            public StoryFlagValue ToValue()
            {
                switch (Kind)
                {
                    case StoryFlagValueKind.Boolean: return StoryFlagValue.FromBoolean(BooleanValue);
                    case StoryFlagValueKind.Number:
                        if (double.IsNaN(NumberValue) || double.IsInfinity(NumberValue)) throw new InvalidDataException("Story flag number is not finite.");
                        return StoryFlagValue.FromNumber(NumberValue);
                    case StoryFlagValueKind.String: return StoryFlagValue.FromString(StringValue ?? string.Empty);
                    default: throw new InvalidDataException("Story flag kind is unknown.");
                }
            }
        }

        private static RuntimeNotification CloneNotification(RuntimeNotification value) => new RuntimeNotification
        {
            NotificationId = value.NotificationId, TitleKey = value.TitleKey, BodyKey = value.BodyKey,
            DestinationId = value.DestinationId, IsRead = value.IsRead
        };
    }
}

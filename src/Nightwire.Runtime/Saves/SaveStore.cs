using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nightwire.Runtime.Domain;

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
    /// One instance should be used per game save root; callers serialize access to a given episode save.
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

        public SaveStore(string saveRoot, SaveStoreOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(saveRoot)) throw new ArgumentException("Save root is required.", nameof(saveRoot));
            _root = Path.GetFullPath(saveRoot);
            _options = options ?? new SaveStoreOptions();
            if (_options.MaximumSaveBytes <= 0 || _options.MaximumNotifications < 0 || _options.MaximumCollectionEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Save limits must be positive.");
            Directory.CreateDirectory(_root);
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
            if (_migrations.ContainsKey(key)) throw new InvalidOperationException("A migration already exists for this episode source version.");
            _migrations.Add(key, migration);
        }

        public string GetSavePath(string episodeId) => Path.Combine(_root, SafeEpisodeDirectory(episodeId), "save.json");

        public void Save(EpisodeProgressState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ValidateState(state);
            string directory = Path.GetDirectoryName(GetSavePath(state.EpisodeId))!;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "save.json");
            string temp = path + ".tmp";
            string backup = path + ".bak";

            var payload = SavePayload.FromState(state);
            byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            string checksum = Sha256(payloadBytes);
            var envelope = new SaveEnvelope
            {
                SaveFormatVersion = CurrentSaveFormatVersion,
                EpisodeId = state.EpisodeId,
                EpisodeVersion = state.EpisodeVersion,
                SavedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                Payload = payload,
                PayloadSha256 = checksum
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            if (bytes.LongLength > _options.MaximumSaveBytes)
                throw new SaveStoreException("save_too_large", "Serialized save exceeds the configured size limit.");

            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Copy(path, backup, true);
                File.Move(temp, path, true);
            }
            catch (Exception exception) when (!(exception is SaveStoreException))
            {
                TryDelete(temp);
                throw new SaveStoreException("save_write_failed", "Could not commit the save file. The previous save was left in place where possible.", exception);
            }
        }

        public SaveLoadResult Load(string episodeId, string installedEpisodeVersion)
        {
            if (string.IsNullOrWhiteSpace(installedEpisodeVersion))
                throw new ArgumentException("Installed episode version is required.", nameof(installedEpisodeVersion));
            string path = GetSavePath(episodeId);
            string backup = path + ".bak";
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
                string.IsNullOrWhiteSpace(envelope.PayloadSha256))
                throw new SaveStoreException("save_envelope_invalid", "Save metadata is incomplete.");

            byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, JsonOptions);
            if (!string.Equals(Sha256(payloadBytes), envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                throw new SaveStoreException("save_checksum_mismatch", "Save payload checksum does not match.");
            return envelope;
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
                if (!_migrations.TryGetValue(MigrationKey(state.EpisodeId, state.EpisodeVersion), out var migration))
                    throw new SaveStoreException("migration_required",
                        $"Save is for episode version '{state.EpisodeVersion}', but '{targetVersion}' is installed. An explicit migration is required.");
                migration.Migrate(state);
                if (!string.Equals(state.EpisodeId, migration.EpisodeId, StringComparison.Ordinal))
                    throw new SaveStoreException("migration_identity_changed", "Episode migrations cannot change the save's episode identity.");
                state.EpisodeVersion = migration.ToEpisodeVersion;
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
            if (payload.Conversations == null || payload.CompletedEventIds == null || payload.UnlockedAssetIds == null ||
                payload.StoryFlags == null || payload.RelationshipScores == null || payload.Notifications == null)
                throw new SaveStoreException("save_payload_invalid", "Save payload collections cannot be null.");
            int total = payload.Conversations.Count + payload.CompletedEventIds.Count + payload.UnlockedAssetIds.Count +
                payload.StoryFlags.Count + payload.RelationshipScores.Count;
            if (total > _options.MaximumCollectionEntries || payload.Notifications.Count > _options.MaximumNotifications)
                throw new SaveStoreException("save_payload_limit", "Save payload exceeds configured collection limits.");
            if (payload.Conversations.Any(x => x == null || string.IsNullOrWhiteSpace(x.ConversationId) ||
                x.DeliveredMessageIds == null || x.ReadMessageIds == null || x.SelectedOptions == null ||
                double.IsNaN(x.ElapsedDelaySeconds) || double.IsInfinity(x.ElapsedDelaySeconds) || x.ElapsedDelaySeconds < 0))
                throw new SaveStoreException("save_payload_invalid", "Conversation save data is incomplete or invalid.");
            if (payload.CompletedEventIds.Any(string.IsNullOrWhiteSpace) || payload.UnlockedAssetIds.Any(string.IsNullOrWhiteSpace) ||
                payload.StoryFlags.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) ||
                    (x.Kind == StoryFlagValueKind.Number && (double.IsNaN(x.NumberValue) || double.IsInfinity(x.NumberValue)))) ||
                payload.Notifications.Any(x => x == null || string.IsNullOrWhiteSpace(x.NotificationId) ||
                    string.IsNullOrWhiteSpace(x.TitleKey) || string.IsNullOrWhiteSpace(x.BodyKey)))
                throw new SaveStoreException("save_payload_invalid", "Save payload contains an empty or invalid identifier/value.");
        }

        private static string MigrationKey(string episodeId, string fromVersion) => episodeId + "\n" + fromVersion;
        private static string SafeEpisodeDirectory(string episodeId)
        {
            if (string.IsNullOrWhiteSpace(episodeId) || episodeId.Length > 128 ||
                episodeId.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')) ||
                episodeId == "." || episodeId == "..")
                throw new SaveStoreException("episode_id_invalid", "Episode ID must contain only letters, digits, dots, underscores, and hyphens.");
            return episodeId;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2")));
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        private sealed class SaveEnvelope
        {
            public SaveEnvelope() { }
            public int SaveFormatVersion { get; set; }
            public string EpisodeId { get; set; } = string.Empty;
            public string EpisodeVersion { get; set; } = string.Empty;
            public string SavedAtUtc { get; set; } = string.Empty;
            public SavePayload? Payload { get; set; }
            public string PayloadSha256 { get; set; } = string.Empty;
        }

        private sealed class SavePayload
        {
            public SavePayload() { }
            public string? ActiveConversationId { get; set; }
            public string? PendingCallId { get; set; }
            public string? PendingMinigameId { get; set; }
            public List<ConversationPayload> Conversations { get; set; } = new List<ConversationPayload>();
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
                foreach (string id in CompletedEventIds) state.CompletedEventIds.Add(id);
                foreach (string id in UnlockedAssetIds) state.UnlockedAssetIds.Add(id);
                foreach (var flag in StoryFlags) state.StoryFlags.Add(flag.Name, flag.ToValue());
                foreach (var score in RelationshipScores) state.RelationshipScores.Add(score.Key, score.Value);
                foreach (var notification in Notifications) state.Notifications.Add(CloneNotification(notification));
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

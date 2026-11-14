using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Nightwire.Runtime.Domain;

namespace Nightwire.Runtime.Loading
{
    /// <summary>
    /// Loads an unpacked episode only after its complete manifest inventory and SHA-256 digests verify.
    /// This class has no Unity dependency and never executes package-provided code.
    /// </summary>
    public sealed class EpisodePackageLoader
    {
        private readonly EpisodePackageLoaderOptions _options;

        public EpisodePackageLoader(EpisodePackageLoaderOptions? options = null)
        {
            _options = options ?? new EpisodePackageLoaderOptions();
            if (_options.MaximumPackageBytes <= 0 || _options.MaximumJsonFileBytes <= 0 || _options.MaximumJsonDepth < 1)
                throw new ArgumentOutOfRangeException(nameof(options), "Package limits must be positive.");
        }

        public LoadedEpisodePackage Load(string packageDirectory)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory))
                throw new ArgumentException("Package directory is required.", nameof(packageDirectory));

            string root = Path.GetFullPath(packageDirectory);
            if (!Directory.Exists(root))
                throw Fail("package_not_found", $"Episode directory does not exist: {root}");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw Fail("unsafe_root", "Episode root cannot be a symbolic link or reparse point.");

            JsonDocument descriptorDocument = ReadJson(Path.Combine(root, "episode.json"), _options.MaximumJsonFileBytes);
            try
            {
                var descriptor = ParseDescriptor(descriptorDocument.RootElement);
                if (!string.Equals(descriptor.SchemaVersion, "0.1", StringComparison.Ordinal))
                    throw Fail("unsupported_schema", $"Unsupported episode schema version '{descriptor.SchemaVersion}'.");
                if (!string.Equals(descriptor.Manifest, "manifest.json", StringComparison.Ordinal))
                    throw Fail("invalid_manifest_name", "Format 0.1 requires the manifest file to be named manifest.json.");
                EnsureRuntimeCompatible(descriptor.MinimumRuntimeVersion, _options.RuntimeVersion);

                using (JsonDocument manifestDocument = ReadJson(Path.Combine(root, descriptor.Manifest), _options.MaximumJsonFileBytes))
                {
                var manifest = ParseManifest(manifestDocument.RootElement);
                if (!string.Equals(manifest.SchemaVersion, descriptor.SchemaVersion, StringComparison.Ordinal))
                    throw Fail("schema_mismatch", "Episode descriptor and manifest schema versions differ.");
                if (!string.Equals(manifest.EpisodeId, descriptor.EpisodeId, StringComparison.Ordinal))
                    throw Fail("episode_id_mismatch", "Episode descriptor and manifest IDs differ.");

                VerifyInventory(root, manifest);
                var package = new LoadedEpisodePackage
                {
                    RootDirectory = root,
                    Descriptor = descriptor,
                    Manifest = manifest,
                    RuntimeDefinition = new EpisodeRuntimeDefinition
                    {
                        EpisodeId = descriptor.EpisodeId,
                        EpisodeVersion = descriptor.EpisodeVersion,
                        EntryEventId = descriptor.EntryEventId
                    }
                };

                IndexAssets(package);
                LoadDocuments(package);
                ValidateReferences(package);
                return package;
                }
            }
            finally
            {
                descriptorDocument.Dispose();
            }
        }

        private void VerifyInventory(string root, EpisodeManifest manifest)
        {
            var listed = new Dictionary<string, EpisodeManifestFile>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            foreach (var entry in manifest.Files)
            {
                ValidateRelativePath(entry.Path);
                if (string.Equals(entry.Path, "manifest.json", StringComparison.OrdinalIgnoreCase))
                    throw Fail("manifest_self_reference", "manifest.json must not list itself.");
                if (!listed.TryAdd(entry.Path, entry))
                    throw Fail("duplicate_path", $"Manifest contains duplicate path '{entry.Path}'.");
                if (entry.SizeBytes < 0 || !IsSha256(entry.Sha256))
                    throw Fail("invalid_manifest_entry", $"Invalid size or SHA-256 for '{entry.Path}'.");

                string fullPath = ResolveSafePath(root, entry.Path);
                if (!File.Exists(fullPath))
                    throw Fail("missing_file", $"Manifest file is missing: '{entry.Path}'.");
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw Fail("unsafe_file", $"Symbolic links and reparse points are not allowed: '{entry.Path}'.");

                var info = new FileInfo(fullPath);
                if (info.Length != entry.SizeBytes)
                    throw Fail("size_mismatch", $"Size mismatch for '{entry.Path}'. Expected {entry.SizeBytes}, found {info.Length}.");
                totalBytes = checked(totalBytes + info.Length);
                if (totalBytes > _options.MaximumPackageBytes)
                    throw Fail("package_too_large", $"Episode exceeds the configured {_options.MaximumPackageBytes} byte limit.");

                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    string actual = ToHex(sha.ComputeHash(stream));
                    if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw Fail("hash_mismatch", $"SHA-256 mismatch for '{entry.Path}'.");
                }
            }

            var discovered = EnumerateFilesWithoutLinks(root);
            foreach (string relative in discovered)
            {
                if (string.Equals(relative, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (!listed.ContainsKey(relative))
                    throw Fail("unlisted_file", $"Package contains a file not declared by the manifest: '{relative}'.");
            }
            foreach (string relative in listed.Keys)
            {
                if (!discovered.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    throw Fail("missing_file", $"Manifest entry was not found in package inventory: '{relative}'.");
            }

            if (!listed.TryGetValue("episode.json", out var descriptorEntry) ||
                !string.Equals(descriptorEntry.Kind, "episode_descriptor", StringComparison.Ordinal))
                throw Fail("descriptor_not_manifested", "episode.json must be listed as episode_descriptor.");
        }

        private List<string> EnumerateFilesWithoutLinks(string root)
        {
            var result = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw Fail("unsafe_link", $"Symbolic links and reparse points are not allowed: '{entry}'.");
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                    else
                    {
                        string relative = entry.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                        result.Add(relative);
                    }
                }
            }
            return result;
        }

        private void IndexAssets(LoadedEpisodePackage package)
        {
            foreach (var asset in package.Manifest.Assets)
            {
                ValidateRelativePath(asset.Path);
                if (!package.AssetsById.TryAdd(asset.AssetId, asset))
                    throw Fail("duplicate_asset_id", $"Duplicate asset ID '{asset.AssetId}'.");
                var file = package.Manifest.Files.FirstOrDefault(x => string.Equals(x.Path, asset.Path, StringComparison.OrdinalIgnoreCase));
                if (file == null || !string.Equals(file.Kind, "media", StringComparison.Ordinal))
                    throw Fail("asset_file_missing", $"Asset '{asset.AssetId}' must reference a manifest file with kind 'media'.");
                if (!package.RuntimeDefinition.AssetIds.Add(asset.AssetId))
                    throw Fail("duplicate_asset_id", $"Duplicate asset ID '{asset.AssetId}'.");
                string expectedPrefix = asset.Type + "/";
                if (!asset.MimeType.StartsWith(expectedPrefix, StringComparison.Ordinal))
                    throw Fail("asset_mime_mismatch", $"Asset '{asset.AssetId}' has a MIME type inconsistent with '{asset.Type}'.");
            }
        }

        private void LoadDocuments(LoadedEpisodePackage package)
        {
            foreach (var file in package.Manifest.Files)
            {
                if (file.Kind == "episode_descriptor" || file.Kind == "media" || file.Kind == "other") continue;
                if (file.SizeBytes > _options.MaximumJsonFileBytes)
                    throw Fail("json_too_large", $"JSON file '{file.Path}' exceeds the configured size limit.");

                string fullPath = ResolveSafePath(package.RootDirectory, file.Path);
                using (JsonDocument document = ReadJson(fullPath, _options.MaximumJsonFileBytes))
                {
                    JsonElement root = document.RootElement.Clone();
                    switch (file.Kind)
                    {
                        case "character_profile":
                            AddDocument(package.CharacterProfiles, RequiredString(root, "character_id", file.Path), root, file.Path);
                            break;
                        case "conversation":
                            AddDocument(package.Conversations, RequiredString(root, "conversation_id", file.Path), root, file.Path);
                            break;
                        case "character_post":
                            AddDocument(package.CharacterPosts, RequiredString(root, "post_id", file.Path), root, file.Path);
                            break;
                        case "call":
                            AddDocument(package.Calls, RequiredString(root, "call_id", file.Path), root, file.Path);
                            break;
                        case "minigame":
                            AddDocument(package.Minigames, RequiredString(root, "minigame_id", file.Path), root, file.Path);
                            break;
                        case "event":
                            AddDocument(package.EventDocuments, RequiredString(root, "event_id", file.Path), root, file.Path);
                            package.RuntimeDefinition.Events.Add(ParseEvent(root, file.Path));
                            break;
                        case "localization":
                            string locale = Path.GetFileNameWithoutExtension(file.Path);
                            AddLocalization(package, locale, root, file.Path);
                            break;
                        default:
                            throw Fail("unsupported_file_kind", $"Unsupported manifest file kind '{file.Kind}' at '{file.Path}'.");
                    }
                }
            }

            foreach (string id in package.CharacterProfiles.Keys) package.RuntimeDefinition.CharacterIds.Add(id);
            foreach (string id in package.Conversations.Keys) package.RuntimeDefinition.ConversationIds.Add(id);
            foreach (string id in package.Calls.Keys) package.RuntimeDefinition.CallIds.Add(id);
            foreach (string id in package.Minigames.Keys) package.RuntimeDefinition.MinigameIds.Add(id);
        }

        private void ValidateReferences(LoadedEpisodePackage package)
        {
            var runtime = package.RuntimeDefinition;
            if (!package.EventDocuments.ContainsKey(package.Descriptor.EntryEventId))
                throw Fail("entry_event_missing", $"Entry event '{package.Descriptor.EntryEventId}' does not exist.");

            if (!package.Localization.ContainsKey(package.Descriptor.DefaultLocale))
                throw Fail("default_locale_missing", $"Default locale '{package.Descriptor.DefaultLocale}' has no localization file.");
            foreach (string locale in package.Descriptor.SupportedLocales)
                if (!package.Localization.ContainsKey(locale))
                    throw Fail("locale_missing", $"Supported locale '{locale}' has no localization file.");

            foreach (var pair in package.CharacterProfiles)
            {
                var profile = pair.Value;
                string? avatar = OptionalString(profile, "avatar_asset_id");
                if (!string.IsNullOrEmpty(avatar) && !runtime.AssetIds.Contains(avatar))
                    throw Fail("avatar_asset_missing", $"Character '{pair.Key}' references missing avatar asset '{avatar}'.");
                foreach (string id in StringArray(profile, "conversation_ids"))
                    if (!runtime.ConversationIds.Contains(id)) throw Fail("conversation_missing", $"Character '{pair.Key}' references missing conversation '{id}'.");
                foreach (string id in StringArray(profile, "post_ids"))
                    if (!package.CharacterPosts.ContainsKey(id)) throw Fail("post_missing", $"Character '{pair.Key}' references missing post '{id}'.");
            }

            foreach (var pair in package.Conversations)
            {
                var conversation = pair.Value;
                var participants = new HashSet<string>(StringArray(conversation, "participants"), StringComparer.Ordinal);
                foreach (string participant in participants)
                    if (participant != "player" && !runtime.CharacterIds.Contains(participant))
                        throw Fail("participant_missing", $"Conversation '{pair.Key}' references unknown participant '{participant}'.");
                var messages = conversation.GetProperty("messages");
                var localMessageIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var message in messages.EnumerateArray())
                {
                    string messageId = RequiredString(message, "message_id", pair.Key);
                    if (!runtime.MessageIds.Add(messageId) || !localMessageIds.Add(messageId))
                        throw Fail("duplicate_message_id", $"Duplicate message ID '{messageId}'.");
                    string type = RequiredString(message, "type", messageId);
                    if (type != "system")
                    {
                        string sender = RequiredString(message, "sender_id", messageId);
                        if (!participants.Contains(sender)) throw Fail("sender_not_participant", $"Sender '{sender}' is not a participant in '{pair.Key}'.");
                    }
                    string? assetId = OptionalString(message, "asset_id");
                    if (!string.IsNullOrEmpty(assetId) && !runtime.AssetIds.Contains(assetId))
                        throw Fail("message_asset_missing", $"Message '{messageId}' references missing asset '{assetId}'.");
                    string? callId = OptionalString(message, "call_id");
                    if (!string.IsNullOrEmpty(callId) && !runtime.CallIds.Contains(callId))
                        throw Fail("call_missing", $"Message '{messageId}' references missing call '{callId}'.");
                    if (type == "choice")
                    {
                        foreach (var option in message.GetProperty("options").EnumerateArray())
                        {
                            string optionId = RequiredString(option, "option_id", messageId);
                            if (!runtime.ChoiceOptionIds.Add(optionId)) throw Fail("duplicate_option_id", $"Duplicate choice option ID '{optionId}'.");
                        }
                    }
                }
                foreach (var message in messages.EnumerateArray())
                {
                    if (OptionalString(message, "type") != "choice") continue;
                    foreach (var option in message.GetProperty("options").EnumerateArray())
                    {
                        string? next = OptionalString(option, "next_message_id");
                        if (!string.IsNullOrEmpty(next) && !localMessageIds.Contains(next))
                            throw Fail("choice_target_missing", $"Choice in '{pair.Key}' points to unknown message '{next}'.");
                    }
                }
            }

            foreach (var pair in package.CharacterPosts)
            {
                string author = RequiredString(pair.Value, "author_id", pair.Key);
                if (!runtime.CharacterIds.Contains(author)) throw Fail("post_author_missing", $"Post '{pair.Key}' has unknown author '{author}'.");
                foreach (string asset in StringArray(pair.Value, "media_asset_ids"))
                    if (!runtime.AssetIds.Contains(asset)) throw Fail("post_asset_missing", $"Post '{pair.Key}' references missing asset '{asset}'.");
            }

            foreach (var pair in package.Calls)
            {
                var call = pair.Value;
                string mode = RequiredString(call, "mode", pair.Key);
                var participants = new HashSet<string>(StringArray(call, "participants"), StringComparer.Ordinal);
                foreach (string participant in participants)
                    if (!runtime.CharacterIds.Contains(participant)) throw Fail("call_participant_missing", $"Call '{pair.Key}' references unknown character '{participant}'.");
                var media = call.GetProperty("media");
                CheckCallAsset(package, pair.Key, media, mode == "video" ? "video_asset_id" : "audio_asset_id", mode);
                CheckCallAsset(package, pair.Key, media, "audio_asset_id", "audio", true);
                CheckCallAsset(package, pair.Key, media, "video_asset_id", "video", true);
                CheckCallAsset(package, pair.Key, media, "poster_asset_id", "image", true);
                int duration = call.GetProperty("duration_ms").GetInt32();
                foreach (var track in call.GetProperty("subtitle_tracks").EnumerateArray())
                {
                    string locale = RequiredString(track, "locale", pair.Key);
                    if (!package.Descriptor.SupportedLocales.Contains(locale))
                        throw Fail("subtitle_locale_unsupported", $"Call '{pair.Key}' uses unsupported subtitle locale '{locale}'.");
                    foreach (var entry in track.GetProperty("entries").EnumerateArray())
                    {
                        int start = entry.GetProperty("start_ms").GetInt32();
                        int end = entry.GetProperty("end_ms").GetInt32();
                        if (start >= end || end > duration) throw Fail("subtitle_timing_invalid", $"Call '{pair.Key}' has subtitle timing outside its duration.");
                        string? speaker = OptionalString(entry, "speaker_id");
                        if (!string.IsNullOrEmpty(speaker) && !participants.Contains(speaker))
                            throw Fail("subtitle_speaker_missing", $"Call '{pair.Key}' subtitle speaker '{speaker}' is not a participant.");
                    }
                }
                foreach (var cue in call.GetProperty("cues").EnumerateArray())
                {
                    if (cue.GetProperty("at_ms").GetInt32() > duration)
                        throw Fail("call_cue_timing_invalid", $"Call '{pair.Key}' contains a cue beyond its duration.");
                    string? speaker = OptionalString(cue, "speaker_id");
                    if (!string.IsNullOrEmpty(speaker) && !participants.Contains(speaker))
                        throw Fail("call_cue_speaker_missing", $"Call '{pair.Key}' cue speaker '{speaker}' is not a participant.");
                }
                string completion = RequiredString(call, "completion_event_id", pair.Key);
                if (!package.RuntimeDefinition.Events.Any(e => e.EventId == completion && e.TriggerType == StoryTriggerType.CallEnded && e.TriggerReferenceId == pair.Key))
                    throw Fail("call_completion_event_missing", $"Call '{pair.Key}' has no matching call_ended completion event.");
            }

            foreach (var pair in package.Minigames)
            {
                string completion = RequiredString(pair.Value, "completion_event_id", pair.Key);
                if (!package.RuntimeDefinition.Events.Any(e => e.EventId == completion && e.TriggerType == StoryTriggerType.MinigameCompleted && e.TriggerReferenceId == pair.Key))
                    throw Fail("minigame_completion_event_missing", $"Minigame '{pair.Key}' has no matching minigame_completed completion event.");
            }

            foreach (var storyEvent in runtime.Events)
            {
                if (storyEvent.TriggerType == StoryTriggerType.MessageCompleted && !runtime.MessageIds.Contains(storyEvent.TriggerReferenceId ?? string.Empty))
                    throw Fail("event_message_missing", $"Event '{storyEvent.EventId}' references an unknown message.");
                if (storyEvent.TriggerType == StoryTriggerType.ChoiceSelected && !runtime.ChoiceOptionIds.Contains(storyEvent.TriggerReferenceId ?? string.Empty))
                    throw Fail("event_choice_missing", $"Event '{storyEvent.EventId}' references an unknown choice option.");
                if (storyEvent.TriggerType == StoryTriggerType.CallEnded && !runtime.CallIds.Contains(storyEvent.TriggerReferenceId ?? string.Empty))
                    throw Fail("event_call_missing", $"Event '{storyEvent.EventId}' references an unknown call.");
                if (storyEvent.TriggerType == StoryTriggerType.MinigameCompleted && !runtime.MinigameIds.Contains(storyEvent.TriggerReferenceId ?? string.Empty))
                    throw Fail("event_minigame_missing", $"Event '{storyEvent.EventId}' references an unknown minigame.");
            }

            foreach (var storyEvent in runtime.Events)
            {
                foreach (var action in storyEvent.Actions)
                {
                    switch (action.Type)
                    {
                        case StoryActionType.OpenConversation:
                            if (!runtime.ConversationIds.Contains(action.TargetId ?? string.Empty))
                                throw Fail("event_conversation_missing", $"Event '{storyEvent.EventId}' opens an unknown conversation.");
                            break;
                        case StoryActionType.UnlockAsset:
                            if (!runtime.AssetIds.Contains(action.TargetId ?? string.Empty))
                                throw Fail("event_asset_missing", $"Event '{storyEvent.EventId}' unlocks an unknown asset.");
                            break;
                        case StoryActionType.StartCall:
                            if (!runtime.CallIds.Contains(action.TargetId ?? string.Empty))
                                throw Fail("event_call_missing", $"Event '{storyEvent.EventId}' starts an unknown call.");
                            break;
                        case StoryActionType.StartMinigame:
                            if (!runtime.MinigameIds.Contains(action.TargetId ?? string.Empty))
                                throw Fail("event_minigame_missing", $"Event '{storyEvent.EventId}' starts an unknown minigame.");
                            break;
                    }
                }
            }

            ValidateLocalizationCoverage(package);
        }

        private static void CheckCallAsset(LoadedEpisodePackage package, string callId, JsonElement media, string property, string expectedType, bool optional = false)
        {
            string? assetId = OptionalString(media, property);
            if (string.IsNullOrEmpty(assetId))
            {
                if (!optional) throw Fail("call_media_missing", $"Call '{callId}' requires '{property}'.");
                return;
            }
            if (!package.AssetsById.TryGetValue(assetId, out var asset))
                throw Fail("call_asset_missing", $"Call '{callId}' references missing asset '{assetId}'.");
            if (!string.Equals(asset.Type, expectedType, StringComparison.Ordinal))
                throw Fail("call_asset_type_mismatch", $"Call '{callId}' expects {expectedType} asset '{assetId}', found '{asset.Type}'.");
        }

        private static void ValidateLocalizationCoverage(LoadedEpisodePackage package)
        {
            var dictionary = package.Localization[package.Descriptor.DefaultLocale];
            var required = new HashSet<string>(StringComparer.Ordinal)
            {
                package.Descriptor.TitleKey
            };
            if (!string.IsNullOrEmpty(package.Descriptor.DescriptionKey)) required.Add(package.Descriptor.DescriptionKey);
            foreach (var p in package.CharacterProfiles.Values)
            {
                AddIfPresent(required, p, "display_name_key"); AddIfPresent(required, p, "bio_key"); AddIfPresent(required, p, "status_key");
            }
            foreach (var p in package.Conversations.Values)
                foreach (var message in p.GetProperty("messages").EnumerateArray())
                {
                    AddIfPresent(required, message, "text_key"); AddIfPresent(required, message, "caption_key"); AddIfPresent(required, message, "prompt_key");
                    if (message.TryGetProperty("options", out var options))
                        foreach (var option in options.EnumerateArray()) AddIfPresent(required, option, "text_key");
                }
            foreach (var p in package.CharacterPosts.Values) AddIfPresent(required, p, "body_key");
            foreach (var p in package.Minigames.Values) { AddIfPresent(required, p, "title_key"); AddIfPresent(required, p, "instructions_key"); }
            foreach (var p in package.Calls.Values)
            {
                foreach (var track in p.GetProperty("subtitle_tracks").EnumerateArray())
                    foreach (var entry in track.GetProperty("entries").EnumerateArray()) AddIfPresent(required, entry, "text_key");
                foreach (var cue in p.GetProperty("cues").EnumerateArray()) AddIfPresent(required, cue, "text_key");
            }
            foreach (var p in package.EventDocuments.Values)
                foreach (var action in p.GetProperty("actions").EnumerateArray()) { AddIfPresent(required, action, "title_key"); AddIfPresent(required, action, "body_key"); }

            foreach (string key in required)
                if (!dictionary.ContainsKey(key)) throw Fail("localization_key_missing", $"Default locale '{package.Descriptor.DefaultLocale}' is missing key '{key}'.");
        }

        private static StoryEventDefinition ParseEvent(JsonElement root, string path)
        {
            var trigger = root.GetProperty("trigger");
            string triggerType = RequiredString(trigger, "type", path);
            var result = new StoryEventDefinition
            {
                EventId = RequiredString(root, "event_id", path),
                TriggerType = triggerType switch
                {
                    "episode_started" => StoryTriggerType.EpisodeStarted,
                    "message_completed" => StoryTriggerType.MessageCompleted,
                    "choice_selected" => StoryTriggerType.ChoiceSelected,
                    "call_ended" => StoryTriggerType.CallEnded,
                    "minigame_completed" => StoryTriggerType.MinigameCompleted,
                    _ => throw Fail("unsupported_trigger", $"Unsupported trigger '{triggerType}' in '{path}'.")
                },
                TriggerReferenceId = triggerType switch
                {
                    "message_completed" => RequiredString(trigger, "message_id", path),
                    "choice_selected" => RequiredString(trigger, "option_id", path),
                    "call_ended" => RequiredString(trigger, "call_id", path),
                    "minigame_completed" => RequiredString(trigger, "minigame_id", path),
                    _ => null
                }
            };

            foreach (var action in root.GetProperty("actions").EnumerateArray())
            {
                string type = RequiredString(action, "type", path);
                var parsed = new StoryActionDefinition
                {
                    Type = type switch
                    {
                        "open_conversation" => StoryActionType.OpenConversation,
                        "show_notification" => StoryActionType.ShowNotification,
                        "unlock_asset" => StoryActionType.UnlockAsset,
                        "set_story_flag" => StoryActionType.SetStoryFlag,
                        "start_call" => StoryActionType.StartCall,
                        "start_minigame" => StoryActionType.StartMinigame,
                        _ => throw Fail("unsupported_action", $"Unsupported action '{type}' in '{path}'.")
                    }
                };
                switch (type)
                {
                    case "open_conversation": parsed.TargetId = RequiredString(action, "conversation_id", path); break;
                    case "unlock_asset": parsed.TargetId = RequiredString(action, "asset_id", path); break;
                    case "start_call": parsed.TargetId = RequiredString(action, "call_id", path); break;
                    case "start_minigame": parsed.TargetId = RequiredString(action, "minigame_id", path); break;
                    case "show_notification":
                        parsed.TitleKey = RequiredString(action, "title_key", path);
                        parsed.BodyKey = RequiredString(action, "body_key", path);
                        break;
                    case "set_story_flag":
                        parsed.Flag = RequiredString(action, "flag", path);
                        var value = action.GetProperty("value");
                        parsed.FlagValue = value.ValueKind switch
                        {
                            JsonValueKind.True => StoryFlagValue.FromBoolean(true),
                            JsonValueKind.False => StoryFlagValue.FromBoolean(false),
                            JsonValueKind.Number => StoryFlagValue.FromNumber(value.GetDouble()),
                            JsonValueKind.String => StoryFlagValue.FromString(value.GetString() ?? string.Empty),
                            _ => throw Fail("invalid_flag_value", $"Invalid flag value in '{path}'.")
                        };
                        break;
                }
                result.Actions.Add(parsed);
            }
            if (result.Actions.Count == 0) throw Fail("empty_event", $"Event '{result.EventId}' has no actions.");
            return result;
        }

        private EpisodePackageDescriptor ParseDescriptor(JsonElement root)
        {
            var descriptor = new EpisodePackageDescriptor
            {
                SchemaVersion = RequiredString(root, "schema_version", "episode.json"),
                EpisodeId = RequiredString(root, "episode_id", "episode.json"),
                EpisodeVersion = RequiredString(root, "episode_version", "episode.json"),
                TitleKey = RequiredString(root, "title_key", "episode.json"),
                DescriptionKey = OptionalString(root, "description_key"),
                MinimumRuntimeVersion = RequiredString(root, "minimum_runtime_version", "episode.json"),
                EntryEventId = RequiredString(root, "entry_event", "episode.json"),
                DefaultLocale = RequiredString(root, "default_locale", "episode.json"),
                Manifest = RequiredString(root, "manifest", "episode.json")
            };
            descriptor.SupportedLocales.AddRange(StringArray(root, "supported_locales"));
            return descriptor;
        }

        private static EpisodeManifest ParseManifest(JsonElement root)
        {
            var manifest = new EpisodeManifest
            {
                SchemaVersion = RequiredString(root, "schema_version", "manifest.json"),
                EpisodeId = RequiredString(root, "episode_id", "manifest.json")
            };
            foreach (var item in root.GetProperty("files").EnumerateArray())
                manifest.Files.Add(new EpisodeManifestFile
                {
                    Path = RequiredString(item, "path", "manifest.json"),
                    Kind = RequiredString(item, "kind", "manifest.json"),
                    SizeBytes = item.GetProperty("size_bytes").GetInt64(),
                    Sha256 = RequiredString(item, "sha256", "manifest.json")
                });
            foreach (var item in root.GetProperty("assets").EnumerateArray())
                manifest.Assets.Add(new EpisodeManifestAsset
                {
                    AssetId = RequiredString(item, "asset_id", "manifest.json"),
                    Type = RequiredString(item, "type", "manifest.json"),
                    Path = RequiredString(item, "path", "manifest.json"),
                    MimeType = RequiredString(item, "mime_type", "manifest.json")
                });
            return manifest;
        }

        private void AddLocalization(LoadedEpisodePackage package, string locale, JsonElement root, string path)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw Fail("invalid_localization", $"Localization value '{property.Name}' in '{path}' must be a string.");
                values.Add(property.Name, property.Value.GetString() ?? string.Empty);
            }
            if (!package.Localization.TryAdd(locale, values))
                throw Fail("duplicate_locale", $"Multiple localization dictionaries found for locale '{locale}'.");
        }

        private JsonDocument ReadJson(string path, int maxBytes)
        {
            if (!File.Exists(path)) throw Fail("required_json_missing", $"Required JSON file is missing: '{path}'.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw Fail("unsafe_json_link", $"Symbolic links and reparse points are not allowed: '{path}'.");
            var info = new FileInfo(path);
            if (info.Length > maxBytes) throw Fail("json_too_large", $"JSON file exceeds {maxBytes} bytes: '{path}'.");
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return JsonDocument.Parse(bytes, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = _options.MaximumJsonDepth
                });
            }
            catch (JsonException exception)
            {
                throw new EpisodePackageLoadException("invalid_json", $"Invalid JSON in '{path}': {exception.Message}", exception);
            }
            catch (IOException exception)
            {
                throw new EpisodePackageLoadException("file_read_failed", $"Could not read '{path}': {exception.Message}", exception);
            }
        }

        private static void EnsureRuntimeCompatible(string minimum, string current)
        {
            if (!TryVersion(minimum, out var required) || !TryVersion(current, out var available))
                throw Fail("invalid_runtime_version", "Runtime versions must use numeric major.minor.patch values.");
            if (available < required)
                throw Fail("runtime_too_old", $"Episode requires runtime {minimum} or newer; current runtime is {current}.");
        }

        private static bool TryVersion(string value, out Version version)
        {
            string core = value.Split('-', '+')[0];
            if (Version.TryParse(core, out var parsed))
            {
                version = parsed;
                return true;
            }
            version = new Version(0, 0, 0);
            return false;
        }

        private static string ResolveSafePath(string root, string relative)
        {
            ValidateRelativePath(relative);
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw Fail("path_escape", $"Path escapes episode root: '{relative}'.");
            return full;
        }

        private static void ValidateRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Contains("\\") || path.Contains(":") || path.Contains("\0"))
                throw Fail("unsafe_path", $"Unsafe package path '{path}'.");
            foreach (string segment in path.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw Fail("unsafe_path", $"Unsafe package path '{path}'.");
            foreach (char c in path)
                if (!(char.IsLetterOrDigit(c) || c == '/' || c == '.' || c == '_' || c == '-'))
                    throw Fail("unsafe_path", $"Unsupported character in package path '{path}'.");
        }

        private static bool IsSha256(string value) =>
            value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private static string RequiredString(JsonElement element, string property, string context)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw Fail("required_property_missing", $"'{context}' is missing string property '{property}'.");
            return value.GetString()!;
        }

        private static string? OptionalString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static List<string> StringArray(JsonElement element, string property)
        {
            var result = new List<string>();
            if (!element.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return result;
            foreach (var value in values.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String) result.Add(value.GetString() ?? string.Empty);
            return result;
        }

        private static void AddIfPresent(HashSet<string> values, JsonElement element, string property)
        {
            string? value = OptionalString(element, property);
            if (!string.IsNullOrEmpty(value)) values.Add(value);
        }

        private static void AddDocument(Dictionary<string, JsonElement> target, string id, JsonElement document, string path)
        {
            if (!target.TryAdd(id, document))
                throw Fail("duplicate_document_id", $"Duplicate document ID '{id}' in '{path}'.");
        }

        private static EpisodePackageLoadException Fail(string code, string message) => new EpisodePackageLoadException(code, message);

    }
}

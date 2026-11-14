using System.Security.Cryptography;
using System.Text.Json;

namespace Nightwire.EpisodeValidator;

internal static class Program
{
    private const string SupportedSchemaVersion = "0.1";
    private static readonly List<string> Errors = [];
    private static readonly List<string> Warnings = [];

    private sealed record ManifestFile(string Path, string Kind, long SizeBytes, string Sha256);
    private sealed record Asset(string Id, string Type, string Path);

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var root = Path.GetFullPath(args[0]);
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"ERROR: Episode directory does not exist: {root}");
            return 2;
        }

        try
        {
            await ValidatePackageAsync(root);
        }
        catch (Exception ex)
        {
            Errors.Add($"Unexpected validator failure: {ex.Message}");
        }

        foreach (var warning in Warnings)
            Console.WriteLine($"WARNING: {warning}");

        foreach (var error in Errors)
            Console.Error.WriteLine($"ERROR: {error}");

        if (Errors.Count > 0)
        {
            Console.Error.WriteLine($"Validation failed: {Errors.Count} error(s), {Warnings.Count} warning(s).");
            return 1;
        }

        Console.WriteLine($"Validation passed: {Warnings.Count} warning(s).");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("NIGHTWIRE Episode Validator");
        Console.WriteLine("Usage: dotnet run --project tools/Nightwire.EpisodeValidator -- <episode-directory>");
        Console.WriteLine("Checks package structure, manifest integrity, references, and localization coverage.");
    }

    private static async Task ValidatePackageAsync(string root)
    {
        var episodePath = Path.Combine(root, "episode.json");
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(episodePath)) Errors.Add("Missing required file: episode.json");
        if (!File.Exists(manifestPath)) Errors.Add("Missing required file: manifest.json");
        if (Errors.Count > 0) return;

        using var episodeDoc = await ReadJsonAsync(episodePath, "episode.json");
        using var manifestDoc = await ReadJsonAsync(manifestPath, "manifest.json");
        if (episodeDoc is null || manifestDoc is null) return;

        var episode = episodeDoc.RootElement;
        var manifest = manifestDoc.RootElement;
        RequireString(episode, "schema_version", "episode.json", SupportedSchemaVersion);
        RequireString(manifest, "schema_version", "manifest.json", SupportedSchemaVersion);

        var episodeId = GetString(episode, "episode_id");
        var manifestEpisodeId = GetString(manifest, "episode_id");
        if (string.IsNullOrWhiteSpace(episodeId)) Errors.Add("episode.json: missing episode_id.");
        if (!string.Equals(episodeId, manifestEpisodeId, StringComparison.Ordinal))
            Errors.Add("manifest.json: episode_id does not match episode.json.");

        var defaultLocale = GetString(episode, "default_locale");
        var locales = GetStringArray(episode, "supported_locales", "episode.json");
        if (!string.IsNullOrWhiteSpace(defaultLocale) && !locales.Contains(defaultLocale, StringComparer.Ordinal))
            Errors.Add("episode.json: default_locale must appear in supported_locales.");

        var files = ReadManifestFiles(manifest);
        var assets = ReadAssets(manifest);
        await ValidateManifestFilesAsync(root, files);
        ValidatePackageFileInventory(root, files);

        var assetById = new Dictionary<string, Asset>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            if (!assetById.TryAdd(asset.Id, asset))
                Errors.Add($"manifest.json: duplicate asset_id '{asset.Id}'.");
            if (!files.Any(f => string.Equals(f.Path, asset.Path, StringComparison.Ordinal)))
                Errors.Add($"manifest.json: asset '{asset.Id}' points to an unlisted file '{asset.Path}'.");
        }

        var characters = new HashSet<string>(StringComparer.Ordinal);
        var conversations = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var events = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var calls = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var minigames = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var posts = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var allLocalizationKeys = new HashSet<string>(StringComparer.Ordinal);
        CollectLocalizationKeys(episode, allLocalizationKeys);

        foreach (var file in files)
        {
            var fullPath = SafeCombine(root, file.Path);
            if (fullPath is null || !File.Exists(fullPath)) continue;

            switch (file.Kind)
            {
                case "character_profile":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        var id = GetString(doc.RootElement, "character_id");
                        if (string.IsNullOrWhiteSpace(id)) Errors.Add($"{file.Path}: missing character_id.");
                        else if (!characters.Add(id)) Errors.Add($"{file.Path}: duplicate character_id '{id}'.");
                        CollectLocalizationKeys(doc.RootElement, allLocalizationKeys);
                    }
                    break;

                case "conversation":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        var id = GetString(doc.RootElement, "conversation_id");
                        if (string.IsNullOrWhiteSpace(id)) Errors.Add($"{file.Path}: missing conversation_id.");
                        else if (!conversations.TryAdd(id, doc.RootElement.Clone()))
                            Errors.Add($"{file.Path}: duplicate conversation_id '{id}'.");
                        CollectLocalizationKeys(doc.RootElement, allLocalizationKeys);
                    }
                    break;

                case "event":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        var id = GetString(doc.RootElement, "event_id");
                        if (string.IsNullOrWhiteSpace(id)) Errors.Add($"{file.Path}: missing event_id.");
                        else if (!events.TryAdd(id, doc.RootElement.Clone()))
                            Errors.Add($"{file.Path}: duplicate event_id '{id}'.");
                        CollectLocalizationKeys(doc.RootElement, allLocalizationKeys);
                    }
                    break;

                case "call":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        var id = GetString(doc.RootElement, "call_id");
                        if (string.IsNullOrWhiteSpace(id)) Errors.Add($"{file.Path}: missing call_id.");
                        else if (!calls.TryAdd(id, doc.RootElement.Clone())) Errors.Add($"{file.Path}: duplicate call_id '{id}'.");
                        CollectLocalizationKeys(doc.RootElement, allLocalizationKeys);
                    }
                    break;

                case "minigame":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        var id = GetString(doc.RootElement, "minigame_id");
                        if (string.IsNullOrWhiteSpace(id)) Errors.Add($"{file.Path}: missing minigame_id.");
                        else if (!minigames.TryAdd(id, doc.RootElement.Clone())) Errors.Add($"{file.Path}: duplicate minigame_id '{id}'.");
                        CollectLocalizationKeys(doc.RootElement, allLocalizationKeys);
                    }
                    break;

                case "character_post":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        var id = GetString(doc.RootElement, "post_id");
                        if (string.IsNullOrWhiteSpace(id)) Errors.Add($"{file.Path}: missing post_id.");
                        else if (!posts.TryAdd(id, doc.RootElement.Clone())) Errors.Add($"{file.Path}: duplicate post_id '{id}'.");
                        CollectLocalizationKeys(doc.RootElement, allLocalizationKeys);
                    }
                    break;

                case "localization":
                    using (var doc = await ReadJsonAsync(fullPath, file.Path))
                    {
                        if (doc is null) break;
                        if (doc.RootElement.ValueKind != JsonValueKind.Object)
                            Errors.Add($"{file.Path}: localization root must be an object.");
                        else
                        {
                            foreach (var property in doc.RootElement.EnumerateObject())
                                if (property.Value.ValueKind != JsonValueKind.String)
                                    Errors.Add($"{file.Path}: localization value '{property.Name}' must be a string.");
                        }
                    }
                    break;
            }
        }

        var entryEvent = GetString(episode, "entry_event");
        if (!string.IsNullOrWhiteSpace(entryEvent) && !events.ContainsKey(entryEvent))
            Errors.Add($"episode.json: entry_event '{entryEvent}' does not resolve to an event.");

        ValidateCharactersAndConversations(files, root, characters, conversations, posts);
        ValidatePosts(posts, characters, assetById);
        ValidateCalls(calls, characters, assetById, events, locales);
        ValidateMinigames(minigames, assetById, events);
        var messageIds = ValidateConversations(conversations, characters, assetById, calls);
        ValidateEvents(events, conversations, calls, minigames, assetById, messageIds);

        foreach (var locale in locales)
        {
            var localePath = $"Localization/{locale}.json";
            var fullPath = SafeCombine(root, localePath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                Errors.Add($"Missing localization file for declared locale '{locale}': {localePath}");
                continue;
            }

            using var doc = await ReadJsonAsync(fullPath, localePath);
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) continue;
            foreach (var key in allLocalizationKeys)
                if (!doc.RootElement.TryGetProperty(key, out _))
                    Errors.Add($"{localePath}: missing localization key '{key}'.");
        }

        Console.WriteLine($"Episode: {episodeId ?? "(unknown)"}");
        Console.WriteLine($"Manifest files: {files.Count}; assets: {assets.Count}; characters: {characters.Count}; posts: {posts.Count}; conversations: {conversations.Count}; calls: {calls.Count}; minigames: {minigames.Count}; events: {events.Count}; locales: {locales.Count}");
    }

    private static async Task<JsonDocument?> ReadJsonAsync(string path, string label)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException ex)
        {
            Errors.Add($"{label}: invalid JSON at line {ex.LineNumber}, byte {ex.BytePositionInLine}: {ex.Message}");
        }
        catch (IOException ex)
        {
            Errors.Add($"{label}: could not read file: {ex.Message}");
        }
        return null;
    }

    private static List<ManifestFile> ReadManifestFiles(JsonElement manifest)
    {
        var result = new List<ManifestFile>();
        if (!manifest.TryGetProperty("files", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            Errors.Add("manifest.json: files must be an array.");
            return result;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            var path = GetString(item, "path");
            var kind = GetString(item, "kind");
            var hash = GetString(item, "sha256");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(hash) ||
                !item.TryGetProperty("size_bytes", out var size) || !size.TryGetInt64(out var sizeBytes))
            {
                Errors.Add("manifest.json: each files entry needs path, kind, size_bytes, and sha256.");
                continue;
            }

            if (!paths.Add(path)) Errors.Add($"manifest.json: duplicate or case-colliding path '{path}'.");
            if (string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase))
                Errors.Add("manifest.json: the manifest must not list or hash itself.");
            if (!IsSafeRelativePath(path)) Errors.Add($"manifest.json: unsafe relative path '{path}'.");
            if (kind is not ("episode_descriptor" or "character_profile" or "character_post" or "conversation" or "event" or "call" or "minigame" or "localization" or "media" or "other"))
                Errors.Add($"manifest.json: unsupported file kind '{kind}' for '{path}'.");
            if (sizeBytes < 0) Errors.Add($"manifest.json: negative size_bytes for '{path}'.");
            if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
                Errors.Add($"manifest.json: invalid SHA-256 value for '{path}'.");

            result.Add(new ManifestFile(path, kind, sizeBytes, hash.ToLowerInvariant()));
        }
        return result;
    }

    private static List<Asset> ReadAssets(JsonElement manifest)
    {
        var result = new List<Asset>();
        if (!manifest.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            Errors.Add("manifest.json: assets must be an array.");
            return result;
        }

        foreach (var item in array.EnumerateArray())
        {
            var id = GetString(item, "asset_id");
            var type = GetString(item, "type");
            var path = GetString(item, "path");
            var mime = GetString(item, "mime_type");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(mime))
            {
                Errors.Add("manifest.json: each asset needs asset_id, type, path, and mime_type.");
                continue;
            }
            if (!IsSafeRelativePath(path)) Errors.Add($"manifest.json: unsafe asset path '{path}'.");
            result.Add(new Asset(id, type, path));
        }
        return result;
    }

    private static async Task ValidateManifestFilesAsync(string root, List<ManifestFile> files)
    {
        foreach (var file in files)
        {
            var fullPath = SafeCombine(root, file.Path);
            if (fullPath is null) continue;
            if (!File.Exists(fullPath))
            {
                Errors.Add($"Manifest file is missing: '{file.Path}'.");
                continue;
            }
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                Errors.Add($"Manifest file must not be a symbolic link or reparse point: '{file.Path}'.");
                continue;
            }

            var info = new FileInfo(fullPath);
            if (info.Length != file.SizeBytes)
                Errors.Add($"'{file.Path}': size mismatch (manifest {file.SizeBytes}, actual {info.Length}).");

            await using var stream = File.OpenRead(fullPath);
            var digest = await SHA256.HashDataAsync(stream);
            var actualHash = Convert.ToHexString(digest).ToLowerInvariant();
            if (!string.Equals(actualHash, file.Sha256, StringComparison.Ordinal))
                Errors.Add($"'{file.Path}': SHA-256 mismatch.");
        }
    }

    private static void ValidatePackageFileInventory(string root, List<ManifestFile> files)
    {
        var declared = new HashSet<string>(files.Select(f => f.Path.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            if (string.Equals(relative, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                Errors.Add($"Package contains a symbolic link or reparse point: '{relative}'.");
                continue;
            }
            if (!declared.Contains(relative))
                Errors.Add($"Package file is not listed in manifest.json: '{relative.Replace(Path.DirectorySeparatorChar, '/')}'.");
        }
    }

    private static void ValidateCharactersAndConversations(
        List<ManifestFile> files, string root, HashSet<string> characters,
        Dictionary<string, JsonElement> conversations, Dictionary<string, JsonElement> posts)
    {
        foreach (var file in files.Where(f => f.Kind == "character_profile"))
        {
            var fullPath = SafeCombine(root, file.Path);
            if (fullPath is null || !File.Exists(fullPath)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            foreach (var conversationId in GetStringArray(doc.RootElement, "conversation_ids", file.Path))
                if (!conversations.ContainsKey(conversationId))
                    Errors.Add($"{file.Path}: conversation_id '{conversationId}' does not resolve.");
            foreach (var postId in GetStringArray(doc.RootElement, "post_ids", file.Path))
                if (!posts.ContainsKey(postId))
                    Errors.Add($"{file.Path}: post_id '{postId}' does not resolve.");
        }
    }

    private static HashSet<string> ValidateConversations(
        Dictionary<string, JsonElement> conversations, HashSet<string> characters,
        Dictionary<string, Asset> assets, Dictionary<string, JsonElement> calls)
    {
        var allMessageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (conversationId, conversation) in conversations)
        {
            var participants = GetStringArray(conversation, "participants", conversationId);
            if (!participants.Contains("player", StringComparer.Ordinal))
                Errors.Add($"{conversationId}: participants must include 'player'.");
            foreach (var participant in participants)
                if (participant != "player" && !characters.Contains(participant))
                    Errors.Add($"{conversationId}: participant '{participant}' has no character profile.");

            if (!conversation.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            {
                Errors.Add($"{conversationId}: messages must be an array.");
                continue;
            }

            var localIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in messages.EnumerateArray())
            {
                var messageId = GetString(message, "message_id");
                var type = GetString(message, "type");
                var sender = GetString(message, "sender_id");
                if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(type))
                {
                    Errors.Add($"{conversationId}: message is missing message_id or type.");
                    continue;
                }
                if (!localIds.Add(messageId)) Errors.Add($"{conversationId}: duplicate message_id '{messageId}'.");
                if (!allMessageIds.Add(messageId)) Errors.Add($"Duplicate message_id across episode: '{messageId}'.");
                if (type != "system" && !string.IsNullOrWhiteSpace(sender) &&
                    !participants.Contains(sender, StringComparer.Ordinal))
                    Errors.Add($"{conversationId}/{messageId}: sender '{sender}' is not a participant.");
                if (type != "system" && !string.IsNullOrWhiteSpace(sender) &&
                    sender != "player" && !characters.Contains(sender))
                    Errors.Add($"{conversationId}/{messageId}: sender character '{sender}' has no profile.");
                if (type is not ("text" or "image" or "video" or "audio" or "document" or "choice" or "system" or "call_invitation"))
                    Errors.Add($"{conversationId}/{messageId}: unsupported message type '{type}'.");

                if (type is "image" or "video" or "audio" or "document")
                    CheckAssetReference(message, conversationId, messageId, assets);
                if (type == "call_invitation")
                {
                    var callId = GetString(message, "call_id");
                    if (callId is null || !calls.ContainsKey(callId))
                        Errors.Add($"{conversationId}/{messageId}: call_id '{callId}' does not resolve.");
                }

                if (type == "choice" && message.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
                {
                    var optionIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var option in options.EnumerateArray())
                    {
                        var optionId = GetString(option, "option_id");
                        if (string.IsNullOrWhiteSpace(optionId) || !optionIds.Add(optionId))
                            Errors.Add($"{conversationId}/{messageId}: missing or duplicate option_id '{optionId}'.");
                    }
                }
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (GetString(message, "type") != "choice" ||
                    !message.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) continue;
                foreach (var option in options.EnumerateArray())
                {
                    var next = GetString(option, "next_message_id");
                    if (!string.IsNullOrWhiteSpace(next) && !localIds.Contains(next))
                        Errors.Add($"{conversationId}: choice points to missing message '{next}'.");
                }
            }
        }
        return allMessageIds;
    }

    private static void ValidatePosts(
        Dictionary<string, JsonElement> posts, HashSet<string> characters,
        Dictionary<string, Asset> assets)
    {
        foreach (var (postId, post) in posts)
        {
            var author = GetString(post, "author_id");
            if (author is null || !characters.Contains(author))
                Errors.Add($"Post '{postId}': author_id '{author}' does not resolve to a character.");
            foreach (var assetId in GetStringArray(post, "media_asset_ids", postId))
                if (!assets.ContainsKey(assetId))
                    Errors.Add($"Post '{postId}': media asset '{assetId}' does not resolve.");

            if (post.TryGetProperty("comments", out var comments) && comments.ValueKind == JsonValueKind.Array)
            {
                var commentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var comment in comments.EnumerateArray())
                {
                    var commentId = GetString(comment, "comment_id");
                    var commentAuthor = GetString(comment, "author_id");
                    if (commentId is null || !commentIds.Add(commentId))
                        Errors.Add($"Post '{postId}': missing or duplicate comment_id '{commentId}'.");
                    if (commentAuthor is null || !characters.Contains(commentAuthor))
                        Errors.Add($"Post '{postId}': comment author '{commentAuthor}' does not resolve to a character.");
                }
            }
        }
    }

    private static void ValidateCalls(
        Dictionary<string, JsonElement> calls, HashSet<string> characters,
        Dictionary<string, Asset> assets, Dictionary<string, JsonElement> events,
        List<string> locales)
    {
        foreach (var (callId, call) in calls)
        {
            foreach (var participant in GetStringArray(call, "participants", callId))
                if (!characters.Contains(participant))
                    Errors.Add($"Call '{callId}': participant '{participant}' has no character profile.");

            var mode = GetString(call, "mode");
            if (!call.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
            {
                Errors.Add($"Call '{callId}': media must be an object.");
            }
            else
            {
                var requiredAsset = mode == "video" ? "video_asset_id" : "audio_asset_id";
                var assetId = GetString(media, requiredAsset);
                if (assetId is null || !assets.TryGetValue(assetId, out var asset))
                    Errors.Add($"Call '{callId}': required {requiredAsset} '{assetId}' does not resolve.");
                else if (asset.Type != mode)
                    Errors.Add($"Call '{callId}': {requiredAsset} must reference a '{mode}' asset, got '{asset.Type}'.");
                foreach (var optional in new[] { "audio_asset_id", "video_asset_id", "poster_asset_id" })
                {
                    var optionalId = GetString(media, optional);
                    if (optionalId is not null && !assets.ContainsKey(optionalId))
                        Errors.Add($"Call '{callId}': {optional} '{optionalId}' does not resolve.");
                }
            }

            var duration = call.TryGetProperty("duration_ms", out var durationElement) && durationElement.TryGetInt64(out var durationMs)
                ? durationMs : 0;
            if (call.TryGetProperty("subtitle_tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
            {
                var trackLocales = new HashSet<string>(StringComparer.Ordinal);
                foreach (var track in tracks.EnumerateArray())
                {
                    var locale = GetString(track, "locale");
                    if (locale is null || !trackLocales.Add(locale))
                        Errors.Add($"Call '{callId}': missing or duplicate subtitle locale '{locale}'.");
                    else if (!locales.Contains(locale, StringComparer.Ordinal))
                        Errors.Add($"Call '{callId}': subtitle locale '{locale}' is not declared by the episode.");

                    if (!track.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) continue;
                    foreach (var entry in entries.EnumerateArray())
                    {
                        var start = entry.TryGetProperty("start_ms", out var startValue) && startValue.TryGetInt64(out var startMs) ? startMs : -1;
                        var end = entry.TryGetProperty("end_ms", out var endValue) && endValue.TryGetInt64(out var endMs) ? endMs : -1;
                        if (start < 0 || end <= start || end > duration)
                            Errors.Add($"Call '{callId}': subtitle interval {start}-{end} is outside call duration {duration}.");
                        var speaker = GetString(entry, "speaker_id");
                        if (speaker is not null && !characters.Contains(speaker))
                            Errors.Add($"Call '{callId}': subtitle speaker '{speaker}' has no character profile.");
                    }
                }
            }

            if (call.TryGetProperty("cues", out var cues) && cues.ValueKind == JsonValueKind.Array)
            {
                var cueIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var cue in cues.EnumerateArray())
                {
                    var cueId = GetString(cue, "cue_id");
                    if (cueId is null || !cueIds.Add(cueId))
                        Errors.Add($"Call '{callId}': missing or duplicate cue_id '{cueId}'.");
                    var at = cue.TryGetProperty("at_ms", out var atValue) && atValue.TryGetInt64(out var atMs) ? atMs : -1;
                    if (at < 0 || at > duration)
                        Errors.Add($"Call '{callId}': cue '{cueId}' is outside call duration.");
                    var speaker = GetString(cue, "speaker_id");
                    if (speaker is not null && !characters.Contains(speaker))
                        Errors.Add($"Call '{callId}': cue speaker '{speaker}' has no character profile.");
                }
            }

            var completionEvent = GetString(call, "completion_event_id");
            if (completionEvent is null)
                Errors.Add($"Call '{callId}': missing completion_event_id.");
            else if (
                (!events.TryGetValue(completionEvent, out var completion) ||
                 !completion.TryGetProperty("trigger", out var trigger) ||
                 GetString(trigger, "type") != "call_ended" ||
                 GetString(trigger, "call_id") != callId))
                Errors.Add($"Call '{callId}': completion_event_id '{completionEvent}' must reference its matching call_ended event.");
        }
    }

    private static void ValidateMinigames(
        Dictionary<string, JsonElement> minigames, Dictionary<string, Asset> assets,
        Dictionary<string, JsonElement> events)
    {
        foreach (var (minigameId, minigame) in minigames)
        {
            var type = GetString(minigame, "type");
            if (!minigame.TryGetProperty("configuration", out var config) || config.ValueKind != JsonValueKind.Object)
            {
                Errors.Add($"Minigame '{minigameId}': configuration must be an object.");
                continue;
            }

            if (type == "frequency_tuning")
            {
                var min = GetNumber(config, "minimum_frequency");
                var max = GetNumber(config, "maximum_frequency");
                var target = GetNumber(config, "target_frequency");
                var tolerance = GetNumber(config, "tolerance");
                if (min is null || max is null || target is null || tolerance is null ||
                    min.Value >= max.Value || target.Value < min.Value || target.Value > max.Value || tolerance.Value <= 0)
                    Errors.Add($"Minigame '{minigameId}': frequency range, target, and tolerance are inconsistent.");
            }
            else if (type == "sequence_reconstruction")
            {
                var symbols = GetStringArray(config, "symbols", minigameId).ToHashSet(StringComparer.Ordinal);
                var sequence = GetStringArray(config, "sequence", minigameId);
                if (sequence.Any(symbol => !symbols.Contains(symbol)))
                    Errors.Add($"Minigame '{minigameId}': sequence contains a symbol not declared in symbols.");
            }
            else if (type == "evidence_matching")
            {
                var itemIds = new HashSet<string>(StringComparer.Ordinal);
                if (config.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var itemId = GetString(item, "item_id");
                        if (itemId is null || !itemIds.Add(itemId))
                            Errors.Add($"Minigame '{minigameId}': missing or duplicate item_id '{itemId}'.");
                        var assetId = GetString(item, "asset_id");
                        if (assetId is not null && !assets.ContainsKey(assetId))
                            Errors.Add($"Minigame '{minigameId}': item asset '{assetId}' does not resolve.");
                    }
                }
                if (config.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pair in pairs.EnumerateArray())
                    {
                        var left = GetString(pair, "left_item_id");
                        var right = GetString(pair, "right_item_id");
                        if (left is null || right is null || left == right || !itemIds.Contains(left) || !itemIds.Contains(right))
                            Errors.Add($"Minigame '{minigameId}': evidence pair '{left}'/'{right}' references invalid items.");
                    }
                }
            }
            else
            {
                Errors.Add($"Minigame '{minigameId}': unsupported type '{type}'.");
            }

            var completionEvent = GetString(minigame, "completion_event_id");
            if (completionEvent is not null &&
                (!events.TryGetValue(completionEvent, out var completion) ||
                 !completion.TryGetProperty("trigger", out var trigger) ||
                 GetString(trigger, "type") != "minigame_completed" ||
                 GetString(trigger, "minigame_id") != minigameId))
                Errors.Add($"Minigame '{minigameId}': completion_event_id '{completionEvent}' must reference its matching minigame_completed event.");
        }
    }

    private static bool HasChoiceOption(JsonElement conversation, string optionId)
    {
        if (!conversation.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) return false;
        foreach (var message in messages.EnumerateArray())
        {
            if (GetString(message, "type") != "choice" ||
                !message.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) continue;
            foreach (var option in options.EnumerateArray())
                if (GetString(option, "option_id") == optionId) return true;
        }
        return false;
    }

    private static double? GetNumber(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) ? number : null;
    }

    private static void ValidateEvents(
        Dictionary<string, JsonElement> events, Dictionary<string, JsonElement> conversations,
        Dictionary<string, JsonElement> calls, Dictionary<string, JsonElement> minigames,
        Dictionary<string, Asset> assets, HashSet<string> messageIds)
    {
        foreach (var (eventId, storyEvent) in events)
        {
            if (storyEvent.TryGetProperty("trigger", out var trigger))
            {
                var triggerType = GetString(trigger, "type");
                var reference = triggerType switch
                {
                    "message_completed" => GetString(trigger, "message_id"),
                    "call_ended" => GetString(trigger, "call_id"),
                    "minigame_completed" => GetString(trigger, "minigame_id"),
                    _ => null
                };
                if (triggerType == "message_completed" && (reference is null || !messageIds.Contains(reference)))
                    Errors.Add($"{eventId}: message_completed trigger references missing message '{reference}'.");
                if (triggerType == "call_ended" && (reference is null || !calls.ContainsKey(reference)))
                    Errors.Add($"{eventId}: call_ended trigger references missing call '{reference}'.");
                if (triggerType == "minigame_completed" && (reference is null || !minigames.ContainsKey(reference)))
                    Errors.Add($"{eventId}: minigame_completed trigger references missing minigame '{reference}'.");
                if (triggerType == "choice_selected")
                {
                    var optionId = GetString(trigger, "option_id");
                    if (optionId is null || !conversations.Values.Any(c => HasChoiceOption(c, optionId)))
                        Errors.Add($"{eventId}: choice_selected trigger references missing option '{optionId}'.");
                }
            }
            if (!storyEvent.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var action in actions.EnumerateArray())
            {
                var type = GetString(action, "type");
                if (type == "open_conversation")
                {
                    var id = GetString(action, "conversation_id");
                    if (id is null || !conversations.ContainsKey(id))
                        Errors.Add($"{eventId}: open_conversation references missing conversation '{id}'.");
                }
                else if (type == "unlock_asset")
                {
                    var id = GetString(action, "asset_id");
                    if (id is null || !assets.ContainsKey(id))
                        Errors.Add($"{eventId}: unlock_asset references missing asset '{id}'.");
                }
                else if (type == "start_call")
                {
                    var id = GetString(action, "call_id");
                    if (id is null || !calls.ContainsKey(id))
                        Errors.Add($"{eventId}: start_call references missing call '{id}'.");
                }
                else if (type == "start_minigame")
                {
                    var id = GetString(action, "minigame_id");
                    if (id is null || !minigames.ContainsKey(id))
                        Errors.Add($"{eventId}: start_minigame references missing minigame '{id}'.");
                }
                else if (type is not ("show_notification" or "set_story_flag"))
                    Errors.Add($"{eventId}: unsupported event action '{type}'.");
            }
        }
    }

    private static void CheckAssetReference(JsonElement message, string conversationId, string messageId, Dictionary<string, Asset> assets)
    {
        var id = GetString(message, "asset_id");
        if (id is null || !assets.ContainsKey(id))
            Errors.Add($"{conversationId}/{messageId}: asset_id '{id}' does not resolve.");
    }

    private static void CollectLocalizationKeys(JsonElement element, HashSet<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.EndsWith("_key", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String)
                    keys.Add(property.Value.GetString()!);
                CollectLocalizationKeys(property.Value, keys);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CollectLocalizationKeys(child, keys);
        }
    }

    private static string? SafeCombine(string root, string relative)
    {
        if (!IsSafeRelativePath(relative))
        {
            Errors.Add($"Unsafe package path: '{relative}'.");
            return null;
        }
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            Errors.Add($"Path escapes episode root: '{relative}'.");
            return null;
        }
        return full;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || Path.IsPathRooted(path))
            return false;
        var segments = path.Split('/');
        return segments.All(s => s.Length > 0 && s != "." && s != ".." && !s.Contains(':'));
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static List<string> GetStringArray(JsonElement element, string property, string source)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            Errors.Add($"{source}: '{property}' must be an array.");
            return result;
        }
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                Errors.Add($"{source}: '{property}' contains a non-string or empty value.");
            else result.Add(item.GetString()!);
        }
        return result;
    }

    private static void RequireString(JsonElement element, string property, string source, string? expected = null)
    {
        var value = GetString(element, property);
        if (string.IsNullOrWhiteSpace(value))
            Errors.Add($"{source}: missing or invalid '{property}'.");
        else if (expected is not null && value != expected)
            Errors.Add($"{source}: '{property}' must be '{expected}', got '{value}'.");
    }
}

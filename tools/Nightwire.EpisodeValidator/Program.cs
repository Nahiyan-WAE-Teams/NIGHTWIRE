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
        var allLocalizationKeys = new HashSet<string>(StringComparer.Ordinal);

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

        ValidateCharactersAndConversations(files, root, characters, conversations);
        ValidateConversations(conversations, characters, assetById);
        ValidateEvents(events, conversations, assetById);

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
        Console.WriteLine($"Manifest files: {files.Count}; assets: {assets.Count}; characters: {characters.Count}; conversations: {conversations.Count}; events: {events.Count}; locales: {locales.Count}");
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
            if (!IsSafeRelativePath(path)) Errors.Add($"manifest.json: unsafe relative path '{path}'.");
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
        Dictionary<string, JsonElement> conversations)
    {
        foreach (var file in files.Where(f => f.Kind == "character_profile"))
        {
            var fullPath = SafeCombine(root, file.Path);
            if (fullPath is null || !File.Exists(fullPath)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
            foreach (var conversationId in GetStringArray(doc.RootElement, "conversation_ids", file.Path))
                if (!conversations.ContainsKey(conversationId))
                    Errors.Add($"{file.Path}: conversation_id '{conversationId}' does not resolve.");
        }
    }

    private static void ValidateConversations(
        Dictionary<string, JsonElement> conversations, HashSet<string> characters,
        Dictionary<string, Asset> assets)
    {
        var allMessageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (conversationId, conversation) in conversations)
        {
            var participants = GetStringArray(conversation, "participants", conversationId);
            if (!participants.Contains("player", StringComparer.Ordinal))
                Errors.Add($"{conversationId}: participants must include 'player'.");

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
    }

    private static void ValidateEvents(
        Dictionary<string, JsonElement> events, Dictionary<string, JsonElement> conversations,
        Dictionary<string, Asset> assets)
    {
        foreach (var (eventId, storyEvent) in events)
        {
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
                else if (type is not ("show_notification" or "set_story_flag" or "start_call" or "start_minigame"))
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

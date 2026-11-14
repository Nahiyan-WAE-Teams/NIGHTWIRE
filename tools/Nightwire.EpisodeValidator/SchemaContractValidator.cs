using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Json.Schema;

namespace Nightwire.EpisodeValidator;

internal static class SchemaContractValidator
{
    private static readonly Dictionary<string, JsonSchema> SchemaCache = new(StringComparer.Ordinal);

    public static string? FindSchemaDirectory(params string[] startingPaths)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string startingPath in startingPaths)
        {
            if (string.IsNullOrWhiteSpace(startingPath)) continue;
            DirectoryInfo? current;
            try
            {
                string full = Path.GetFullPath(startingPath);
                current = new DirectoryInfo(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!);
            }
            catch
            {
                continue;
            }

            while (current != null)
            {
                if (visited.Add(current.FullName))
                {
                    string candidate = Path.Combine(current.FullName, "schemas", "0.1");
                    if (File.Exists(Path.Combine(candidate, "episode.schema.json")) &&
                        File.Exists(Path.Combine(candidate, "manifest.schema.json")))
                        return candidate;
                }
                current = current.Parent;
            }
        }
        return null;
    }

    public static string? SchemaForKind(string kind) => kind switch
    {
        "character_profile" => "character-profile.schema.json",
        "character_post" => "character-post.schema.json",
        "conversation" => "conversation.schema.json",
        "event" => "event.schema.json",
        "call" => "call.schema.json",
        "minigame" => "minigame.schema.json",
        "localization" => "localization.schema.json",
        _ => null
    };

    public static void Validate(string schemaDirectory, string schemaFile, string documentPath,
        string displayPath, Action<string> reportError)
    {
        try
        {
            string schemaPath = Path.Combine(schemaDirectory, schemaFile);
            if (!File.Exists(schemaPath))
            {
                reportError($"JSON Schema file is missing: '{schemaPath}'.");
                return;
            }

            if (!SchemaCache.TryGetValue(schemaPath, out var schema))
            {
                schema = JsonSchema.FromFile(schemaPath);
                SchemaCache.Add(schemaPath, schema);
            }

            using var instance = JsonDocument.Parse(File.ReadAllBytes(documentPath),
                new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow });
            var results = schema.Evaluate(instance.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });

            if (!results.IsValid)
                ReportFailures(results, displayPath, reportError);
        }
        catch (Exception exception)
        {
            reportError($"{displayPath}: JSON Schema evaluation failed safely: {exception.Message}");
        }
    }

    private static void ReportFailures(EvaluationResults result, string displayPath, Action<string> reportError)
    {
        foreach (var error in result.Errors)
            reportError($"{displayPath}: schema violation at '{result.InstanceLocation}' ({error.Key}): {error.Value}");

        foreach (var child in result.Details)
            ReportFailures(child, displayPath, reportError);
    }
}

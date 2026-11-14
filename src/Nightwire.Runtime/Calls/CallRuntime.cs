using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nightwire.Runtime.Domain;

namespace Nightwire.Runtime.Calls
{
    public sealed class CallRuntimeResult
    {
        public bool Success { get; internal set; }
        public string? Error { get; internal set; }
        public bool Completed { get; internal set; }
        public long ElapsedMilliseconds { get; internal set; }
        public List<JsonElement> Cues { get; } = new List<JsonElement>();
        public List<JsonElement> Subtitles { get; } = new List<JsonElement>();
    }

    /// <summary>Deterministic playback timeline for authored prerecorded calls. Media playback remains a host concern.</summary>
    public sealed class CallRuntime
    {
        public CallRuntimeResult Start(LoadedEpisodePackage package, EpisodeProgressState state, string callId)
        {
            if (package == null || state == null) throw new ArgumentNullException(package == null ? nameof(package) : nameof(state));
            if (!package.Calls.ContainsKey(callId)) return Fail("Call is not part of this episode.");
            if (!string.Equals(state.PendingCallId, callId, StringComparison.Ordinal)) return Fail("Call is not the active pending call.");
            if (!state.CallProgress.ContainsKey(callId)) state.CallProgress.Add(callId, new CallProgressState());
            return Success(state.CallProgress[callId]);
        }

        public CallRuntimeResult Advance(LoadedEpisodePackage package, EpisodeProgressState state, string callId, long deltaMilliseconds, string locale)
        {
            if (deltaMilliseconds < 0) return Fail("Elapsed call time cannot be negative.");
            if (!package.Calls.TryGetValue(callId, out var definition)) return Fail("Call is not part of this episode.");
            if (!string.Equals(state.PendingCallId, callId, StringComparison.Ordinal)) return Fail("Call is not the active pending call.");
            if (!state.CallProgress.TryGetValue(callId, out var progress)) return Fail("Call has not been started.");
            if (progress.IsComplete) return Success(progress);

            long duration = RequiredInt64(definition, "duration_ms");
            progress.ElapsedMilliseconds = Math.Min(duration, checked(progress.ElapsedMilliseconds + deltaMilliseconds));
            var result = Success(progress);
            if (definition.TryGetProperty("cues", out var cues))
            {
                foreach (var cue in cues.EnumerateArray())
                {
                    string id = RequiredString(cue, "cue_id");
                    long at = RequiredInt64(cue, "at_ms");
                    if (at <= progress.ElapsedMilliseconds && progress.FiredCueIds.Add(id))
                        result.Cues.Add(cue.Clone());
                }
            }
            if (definition.TryGetProperty("subtitle_tracks", out var tracks))
            {
                JsonElement? selected = null;
                foreach (var track in tracks.EnumerateArray())
                    if (string.Equals(RequiredString(track, "locale"), locale, StringComparison.OrdinalIgnoreCase))
                    { selected = track; break; }
                if (selected.HasValue && selected.Value.TryGetProperty("entries", out var entries))
                    foreach (var entry in entries.EnumerateArray())
                    {
                        long end = RequiredInt64(entry, "end_ms");
                        if (end > progress.ElapsedMilliseconds - deltaMilliseconds && end <= progress.ElapsedMilliseconds)
                            result.Subtitles.Add(entry.Clone());
                    }
            }
            if (progress.ElapsedMilliseconds >= duration)
            {
                progress.IsComplete = true;
                result.Completed = true;
            }
            return result;
        }

        private static string RequiredString(JsonElement value, string name) =>
            value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? string.Empty : throw new InvalidOperationException("Call package is missing " + name + ".");
        private static long RequiredInt64(JsonElement value, string name) =>
            value.TryGetProperty(name, out var item) && item.TryGetInt64(out long number)
                ? number : throw new InvalidOperationException("Call package has invalid " + name + ".");
        private static CallRuntimeResult Success(CallProgressState state) =>
            new CallRuntimeResult { Success = true, Completed = state.IsComplete, ElapsedMilliseconds = state.ElapsedMilliseconds };
        private static CallRuntimeResult Fail(string error) => new CallRuntimeResult { Success = false, Error = error };
    }
}

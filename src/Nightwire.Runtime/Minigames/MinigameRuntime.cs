using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nightwire.Runtime.Domain;

namespace Nightwire.Runtime.Minigames
{
    public sealed class MinigameRuntimeResult
    {
        public bool Success { get; internal set; }
        public string? Error { get; internal set; }
        public bool Solved { get; internal set; }
        public bool AttemptsExhausted { get; internal set; }
        public int AttemptsRemaining { get; internal set; }
        public List<string> CorrectSequence { get; } = new List<string>();
    }

    /// <summary>Deterministic, data-driven evaluator for the three minigame types in episode schema 0.1.</summary>
    public sealed class MinigameRuntime
    {
        public MinigameRuntimeResult Start(LoadedEpisodePackage package, EpisodeProgressState state, string minigameId)
        {
            if (!package.Minigames.ContainsKey(minigameId)) return Fail("Minigame is not part of this episode.");
            if (!string.Equals(state.PendingMinigameId, minigameId, StringComparison.Ordinal)) return Fail("Minigame is not the active pending minigame.");
            if (!state.MinigameProgress.ContainsKey(minigameId)) state.MinigameProgress.Add(minigameId, new MinigameProgressState());
            return Snapshot(package.Minigames[minigameId], state.MinigameProgress[minigameId]);
        }

        public MinigameRuntimeResult AddElapsedTime(LoadedEpisodePackage package, EpisodeProgressState state, string minigameId, double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return Fail("Elapsed time must be a finite non-negative number.");
            if (!TryGet(package, state, minigameId, out var definition, out var progress, out var error)) return Fail(error!);
            progress.ElapsedSeconds += seconds;
            if (definition.TryGetProperty("time_limit_seconds", out var limit) && progress.ElapsedSeconds >= limit.GetDouble())
                return new MinigameRuntimeResult { Success = true, AttemptsRemaining = Remaining(definition, progress), AttemptsExhausted = true };
            return Snapshot(definition, progress);
        }

        public MinigameRuntimeResult SubmitSequence(LoadedEpisodePackage package, EpisodeProgressState state, string minigameId, IReadOnlyList<string> sequence)
        {
            if (!TryGet(package, state, minigameId, out var definition, out var progress, out var error)) return Fail(error!);
            if (RequiredString(definition, "type") != "sequence_reconstruction") return Fail("This minigame does not accept sequence input.");
            if (IsExpired(definition, progress)) return Fail("The minigame time limit has expired.");
            if (progress.AttemptsUsed >= AttemptLimit(definition)) return Fail("No attempts remain.");
            if (sequence == null) return Fail("Sequence input is required.");
            if (!definition.GetProperty("configuration").TryGetProperty("sequence", out var expected)) return Fail("Sequence configuration is missing.");
            if (sequence.Count != expected.GetArrayLength() || sequence.Count > 32) return Fail("Sequence input has an invalid length.");
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in definition.GetProperty("configuration").GetProperty("symbols").EnumerateArray())
                allowed.Add(symbol.GetString() ?? string.Empty);
            if (sequence.Any(symbol => string.IsNullOrEmpty(symbol) || !allowed.Contains(symbol)))
                return Fail("Sequence contains a symbol not allowed by this puzzle.");
            progress.AttemptsUsed++;
            progress.SequenceInput.Clear();
            progress.SequenceInput.AddRange(sequence);
            bool solved = true;
            for (int i = 0; solved && i < sequence.Count; i++) solved = string.Equals(expected[i].GetString(), sequence[i], StringComparison.Ordinal);
            if (solved) progress.IsComplete = true;
            var result = Snapshot(definition, progress);
            result.Solved = solved;
            result.AttemptsExhausted = !solved && result.AttemptsRemaining == 0;
            if (solved) result.CorrectSequence.AddRange(sequence);
            return result;
        }

        public MinigameRuntimeResult SubmitFrequency(LoadedEpisodePackage package, EpisodeProgressState state, string minigameId, double frequency)
        {
            if (!TryGet(package, state, minigameId, out var definition, out var progress, out var error)) return Fail(error!);
            if (RequiredString(definition, "type") != "frequency_tuning") return Fail("This minigame does not accept frequency input.");
            if (IsExpired(definition, progress)) return Fail("The minigame time limit has expired.");
            if (progress.AttemptsUsed >= AttemptLimit(definition)) return Fail("No attempts remain.");
            var config = definition.GetProperty("configuration");
            double min = config.GetProperty("minimum_frequency").GetDouble();
            double max = config.GetProperty("maximum_frequency").GetDouble();
            double target = config.GetProperty("target_frequency").GetDouble();
            double tolerance = config.GetProperty("tolerance").GetDouble();
            if (double.IsNaN(frequency) || double.IsInfinity(frequency) || frequency < min || frequency > max)
                return Fail("Frequency is outside the authored tuning range.");
            progress.AttemptsUsed++;
            var result = Snapshot(definition, progress);
            result.Solved = Math.Abs(frequency - target) <= tolerance;
            if (result.Solved) progress.IsComplete = true;
            result.AttemptsExhausted = !result.Solved && result.AttemptsRemaining == 0;
            return result;
        }

        public MinigameRuntimeResult MatchEvidence(LoadedEpisodePackage package, EpisodeProgressState state, string minigameId, string leftId, string rightId)
        {
            if (!TryGet(package, state, minigameId, out var definition, out var progress, out var error)) return Fail(error!);
            if (RequiredString(definition, "type") != "evidence_matching") return Fail("This minigame does not accept evidence matches.");
            if (IsExpired(definition, progress)) return Fail("The minigame time limit has expired.");
            if (progress.AttemptsUsed >= AttemptLimit(definition)) return Fail("No attempts remain.");
            var config = definition.GetProperty("configuration");
            var items = config.GetProperty("items");
            bool leftExists = false, rightExists = false;
            foreach (var item in items.EnumerateArray())
            {
                string id = RequiredString(item, "item_id");
                leftExists |= id == leftId; rightExists |= id == rightId;
            }
            if (!leftExists || !rightExists || leftId == rightId) return Fail("Evidence item selection is invalid.");
            string key = leftId + "\u001f" + rightId;
            string reverse = rightId + "\u001f" + leftId;
            if (progress.MatchedItemIds.Contains(key) || progress.MatchedItemIds.Contains(reverse))
                return Fail("That evidence pair has already been submitted.");
            progress.AttemptsUsed++;
            bool correct = false;
            foreach (var pair in config.GetProperty("pairs").EnumerateArray())
            {
                string a = RequiredString(pair, "left_item_id");
                string b = RequiredString(pair, "right_item_id");
                if ((a == leftId && b == rightId) || (a == rightId && b == leftId)) { correct = true; break; }
            }
            if (correct) progress.MatchedItemIds.Add(key);
            bool solved = true;
            foreach (var pair in config.GetProperty("pairs").EnumerateArray())
            {
                string a = RequiredString(pair, "left_item_id"), b = RequiredString(pair, "right_item_id");
                if (!progress.MatchedItemIds.Contains(a + "\u001f" + b) && !progress.MatchedItemIds.Contains(b + "\u001f" + a))
                { solved = false; break; }
            }
            if (solved) progress.IsComplete = true;
            var result = Snapshot(definition, progress);
            result.Solved = solved;
            result.AttemptsExhausted = !solved && result.AttemptsRemaining == 0;
            return result;
        }

        private static bool TryGet(LoadedEpisodePackage package, EpisodeProgressState state, string id,
            out JsonElement definition, out MinigameProgressState progress, out string? error)
        {
            definition = default; progress = null!; error = null;
            if (!package.Minigames.TryGetValue(id, out definition)) { error = "Minigame is not part of this episode."; return false; }
            if (state.PendingMinigameId != id) { error = "Minigame is not the active pending minigame."; return false; }
            if (!state.MinigameProgress.TryGetValue(id, out progress!)) { error = "Minigame has not been started."; return false; }
            return true;
        }
        private static bool IsExpired(JsonElement d, MinigameProgressState p) => d.TryGetProperty("time_limit_seconds", out var limit) && p.ElapsedSeconds >= limit.GetDouble();
        private static int AttemptLimit(JsonElement d) => d.TryGetProperty("attempt_limit", out var v) ? v.GetInt32() : int.MaxValue;
        private static int Remaining(JsonElement d, MinigameProgressState p) => Math.Max(0, AttemptLimit(d) - p.AttemptsUsed);
        private static string RequiredString(JsonElement d, string key) => d.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;
        private static MinigameRuntimeResult Snapshot(JsonElement d, MinigameProgressState p) =>
            new MinigameRuntimeResult { Success = true, AttemptsRemaining = Remaining(d, p) };
        private static MinigameRuntimeResult Fail(string error) => new MinigameRuntimeResult { Success = false, Error = error };
    }
}

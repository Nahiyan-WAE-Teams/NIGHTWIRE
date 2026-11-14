using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Progression;

namespace Nightwire.Runtime.Conversations
{
    public sealed class ConversationRuntimeResult
    {
        public bool Success { get; internal set; }
        public bool YieldedAtSafetyLimit { get; internal set; }
        public string? Error { get; internal set; }
        public List<string> NewlyDeliveredMessageIds { get; } = new List<string>();
        public List<StoryExecutionResult> StoryResults { get; } = new List<StoryExecutionResult>();
    }

    /// <summary>
    /// Deterministic, data-driven conversation interpreter. Time advances only when the host calls Advance.
    /// </summary>
    public sealed class ConversationRuntime
    {
        private readonly StoryEventEngine _storyEngine;

        public ConversationRuntime(StoryEventEngine? storyEngine = null)
        {
            _storyEngine = storyEngine ?? new StoryEventEngine();
        }

        public ConversationRuntimeResult BeginConversation(
            LoadedEpisodePackage package, EpisodeProgressState state, string conversationId)
        {
            var result = ValidateContext(package, state);
            if (!result.Success) return result;
            if (!package.Conversations.TryGetValue(conversationId, out var document))
                return Fail(result, $"Conversation '{conversationId}' does not exist.");

            var messages = GetMessages(document);
            if (messages.Count == 0) return Fail(result, $"Conversation '{conversationId}' has no messages.");

            if (!state.ConversationProgress.TryGetValue(conversationId, out var progress))
            {
                progress = new ConversationProgressState();
                state.ConversationProgress.Add(conversationId, progress);
            }

            if (progress.IsComplete)
                return Fail(result, $"Conversation '{conversationId}' is already complete.");

            if (progress.NextMessageId == null && progress.CurrentMessageId == null)
                progress.NextMessageId = RequiredString(messages[0], "message_id");

            state.ActiveConversationId = conversationId;
            result.Success = true;
            return result;
        }

        public ConversationRuntimeResult Advance(
            LoadedEpisodePackage package, EpisodeProgressState state, double elapsedSeconds)
        {
            var result = ValidateContext(package, state);
            if (!result.Success) return result;
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0)
                return Fail(result, "Elapsed time must be a finite, non-negative number.");

            string? conversationId = state.ActiveConversationId;
            if (string.IsNullOrEmpty(conversationId) ||
                !package.Conversations.TryGetValue(conversationId, out var document) ||
                !state.ConversationProgress.TryGetValue(conversationId, out var progress))
                return Fail(result, "There is no active conversation.");

            if (progress.IsComplete || progress.WaitingChoiceMessageId != null)
            {
                result.Success = true;
                return result;
            }

            var messages = GetMessages(document);
            var byId = messages.ToDictionary(x => RequiredString(x, "message_id"), x => x, StringComparer.Ordinal);
            double remaining = elapsedSeconds;
            int safety = messages.Count + 1;

            while (progress.NextMessageId != null && safety-- > 0)
            {
                if (!byId.TryGetValue(progress.NextMessageId, out var message))
                    return Fail(result, $"Next message '{progress.NextMessageId}' is missing.");

                double delay = OptionalNumber(message, "delay_seconds", 0);
                double needed = Math.Max(0, delay - progress.ElapsedDelaySeconds);
                if (remaining < needed)
                {
                    progress.ElapsedDelaySeconds += remaining;
                    remaining = 0;
                    break;
                }

                remaining -= needed;
                progress.ElapsedDelaySeconds = 0;
                string messageId = RequiredString(message, "message_id");
                progress.DeliveredMessageIds.Add(messageId);
                result.NewlyDeliveredMessageIds.Add(messageId);
                progress.CurrentMessageId = messageId;

                int index = messages.FindIndex(x => RequiredString(x, "message_id") == messageId);
                progress.NextMessageId = index + 1 < messages.Count
                    ? RequiredString(messages[index + 1], "message_id")
                    : null;

                if (OptionalString(message, "type") == "choice")
                {
                    progress.WaitingChoiceMessageId = messageId;
                    break;
                }
            }

            if (safety <= 0)
            {
                result.YieldedAtSafetyLimit = true;
                result.Success = true;
                return result;
            }
            if (progress.NextMessageId == null && progress.WaitingChoiceMessageId == null)
                progress.IsComplete = true;

            result.Success = true;
            return result;
        }

        public ConversationRuntimeResult MarkRead(
            LoadedEpisodePackage package, EpisodeProgressState state, string messageId)
        {
            var result = ValidateContext(package, state);
            if (!result.Success) return result;
            if (!TryGetActiveProgress(state, out _, out var progress))
                return Fail(result, "There is no active conversation.");
            if (!progress.DeliveredMessageIds.Contains(messageId))
                return Fail(result, $"Message '{messageId}' has not been delivered.");

            if (!progress.ReadMessageIds.Contains(messageId))
            {
                var story = _storyEngine.ProcessTrigger(package.RuntimeDefinition, state,
                    new StoryTrigger { Type = StoryTriggerType.MessageCompleted, ReferenceId = messageId });
                result.StoryResults.Add(story);
                if (!story.Success) return Fail(result, story.Error ?? "Story event failed.");
                progress.ReadMessageIds.Add(messageId);
            }

            result.Success = true;
            return result;
        }

        public ConversationRuntimeResult SelectChoice(
            LoadedEpisodePackage package, EpisodeProgressState state, string optionId)
        {
            var result = ValidateContext(package, state);
            if (!result.Success) return result;
            if (!TryGetActiveProgress(state, out var conversationId, out var progress) ||
                progress.WaitingChoiceMessageId == null ||
                !package.Conversations.TryGetValue(conversationId, out var document))
                return Fail(result, "No conversation choice is currently awaiting a selection.");

            var messages = GetMessages(document);
            var choice = messages.FirstOrDefault(x =>
                RequiredString(x, "message_id") == progress.WaitingChoiceMessageId);
            if (choice.ValueKind != JsonValueKind.Object ||
                !choice.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
                return Fail(result, "The pending choice message is malformed.");

            JsonElement selected = default;
            bool found = false;
            foreach (var option in options.EnumerateArray())
            {
                if (OptionalString(option, "option_id") == optionId)
                {
                    selected = option;
                    found = true;
                    break;
                }
            }
            if (!found) return Fail(result, $"Choice option '{optionId}' is not available.");

            var story = _storyEngine.ProcessTrigger(package.RuntimeDefinition, state,
                new StoryTrigger { Type = StoryTriggerType.ChoiceSelected, ReferenceId = optionId });
            result.StoryResults.Add(story);
            if (!story.Success) return Fail(result, story.Error ?? "Story event failed.");

            progress.SelectedOptions[progress.WaitingChoiceMessageId] = optionId;
            if (selected.TryGetProperty("set_flags", out var flags) && flags.ValueKind == JsonValueKind.Array)
            {
                foreach (var flag in flags.EnumerateArray())
                    if (flag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(flag.GetString()))
                        state.StoryFlags[flag.GetString()!] = StoryFlagValue.FromBoolean(true);
            }

            if (selected.TryGetProperty("relationship_delta", out var delta) &&
                delta.ValueKind == JsonValueKind.Number && delta.TryGetInt32(out int relationshipDelta))
            {
                string senderId = RequiredString(choice, "sender_id");
                state.RelationshipScores.TryGetValue(senderId, out long currentScore);
                state.RelationshipScores[senderId] = currentScore + relationshipDelta;
            }

            if (selected.TryGetProperty("next_message_id", out var next))
                progress.NextMessageId = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
            // If next_message_id is omitted, continue to the next message in authored order.

            progress.WaitingChoiceMessageId = null;
            if (progress.NextMessageId == null) progress.IsComplete = true;

            result.Success = true;
            return result;
        }

        private static ConversationRuntimeResult ValidateContext(
            LoadedEpisodePackage package, EpisodeProgressState state)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new ConversationRuntimeResult();
            if (!string.Equals(package.Descriptor.EpisodeId, state.EpisodeId, StringComparison.Ordinal))
                return Fail(result, "Progress state belongs to a different episode.");
            result.Success = true;
            return result;
        }

        private static bool TryGetActiveProgress(
            EpisodeProgressState state, out string conversationId, out ConversationProgressState progress)
        {
            conversationId = state.ActiveConversationId ?? string.Empty;
            if (conversationId.Length > 0 && state.ConversationProgress.TryGetValue(conversationId, out progress!))
                return true;
            progress = null!;
            return false;
        }

        private static List<JsonElement> GetMessages(JsonElement document)
        {
            if (!document.TryGetProperty("messages", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Conversation has no messages array.");
            return values.EnumerateArray().Select(x => x).ToList();
        }

        private static string RequiredString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException($"Required conversation property '{property}' is missing.");
            return value.GetString()!;
        }

        private static string? OptionalString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        }

        private static double OptionalNumber(JsonElement element, string property, double fallback)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble() : fallback;
        }

        private static ConversationRuntimeResult Fail(ConversationRuntimeResult result, string error)
        {
            result.Success = false;
            result.Error = error;
            return result;
        }
    }
}

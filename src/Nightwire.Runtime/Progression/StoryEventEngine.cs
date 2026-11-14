using System;
using System.Collections.Generic;
using Nightwire.Runtime.Domain;

namespace Nightwire.Runtime.Progression
{
    public sealed class StoryTrigger
    {
        public StoryTriggerType Type { get; set; }
        public string? ReferenceId { get; set; }
    }

    public sealed class StoryExecutionResult
    {
        public bool Success { get; internal set; }
        public string? Error { get; internal set; }
        public List<string> ExecutedEventIds { get; } = new List<string>();
        public List<RuntimeCommand> Commands { get; } = new List<RuntimeCommand>();
    }

    public sealed class RuntimeCommand
    {
        public StoryActionType Type { get; set; }
        public string? TargetId { get; set; }
        public string? TitleKey { get; set; }
        public string? BodyKey { get; set; }
        public string? Flag { get; set; }
        public StoryFlagValue FlagValue { get; set; } = StoryFlagValue.FromBoolean(true);
    }

    /// <summary>
    /// Deterministic interpreter for the episode event allowlist. Contains no Unity dependencies.
    /// </summary>
    public sealed class StoryEventEngine
    {
        public StoryExecutionResult ProcessTrigger(EpisodeRuntimeDefinition episode, EpisodeProgressState state, StoryTrigger trigger)
        {
            if (episode == null) throw new ArgumentNullException(nameof(episode));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));

            var result = new StoryExecutionResult();
            if (!string.Equals(state.EpisodeId, episode.EpisodeId, StringComparison.Ordinal))
            {
                result.Error = "Progress state belongs to a different episode.";
                return result;
            }

            var matchedEvents = new List<StoryEventDefinition>();
            foreach (var storyEvent in episode.Events)
            {
                if (!Matches(storyEvent, trigger)) continue;
                if (storyEvent.OneShot && state.CompletedEventIds.Contains(storyEvent.EventId)) continue;
                var validationError = ValidateActions(episode, storyEvent);
                if (validationError != null)
                {
                    result.Error = $"Event '{storyEvent.EventId}': {validationError}";
                    return result;
                }
                matchedEvents.Add(storyEvent);
            }

            // Validate the entire trigger batch before applying any side effects.
            foreach (var storyEvent in matchedEvents)
            {
                ApplyActions(storyEvent, state, result);
                state.CompletedEventIds.Add(storyEvent.EventId);
                result.ExecutedEventIds.Add(storyEvent.EventId);
            }

            result.Success = true;
            return result;
        }

        private static bool Matches(StoryEventDefinition storyEvent, StoryTrigger trigger)
        {
            return storyEvent.TriggerType == trigger.Type &&
                   string.Equals(storyEvent.TriggerReferenceId, trigger.ReferenceId, StringComparison.Ordinal);
        }

        private static string? ValidateActions(EpisodeRuntimeDefinition episode, StoryEventDefinition storyEvent)
        {
            foreach (var action in storyEvent.Actions)
            {
                switch (action.Type)
                {
                    case StoryActionType.OpenConversation:
                        if (string.IsNullOrWhiteSpace(action.TargetId) || !episode.ConversationIds.Contains(action.TargetId))
                            return $"conversation target '{action.TargetId}' does not resolve.";
                        break;
                    case StoryActionType.ShowNotification:
                        if (string.IsNullOrWhiteSpace(action.TitleKey) || string.IsNullOrWhiteSpace(action.BodyKey))
                            return "notification requires title_key and body_key localization keys.";
                        break;
                    case StoryActionType.UnlockAsset:
                        if (string.IsNullOrWhiteSpace(action.TargetId) || !episode.AssetIds.Contains(action.TargetId))
                            return $"asset target '{action.TargetId}' does not resolve.";
                        break;
                    case StoryActionType.SetStoryFlag:
                        if (string.IsNullOrWhiteSpace(action.Flag) || action.FlagValue == null)
                            return "set_story_flag requires a flag name and value.";
                        break;
                    case StoryActionType.StartCall:
                        if (string.IsNullOrWhiteSpace(action.TargetId) || !episode.CallIds.Contains(action.TargetId))
                            return $"call target '{action.TargetId}' does not resolve.";
                        break;
                    case StoryActionType.StartMinigame:
                        if (string.IsNullOrWhiteSpace(action.TargetId) || !episode.MinigameIds.Contains(action.TargetId))
                            return $"minigame target '{action.TargetId}' does not resolve.";
                        break;
                    default:
                        return $"unsupported action type '{action.Type}'.";
                }
            }
            return null;
        }

        private static void ApplyActions(StoryEventDefinition storyEvent, EpisodeProgressState state, StoryExecutionResult result)
        {
            foreach (var action in storyEvent.Actions)
            {
                result.Commands.Add(new RuntimeCommand
                {
                    Type = action.Type,
                    TargetId = action.TargetId,
                    TitleKey = action.TitleKey,
                    BodyKey = action.BodyKey,
                    Flag = action.Flag,
                    FlagValue = action.FlagValue
                });

                switch (action.Type)
                {
                    case StoryActionType.OpenConversation:
                        state.ActiveConversationId = action.TargetId;
                        break;
                    case StoryActionType.ShowNotification:
                        state.Notifications.Add(new RuntimeNotification
                        {
                            NotificationId = $"{storyEvent.EventId}.notification.{state.Notifications.Count + 1}",
                            TitleKey = action.TitleKey!,
                            BodyKey = action.BodyKey!
                        });
                        break;
                    case StoryActionType.UnlockAsset:
                        state.UnlockedAssetIds.Add(action.TargetId!);
                        break;
                    case StoryActionType.SetStoryFlag:
                        state.StoryFlags[action.Flag!] = action.FlagValue;
                        break;
                    case StoryActionType.StartCall:
                        state.PendingCallId = action.TargetId;
                        break;
                    case StoryActionType.StartMinigame:
                        state.PendingMinigameId = action.TargetId;
                        break;
                }
            }
        }
    }
}

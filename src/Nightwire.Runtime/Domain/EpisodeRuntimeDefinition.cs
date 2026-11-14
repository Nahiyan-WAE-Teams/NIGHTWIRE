using System;
using System.Collections.Generic;

namespace Nightwire.Runtime.Domain
{
    public sealed class EpisodeRuntimeDefinition
    {
        public string EpisodeId { get; set; } = string.Empty;
        public string EpisodeVersion { get; set; } = string.Empty;
        public string EntryEventId { get; set; } = string.Empty;

        public HashSet<string> ConversationIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> CallIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> MinigameIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> AssetIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public List<StoryEventDefinition> Events { get; } = new List<StoryEventDefinition>();
    }

    public sealed class StoryEventDefinition
    {
        public string EventId { get; set; } = string.Empty;
        public StoryTriggerType TriggerType { get; set; }
        public string? TriggerReferenceId { get; set; }
        public bool OneShot { get; set; } = true;
        public List<StoryActionDefinition> Actions { get; } = new List<StoryActionDefinition>();
    }

    public sealed class StoryActionDefinition
    {
        public StoryActionType Type { get; set; }
        public string? TargetId { get; set; }
        public string? Flag { get; set; }
        public bool Value { get; set; } = true;
        public string? TextKey { get; set; }
    }

    public enum StoryTriggerType
    {
        EpisodeStarted,
        MessageCompleted,
        ChoiceSelected,
        CallEnded,
        MinigameCompleted
    }

    public enum StoryActionType
    {
        OpenConversation,
        ShowNotification,
        UnlockAsset,
        SetStoryFlag,
        StartCall,
        StartMinigame
    }
}

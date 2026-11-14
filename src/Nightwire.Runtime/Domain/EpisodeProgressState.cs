using System;
using System.Collections.Generic;

namespace Nightwire.Runtime.Domain
{
    /// <summary>
    /// Mutable player-owned progress. Never serialize this into the authored episode package.
    /// </summary>
    public sealed class EpisodeProgressState
    {
        public string EpisodeId { get; set; } = string.Empty;
        public string EpisodeVersion { get; set; } = string.Empty;
        public string? ActiveConversationId { get; set; }
        public string? PendingCallId { get; set; }
        public string? PendingMinigameId { get; set; }

        public Dictionary<string, ConversationProgressState> ConversationProgress { get; } = new Dictionary<string, ConversationProgressState>(StringComparer.Ordinal);
        public Dictionary<string, CallProgressState> CallProgress { get; } = new Dictionary<string, CallProgressState>(StringComparer.Ordinal);
        public Dictionary<string, MinigameProgressState> MinigameProgress { get; } = new Dictionary<string, MinigameProgressState>(StringComparer.Ordinal);

        public HashSet<string> CompletedEventIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> UnlockedAssetIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, StoryFlagValue> StoryFlags { get; } = new Dictionary<string, StoryFlagValue>(StringComparer.Ordinal);
        public Dictionary<string, long> RelationshipScores { get; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public List<RuntimeNotification> Notifications { get; } = new List<RuntimeNotification>();

        public void ResetTransientActions()
        {
            PendingCallId = null;
            PendingMinigameId = null;
        }
    }

    public sealed class RuntimeNotification
    {
        public string NotificationId { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string BodyKey { get; set; } = string.Empty;
        public string? DestinationId { get; set; }
        public bool IsRead { get; set; }
    }
}

namespace Nightwire.Runtime.Domain
{
    public sealed class CallProgressState
    {
        public long ElapsedMilliseconds { get; set; }
        public bool IsComplete { get; set; }
        public HashSet<string> FiredCueIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> FiredSubtitleIds { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class MinigameProgressState
    {
        public int AttemptsUsed { get; set; }
        public bool IsComplete { get; set; }
        public double ElapsedSeconds { get; set; }
        public List<string> SequenceInput { get; } = new List<string>();
        public HashSet<string> MatchedItemIds { get; } = new HashSet<string>(StringComparer.Ordinal);
    }
}

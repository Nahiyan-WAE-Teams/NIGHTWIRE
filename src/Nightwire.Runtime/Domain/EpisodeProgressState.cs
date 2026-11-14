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

        public HashSet<string> CompletedEventIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> UnlockedAssetIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, bool> StoryFlags { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);
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
        public string TextKey { get; set; } = string.Empty;
        public string? DestinationId { get; set; }
        public bool IsRead { get; set; }
    }
}

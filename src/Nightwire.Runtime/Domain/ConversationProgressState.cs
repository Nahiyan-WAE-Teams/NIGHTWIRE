using System;
using System.Collections.Generic;

namespace Nightwire.Runtime.Domain
{
    /// <summary>
    /// Player-owned state for one authored conversation; safe to serialize with episode save data.
    /// </summary>
    public sealed class ConversationProgressState
    {
        public string? CurrentMessageId { get; set; }
        public string? NextMessageId { get; set; }
        public string? WaitingChoiceMessageId { get; set; }
        public double ElapsedDelaySeconds { get; set; }
        public bool IsComplete { get; set; }
        public HashSet<string> DeliveredMessageIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> ReadMessageIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, string> SelectedOptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

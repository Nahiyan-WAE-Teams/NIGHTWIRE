using Nightwire.Runtime.Domain;

namespace Nightwire.Runtime.Saves
{
    /// <summary>
    /// Explicit, deterministic migration for player progress between two authored episode versions.
    /// Implementations must only transform player-owned state and must not access the network.
    /// </summary>
    public interface IEpisodeProgressMigration
    {
        string EpisodeId { get; }
        string FromEpisodeVersion { get; }
        string ToEpisodeVersion { get; }
        void Migrate(EpisodeProgressState state);
    }
}

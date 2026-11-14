using System;
using System.IO;
using Nightwire.Runtime.Installation;
using Nightwire.Runtime.Loading;
using Nightwire.Runtime.Saves;
using Nightwire.Runtime.Sessions;

namespace Nightwire.Unity
{
    public sealed class EpisodeSessionOpenResult
    {
        public bool Success { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
        public RuntimeSessionResult SessionResult { get; internal set; }
    }

    public sealed class EpisodeSessionCloseResult
    {
        public bool Success { get; internal set; }
        public string ErrorCode { get; internal set; }
        public string Error { get; internal set; }
    }

    /// <summary>
    /// Owns one gameplay session and its exclusive per-episode access lease.
    /// The lease remains held until progress has been saved and the coordinator closed.
    /// Call all methods from the Unity main thread.
    /// </summary>
    public sealed class EpisodeSessionHost
    {
        private readonly EpisodeAccessCoordinator _access;
        private readonly SaveStore _saveStore;
        private readonly EpisodePackageLoader _loader;
        private RuntimeSessionCoordinator? _session;
        private IDisposable? _sessionLease;

        public RuntimeSessionCoordinator? ActiveSession => _session;
        public string? ActiveEpisodeId => _session?.Package?.Descriptor.EpisodeId;

        public EpisodeSessionHost(EpisodeAccessCoordinator access, SaveStore saveStore, string runtimeVersion)
        {
            _access = access ?? throw new ArgumentNullException(nameof(access));
            _saveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));
            if (string.IsNullOrWhiteSpace(runtimeVersion))
                throw new ArgumentException("Runtime version is required.", nameof(runtimeVersion));
            _loader = new EpisodePackageLoader(new Nightwire.Runtime.Domain.EpisodePackageLoaderOptions
            {
                RuntimeVersion = runtimeVersion
            });
        }

        public EpisodeSessionOpenResult OpenEpisode(string episodeId, string installDirectory)
        {
            if (_session != null)
                return Failure("session_already_active", "Close the active episode before opening another.");
            if (string.IsNullOrWhiteSpace(episodeId) || string.IsNullOrWhiteSpace(installDirectory))
                return Failure("episode_location_invalid", "Episode identity and install directory are required.");

            IDisposable? lease = null;
            if (!_access.TryAcquireSessionLease(episodeId, out lease) || lease == null)
                return Failure("episode_install_active", "This episode is currently being installed or updated.");

            Nightwire.Runtime.Loading.LoadedEpisodePackage package;
            try
            {
                package = _loader.Load(installDirectory);
            }
            catch (Exception exception)
            {
                lease.Dispose();
                return Failure("package_load_failed", exception.Message);
            }

            if (!string.Equals(package.Descriptor.EpisodeId, episodeId, StringComparison.Ordinal))
            {
                lease.Dispose();
                return Failure("episode_identity_mismatch", "Installed package identity does not match the requested episode.");
            }

            var coordinator = new RuntimeSessionCoordinator(_saveStore, _loader);
            try
            {
                string savePath = _saveStore.GetSavePath(episodeId);
                bool hasSave = File.Exists(savePath) || File.Exists(savePath + ".bak");
                RuntimeSessionResult result = hasSave
                    ? coordinator.ResumeSession(package)
                    : coordinator.StartNewSession(package);

                if (!result.Success)
                {
                    coordinator.CloseSession();
                    lease.Dispose();
                    return new EpisodeSessionOpenResult
                    {
                        Success = false,
                        ErrorCode = result.ErrorCode,
                        Error = result.Error,
                        SessionResult = result
                    };
                }

                coordinator.SessionClosed += RuntimeSessionClosed;
                _session = coordinator;
                _sessionLease = lease;
                return new EpisodeSessionOpenResult { Success = true, SessionResult = result };
            }
            catch
            {
                coordinator.CloseSession();
                lease.Dispose();
                throw;
            }
        }

        public RuntimeSessionResult? SaveCheckpoint()
        {
            var session = _session;
            if (session == null) return null;
            RuntimeSessionResult result;
            try
            {
                result = session.SaveNow();
            }
            catch
            {
                ReleaseSession();
                throw;
            }

            // RuntimeSessionCoordinator notifies this host synchronously when a persistence
            // failure closes the session, releasing the lease before control returns here.
            if (!result.Success && !session.IsActive)
                ReleaseSession();
            return result;
        }

        public EpisodeSessionCloseResult SaveAndClose()
        {
            if (_session == null)
                return new EpisodeSessionCloseResult
                {
                    Success = false,
                    ErrorCode = "session_not_active",
                    Error = "There is no active episode session."
                };

            RuntimeSessionResult result;
            try
            {
                result = _session.SaveNow();
            }
            finally
            {
                ReleaseSession();
            }
            return new EpisodeSessionCloseResult
            {
                Success = result.Success,
                ErrorCode = result.ErrorCode,
                Error = result.Error
            };
        }

        private void RuntimeSessionClosed()
        {
            var session = _session;
            if (session != null)
                session.SessionClosed -= RuntimeSessionClosed;
            _session = null;

            var lease = _sessionLease;
            _sessionLease = null;
            lease?.Dispose();
        }

        private void ReleaseSession()
        {
            var session = _session;
            _session = null;
            if (session != null)
            {
                session.SessionClosed -= RuntimeSessionClosed;
                session.CloseSession();
            }

            var lease = _sessionLease;
            _sessionLease = null;
            lease?.Dispose();
        }

        private static EpisodeSessionOpenResult Failure(string code, string message) =>
            new EpisodeSessionOpenResult { Success = false, ErrorCode = code, Error = message };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nightwire.Runtime.Installation
{
    /// <summary>
    /// Shared per-episode exclusion gate. The host must acquire a session lease before opening
    /// RuntimeSessionCoordinator and retain it until CloseSession has completed.
    /// </summary>
    public sealed class EpisodeAccessCoordinator : IEpisodeInstallLeaseProvider
    {
        private sealed class AccessState
        {
            public bool SessionActive;
            public bool InstallActive;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, AccessState> _states =
            new Dictionary<string, AccessState>(StringComparer.Ordinal);

        public bool TryAcquireSessionLease(string episodeId, out IDisposable? lease)
        {
            return TryAcquire(episodeId, session: true, out lease);
        }

        public bool TryAcquireInstallLease(string episodeId, out IDisposable? lease)
        {
            return TryAcquire(episodeId, session: false, out lease);
        }

        private bool TryAcquire(string episodeId, bool session, out IDisposable? lease)
        {
            lease = null;
            if (string.IsNullOrWhiteSpace(episodeId)) return false;
            lock (_gate)
            {
                if (!_states.TryGetValue(episodeId, out var state))
                {
                    state = new AccessState();
                    _states.Add(episodeId, state);
                }

                if (state.SessionActive || state.InstallActive) return false;
                if (session) state.SessionActive = true;
                else state.InstallActive = true;
                lease = new AccessLease(this, episodeId, session);
                return true;
            }
        }

        private void Release(string episodeId, bool session)
        {
            lock (_gate)
            {
                if (!_states.TryGetValue(episodeId, out var state)) return;
                if (session) state.SessionActive = false;
                else state.InstallActive = false;
                if (!state.SessionActive && !state.InstallActive) _states.Remove(episodeId);
            }
        }

        private sealed class AccessLease : IDisposable
        {
            private EpisodeAccessCoordinator? _owner;
            private readonly string _episodeId;
            private readonly bool _session;

            public AccessLease(EpisodeAccessCoordinator owner, string episodeId, bool session)
            {
                _owner = owner;
                _episodeId = episodeId;
                _session = session;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.Release(_episodeId, _session);
            }
        }
    }

    public sealed class EpisodeLibraryRow
    {
        public string EpisodeId { get; internal set; } = string.Empty;
        public string? InstalledVersion { get; internal set; }
        public string? AvailableVersion { get; internal set; }
        public string? MinimumRuntimeVersion { get; internal set; }
        public EpisodeLibraryStatus Status { get; internal set; }
        public bool IsBusy { get; internal set; }
        public long BytesReceived { get; internal set; }
        public long? TotalBytes { get; internal set; }
        public double? ProgressFraction => TotalBytes.HasValue && TotalBytes.Value > 0
            ? Math.Max(0, Math.Min(1, (double)BytesReceived / TotalBytes.Value)) : (double?)null;
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
    }

    public sealed class EpisodeLibrarySnapshot
    {
        public bool IsRefreshing { get; internal set; }
        public bool IsOperationBusy { get; internal set; }
        public bool CatalogueAvailable { get; internal set; }
        public string? CatalogueError { get; internal set; }
        public IReadOnlyList<EpisodeLibraryRow> Episodes { get; internal set; } =
            Array.Empty<EpisodeLibraryRow>();
    }

    /// <summary>
    /// Presentation-facing state adapter for Unity UI Toolkit/uGUI or another host.
    /// Construct and subscribe on the Unity main thread so Progress callbacks and Changed events
    /// are delivered on that synchronization context.
    /// </summary>
    public sealed class EpisodeLibraryPresentation : IDisposable
    {
        private readonly EpisodeReleaseManager _manager;
        private readonly object _gate = new object();
        private readonly Dictionary<string, EpisodeLibraryRow> _rows =
            new Dictionary<string, EpisodeLibraryRow>(StringComparer.Ordinal);
        private CancellationTokenSource? _operationCancellation;
        private CancellationTokenSource? _refreshCancellation;
        private bool _refreshing;
        private bool _disposed;
        private string? _catalogueError;
        private bool _catalogueAvailable;

        public event Action<EpisodeLibrarySnapshot>? Changed;

        public EpisodeLibraryPresentation(EpisodeReleaseManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _catalogueAvailable = manager.CurrentCatalogue != null;
            RebuildRows();
        }

        public EpisodeLibrarySnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    var rows = _rows.Values.OrderBy(x => x.EpisodeId, StringComparer.Ordinal)
                        .Select(CloneRow).ToArray();
                    return new EpisodeLibrarySnapshot
                    {
                        IsRefreshing = _refreshing,
                        IsOperationBusy = _operationCancellation != null,
                        CatalogueAvailable = _catalogueAvailable,
                        CatalogueError = _catalogueError,
                        Episodes = rows
                    };
                }
            }
        }

        public async Task<ReleaseCatalogueResult> RefreshAsync(DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            CancellationTokenSource refreshSource;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(EpisodeLibraryPresentation));
                if (_refreshCancellation != null)
                    return new ReleaseCatalogueResult
                    {
                        Success = false,
                        ErrorCode = "library_refresh_in_progress",
                        Error = "A library refresh is already running."
                    };
                refreshSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _refreshCancellation = refreshSource;
                _refreshing = true;
            }
            Publish();

            try
            {
                ReleaseCatalogueResult result = await _manager.RefreshCatalogueAsync(
                    utcNow, refreshSource.Token);
                bool publish;
                lock (_gate)
                {
                    publish = !_disposed;
                    if (publish)
                    {
                        _catalogueAvailable = result.Success || _manager.CurrentCatalogue != null;
                        _catalogueError = result.Success ? null : result.Error;
                    }
                }
                if (publish)
                {
                    RebuildRows();
                    Publish();
                }
                return result;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_refreshCancellation, refreshSource))
                        _refreshCancellation = null;
                    if (!_disposed) _refreshing = false;
                }
                refreshSource.Dispose();
                Publish();
            }
        }

        public async Task<EpisodeManagerOperationResult> InstallOrUpdateAsync(string episodeId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(episodeId))
                throw new ArgumentException("Episode ID is required.", nameof(episodeId));

            CancellationTokenSource operationSource;
            CancellationToken operationToken;
            lock (_gate)
            {
                if (_disposed)
                    return FailedOperation("library_disposed", "Episode library presentation has been disposed.", episodeId);
                if (_operationCancellation != null)
                    return FailedOperation("library_operation_in_progress", "Another library operation is already running.", episodeId);
                operationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _operationCancellation = operationSource;
                operationToken = operationSource.Token;
                if (!_rows.TryGetValue(episodeId, out var row))
                {
                    row = new EpisodeLibraryRow { EpisodeId = episodeId, Status = EpisodeLibraryStatus.AvailableToDownload };
                    _rows.Add(episodeId, row);
                }
                row.IsBusy = true;
                row.Error = null;
                row.ErrorCode = null;
                row.BytesReceived = 0;
                row.TotalBytes = null;
            }
            Publish();

            long lastPublishedBytes = 0;
            var progress = new Progress<EpisodeDownloadProgress>(value =>
            {
                bool publish = false;
                lock (_gate)
                {
                    if (_rows.TryGetValue(episodeId, out var row))
                    {
                        row.BytesReceived = value.BytesReceived;
                        row.TotalBytes = value.TotalBytes;
                        long step = value.TotalBytes.HasValue
                            ? Math.Max(64 * 1024L, value.TotalBytes.Value / 100L)
                            : 256 * 1024L;
                        if (value.BytesReceived - lastPublishedBytes >= step ||
                            (value.TotalBytes.HasValue && value.BytesReceived >= value.TotalBytes.Value))
                        {
                            lastPublishedBytes = value.BytesReceived;
                            publish = true;
                        }
                    }
                }
                if (publish) Publish();
            });

            EpisodeManagerOperationResult result;
            try
            {
                result = await _manager.InstallOrUpdateAsync(episodeId, progress, operationToken);
                lock (_gate)
                {
                    if (_rows.TryGetValue(episodeId, out var row))
                    {
                        row.IsBusy = false;
                        row.ErrorCode = result.Success ? null : result.ErrorCode;
                        row.Error = result.Success ? null : result.Error;
                    }
                }
                RebuildRows();
                Publish();
                return result;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_operationCancellation, operationSource))
                        _operationCancellation = null;
                    if (_rows.TryGetValue(episodeId, out var row)) row.IsBusy = false;
                }
                operationSource.Dispose();
                Publish();
            }
        }

        public void CancelCurrentOperation()
        {
            CancellationTokenSource? operation;
            lock (_gate) operation = _operationCancellation;
            try { operation?.Cancel(); }
            catch (AggregateException) { /* Cancellation must not interrupt presentation teardown. */ }
            catch (ObjectDisposedException) { /* The operation may have completed after the lock was released. */ }
        }

        public void ReloadInstalledLibrary()
        {
            ThrowIfDisposed();
            RebuildRows();
            Publish();
        }

        private void RebuildRows()
        {
            var library = _manager.GetLibrary();
            lock (_gate)
            {
                var next = new Dictionary<string, EpisodeLibraryRow>(StringComparer.Ordinal);
                foreach (var entry in library)
                {
                    _rows.TryGetValue(entry.EpisodeId, out var previous);
                    next[entry.EpisodeId] = new EpisodeLibraryRow
                    {
                        EpisodeId = entry.EpisodeId,
                        InstalledVersion = entry.InstalledVersion,
                        AvailableVersion = entry.AvailableVersion,
                        MinimumRuntimeVersion = entry.MinimumRuntimeVersion,
                        Status = entry.Status,
                        IsBusy = previous?.IsBusy ?? false,
                        BytesReceived = previous?.BytesReceived ?? 0,
                        TotalBytes = previous?.TotalBytes,
                        ErrorCode = previous?.ErrorCode,
                        Error = previous?.Error
                    };
                }
                _rows.Clear();
                foreach (var pair in next) _rows.Add(pair.Key, pair.Value);
            }
        }

        private void Publish()
        {
            lock (_gate)
            {
                if (_disposed) return;
            }
            var handlers = Changed;
            if (handlers == null) return;
            var snapshot = Snapshot;
            foreach (Action<EpisodeLibrarySnapshot> handler in handlers.GetInvocationList())
            {
                try { handler(snapshot); }
                catch { /* Presentation subscribers must not interrupt download or install work. */ }
            }
        }

        private static EpisodeLibraryRow CloneRow(EpisodeLibraryRow row) => new EpisodeLibraryRow
        {
            EpisodeId = row.EpisodeId,
            InstalledVersion = row.InstalledVersion,
            AvailableVersion = row.AvailableVersion,
            MinimumRuntimeVersion = row.MinimumRuntimeVersion,
            Status = row.Status,
            IsBusy = row.IsBusy,
            BytesReceived = row.BytesReceived,
            TotalBytes = row.TotalBytes,
            ErrorCode = row.ErrorCode,
            Error = row.Error
        };

        private static EpisodeManagerOperationResult FailedOperation(string code, string message, string episodeId) =>
            new EpisodeManagerOperationResult
            {
                Success = false, ErrorCode = code, Error = message, EpisodeId = episodeId
            };

        private void ThrowIfDisposed()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(EpisodeLibraryPresentation));
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? operation;
            CancellationTokenSource? refresh;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                operation = _operationCancellation;
                refresh = _refreshCancellation;
            }

            // Cancel outside the presentation lock: cancellation callbacks may resume
            // asynchronous work that needs to acquire this same lock during cleanup.
            try { refresh?.Cancel(); }
            catch (AggregateException) { /* All cancellation callbacks are still attempted. */ }
            catch (ObjectDisposedException) { /* The refresh may have completed after the lock was released. */ }
            try { operation?.Cancel(); }
            catch (AggregateException) { /* All cancellation callbacks are still attempted. */ }
            catch (ObjectDisposedException) { /* The operation may have completed after the lock was released. */ }
        }
    }
}

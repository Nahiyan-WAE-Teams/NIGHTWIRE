using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nightwire.Runtime.Loading;

namespace Nightwire.Runtime.Installation
{
    /// <summary>Host boundary for retrieving a catalogue envelope. Implementations must enforce HTTPS.</summary>
    public interface IReleaseCatalogueTransport
    {
        Task<byte[]> FetchAsync(Uri catalogueUri, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Atomically prevents a new gameplay session from opening while an episode install/update is in progress.
    /// The host must coordinate this lease with RuntimeSessionCoordinator session start/close operations.
    /// </summary>
    public interface IEpisodeInstallLeaseProvider
    {
        bool TryAcquireInstallLease(string episodeId, out IDisposable? lease);
    }

    public sealed class HttpReleaseCatalogueTransport : IReleaseCatalogueTransport, IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _ownsClient;
        private readonly int _maximumBytes;

        public HttpReleaseCatalogueTransport(HttpClient? client = null, int maximumBytes = 2 * 1024 * 1024)
        {
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            _maximumBytes = maximumBytes;
            _client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _ownsClient = client == null;
        }

        public async Task<byte[]> FetchAsync(Uri catalogueUri, CancellationToken cancellationToken)
        {
            if (catalogueUri == null || !catalogueUri.IsAbsoluteUri ||
                catalogueUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(catalogueUri.UserInfo))
                throw new InvalidOperationException("Release catalogue URL must be absolute HTTPS without credentials.");

            using var request = new HttpRequestMessage(HttpMethod.Get, catalogueUri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            Uri? responseUri = response.RequestMessage?.RequestUri;
            if (responseUri == null || !responseUri.IsAbsoluteUri ||
                responseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(responseUri.UserInfo) ||
                !Uri.Equals(responseUri, catalogueUri))
                throw new InvalidOperationException("Release catalogue redirects are not allowed.");
            int status = (int)response.StatusCode;
            if (status >= 300 && status <= 399)
                throw new InvalidOperationException("Release catalogue redirects are not allowed.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength.HasValue &&
                response.Content.Headers.ContentLength.Value > _maximumBytes)
                throw new InvalidDataException("Release catalogue exceeds the configured size limit.");

            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > _maximumBytes)
                    throw new InvalidDataException("Release catalogue exceeds the configured size limit.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        public void Dispose() { if (_ownsClient) _client.Dispose(); }
    }

    public enum EpisodeLibraryStatus
    {
        AvailableToDownload,
        InstalledOffline,
        UpdateAvailable,
        InstalledButNotInCatalogue,
        InstalledPackageInvalid
    }

    public sealed class EpisodeLibraryEntry
    {
        public string EpisodeId { get; internal set; } = string.Empty;
        public string? AvailableVersion { get; internal set; }
        public string? InstalledVersion { get; internal set; }
        public string? MinimumRuntimeVersion { get; internal set; }
        public EpisodeLibraryStatus Status { get; internal set; }
        public string? InstallDirectory { get; internal set; }
    }

    public sealed class EpisodeManagerOperationResult
    {
        public bool Success { get; internal set; }
        public string? EpisodeId { get; internal set; }
        public string? EpisodeVersion { get; internal set; }
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
        public EpisodeDownloadResult? Download { get; internal set; }
        public EpisodeInstallResult? Install { get; internal set; }
    }

    /// <summary>
    /// Orchestrates signed catalogue acceptance, release downloads, verified installs, and offline library discovery.
    /// The host supplies protected sequence storage, trusted keys, transport, and session/install lease coordination.
    /// </summary>
    public sealed class EpisodeReleaseManager : IDisposable
    {
        private readonly Uri _catalogueUri;
        private readonly Version _runtimeVersion;
        private readonly string _installRoot;
        private readonly string _downloadRoot;
        private readonly IReleaseCatalogueTransport _transport;
        private readonly ReleaseCatalogueAcceptanceService _acceptance;
        private readonly EpisodePackageDownloader _downloader;
        private readonly EpisodePackageInstaller _installer;
        private readonly EpisodePackageLoader _loader;
        private readonly IEpisodeInstallLeaseProvider _leaseProvider;
        private readonly bool _ownsDownloader;
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly object _gate = new object();
        private readonly HashSet<string> _operations = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object SharedEpisodeOperationsGate = new object();
        private static readonly HashSet<string> SharedEpisodeOperations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private VerifiedReleaseCatalogue? _catalogue;
        private EpisodeInstallRecoveryResult? _startupRecovery;
        private bool _disposed;
        private bool _downloaderDisposed;
        private int _activeRefreshes;
        private bool _lifetimeResourcesDisposed;
        private bool _shutdownSignalCompleted;

        public VerifiedReleaseCatalogue? CurrentCatalogue
        {
            get { lock (_gate) return _catalogue; }
        }

        public EpisodeReleaseManager(Uri catalogueUri, string installRoot, string downloadRoot,
            IReleaseCatalogueTransport transport, ReleaseCatalogueAcceptanceService acceptance,
            IEpisodeInstallLeaseProvider leaseProvider, string runtimeVersion, EpisodePackageDownloader? downloader = null,
            EpisodePackageInstaller? installer = null, EpisodePackageLoader? loader = null)
        {
            if (catalogueUri == null || !catalogueUri.IsAbsoluteUri ||
                catalogueUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(catalogueUri.UserInfo))
                throw new ArgumentException("Catalogue URI must be absolute HTTPS without credentials.", nameof(catalogueUri));
            if (!Version.TryParse(runtimeVersion, out var parsedRuntimeVersion))
                throw new ArgumentException("Runtime version must use a numeric version format such as 1.2.3.", nameof(runtimeVersion));
            _runtimeVersion = parsedRuntimeVersion;
            _catalogueUri = catalogueUri;
            _installRoot = Path.GetFullPath(installRoot ?? throw new ArgumentNullException(nameof(installRoot)));
            _downloadRoot = Path.GetFullPath(downloadRoot ?? throw new ArgumentNullException(nameof(downloadRoot)));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _acceptance = acceptance ?? throw new ArgumentNullException(nameof(acceptance));
            _leaseProvider = leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider));
            if (installer != null && loader == null)
                throw new ArgumentException("A custom installer must be paired with the package loader configured for this runtime version.", nameof(loader));
            _ownsDownloader = downloader == null;
            _downloader = downloader ?? new EpisodePackageDownloader();
            _loader = loader ?? new EpisodePackageLoader(
                new Nightwire.Runtime.Domain.EpisodePackageLoaderOptions { RuntimeVersion = runtimeVersion });
            _installer = installer ?? new EpisodePackageInstaller(loader: _loader);
        }

        /// <summary>
        /// Performs interrupted-install recovery once, before library discovery or installation.
        /// The manager serializes this operation with disposal and install admission.
        /// </summary>
        public EpisodeInstallRecoveryResult RecoverAtStartup()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(EpisodeReleaseManager));
                if (_startupRecovery != null) return _startupRecovery;
                _startupRecovery = _installer.RecoverInterruptedInstalls(_installRoot);
                return _startupRecovery;
            }
        }

        public async Task<ReleaseCatalogueResult> RefreshCatalogueAsync(DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_disposed)
                    return CatalogueFailure("manager_disposed", "Episode release manager has been disposed.");
                _activeRefreshes++;
            }

            bool acquired = false;
            try
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _lifetimeCancellation.Token);
                CancellationToken operationToken = linkedCancellation.Token;
                try
                {
                    await _refreshGate.WaitAsync(operationToken).ConfigureAwait(false);
                    acquired = true;
                }
                catch (OperationCanceledException)
                {
                    return CatalogueFailure("catalogue_fetch_cancelled", "Release catalogue refresh was cancelled.");
                }

                byte[] envelope;
                try
                {
                    envelope = await _transport.FetchAsync(_catalogueUri, operationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CatalogueFailure("catalogue_fetch_cancelled", "Release catalogue refresh was cancelled.");
                }
                catch (Exception exception)
                {
                    return CatalogueFailure("catalogue_fetch_failed", exception.Message);
                }

                // Linearize catalogue acceptance against manager disposal. Checking
                // cancellation and persisting the accepted sequence must be one critical
                // section, otherwise Dispose could race between the check and persistence.
                lock (_gate)
                {
                    if (_disposed || operationToken.IsCancellationRequested)
                        return CatalogueFailure("catalogue_fetch_cancelled",
                            "Release catalogue refresh was cancelled.");
                    var accepted = _acceptance.Accept(envelope, utcNow);
                    if (accepted.Success && accepted.Catalogue != null)
                        _catalogue = accepted.Catalogue;
                    return accepted;
                }
            }
            catch (OperationCanceledException)
            {
                return CatalogueFailure("catalogue_fetch_cancelled", "Release catalogue refresh was cancelled.");
            }
            finally
            {
                if (acquired) _refreshGate.Release();
                CompleteRefreshOperation();
            }
        }

        public IReadOnlyList<EpisodeLibraryEntry> GetLibrary()
        {
            RecoverAtStartup();
            lock (EpisodePackageInstaller.GetInstallRootLock(_installRoot))
                return GetLibraryCore();
        }

        private IReadOnlyList<EpisodeLibraryEntry> GetLibraryCore()
        {
            VerifiedReleaseCatalogue? catalogue;
            lock (_gate) catalogue = _catalogue;
            var releases = catalogue?.Releases.ToDictionary(x => x.EpisodeId, StringComparer.Ordinal)
                ?? new Dictionary<string, EpisodeReleaseEntry>(StringComparer.Ordinal);
            var ids = new HashSet<string>(releases.Keys, StringComparer.Ordinal);
            EnsureNoLinkedAncestors(_installRoot);
            if (Directory.Exists(_installRoot))
            {
                foreach (string directory in Directory.GetDirectories(_installRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    string id = Path.GetFileName(directory);
                    if (!id.StartsWith(".", StringComparison.Ordinal)) ids.Add(id);
                }
            }

            var result = new List<EpisodeLibraryEntry>();
            foreach (string id in ids.OrderBy(x => x, StringComparer.Ordinal))
            {
                releases.TryGetValue(id, out var release);
                var entry = new EpisodeLibraryEntry
                {
                    EpisodeId = id,
                    AvailableVersion = release?.EpisodeVersion,
                    MinimumRuntimeVersion = release?.MinimumRuntimeVersion
                };
                string directory = Path.Combine(_installRoot, id);
                if (Directory.Exists(directory))
                {
                    try
                    {
                        var package = _loader.Load(directory);
                        if (!string.Equals(package.Descriptor.EpisodeId, id, StringComparison.Ordinal))
                            throw new InvalidDataException("Installed directory identity does not match its episode descriptor.");
                        entry.InstalledVersion = package.Descriptor.EpisodeVersion;
                        entry.InstallDirectory = directory;
                        entry.Status = release == null ? EpisodeLibraryStatus.InstalledButNotInCatalogue :
                            string.Equals(entry.InstalledVersion, release.EpisodeVersion, StringComparison.Ordinal)
                                ? EpisodeLibraryStatus.InstalledOffline : EpisodeLibraryStatus.UpdateAvailable;
                    }
                    catch
                    {
                        entry.Status = EpisodeLibraryStatus.InstalledPackageInvalid;
                    }
                }
                else entry.Status = EpisodeLibraryStatus.AvailableToDownload;
                result.Add(entry);
            }
            return result.AsReadOnly();
        }

        public async Task<EpisodeManagerOperationResult> InstallOrUpdateAsync(string episodeId,
            IProgress<EpisodeDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            try { RecoverAtStartup(); }
            catch (ObjectDisposedException)
            {
                return Failure("manager_disposed", "Episode release manager has been disposed.", episodeId);
            }
            catch (Exception exception)
            {
                return Failure("install_recovery_failed", exception.Message, episodeId);
            }

            EpisodeReleaseEntry? release;
            lock (_gate)
            {
                if (_disposed) return Failure("manager_disposed", "Episode release manager has been disposed.", episodeId);
                release = _catalogue?.Releases.FirstOrDefault(x =>
                    string.Equals(x.EpisodeId, episodeId, StringComparison.Ordinal));
                if (release == null) return Failure("release_not_found", "Episode is not present in the currently accepted release catalogue.", episodeId);
                if (!_operations.Add(episodeId))
                    return Failure("episode_operation_in_progress", "Another download or install is already running for this episode.", episodeId);
                if (!TryAcquireSharedEpisodeOperation(episodeId))
                {
                    _operations.Remove(episodeId);
                    return Failure("episode_operation_in_progress",
                        "Another release manager is already downloading or installing this episode.", episodeId);
                }
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetimeCancellation.Token);
            CancellationToken operationToken = linkedCancellation.Token;
            IDisposable? lease = null;
            string? archivePath = null;
            try
            {
                operationToken.ThrowIfCancellationRequested();
                if (!_leaseProvider.TryAcquireInstallLease(episodeId, out lease) || lease == null)
                    return Failure("episode_session_active", "Episode cannot be updated while a gameplay session is active.", episodeId);
                if (!Version.TryParse(release.MinimumRuntimeVersion, out var minimumRuntimeVersion) ||
                    _runtimeVersion < minimumRuntimeVersion)
                    return Failure("runtime_version_incompatible", "This episode requires a newer or unsupported runtime version.", episodeId);

                // Downloader and installer validate every path ancestor before creating their roots.
                // Cache identity follows the canonical install root, not manager lifetime.
                // This isolates different libraries that share a download root while allowing
                // a later manager instance to reuse a previously verified archive.
                string managerDownloadRoot = GetDownloadCacheDirectory(_installRoot, _downloadRoot);
                archivePath = Path.Combine(managerDownloadRoot, episodeId + "-" + release.EpisodeVersion + ".zip");
                var download = await _downloader.DownloadReleaseAsync(release, archivePath, progress, operationToken)
                    .ConfigureAwait(false);
                operationToken.ThrowIfCancellationRequested();
                if (!download.Success)
                    return new EpisodeManagerOperationResult
                    {
                        Success = false, EpisodeId = episodeId, EpisodeVersion = release.EpisodeVersion,
                        ErrorCode = download.ErrorCode, Error = download.Error, Download = download
                    };

                // Admission to the synchronous install commit is linearized against Dispose.
                // If shutdown wins this gate, no filesystem replacement begins. If this check
                // wins, the install is already admitted; InstallArchive is synchronous and
                // cannot be interrupted safely halfway through its rollback-capable swap.
                lock (_gate)
                {
                    if (_disposed || operationToken.IsCancellationRequested)
                        return Failure("episode_install_cancelled",
                            "Episode installation was cancelled before its install commit began.", episodeId);
                }

                var install = _installer.InstallArchive(archivePath, _installRoot, release.EpisodeId, release.EpisodeVersion);
                if (!install.Success)
                    return new EpisodeManagerOperationResult
                    {
                        Success = false, EpisodeId = episodeId, EpisodeVersion = release.EpisodeVersion,
                        ErrorCode = install.ErrorCode, Error = install.Error, Download = download, Install = install
                    };
                return new EpisodeManagerOperationResult
                {
                    Success = true, EpisodeId = episodeId, EpisodeVersion = release.EpisodeVersion,
                    Download = download, Install = install
                };
            }
            catch (OperationCanceledException)
            {
                return Failure("episode_install_cancelled", "Episode installation was cancelled.", episodeId);
            }
            catch (Exception exception)
            {
                return Failure("episode_install_failed", exception.Message, episodeId);
            }
            finally
            {
                try { lease?.Dispose(); }
                finally
                {
                    lock (_gate) _operations.Remove(episodeId);
                    ReleaseSharedEpisodeOperation(episodeId);
                    DisposeDownloaderIfReady();
                }
                // Keep a successfully downloaded archive as a cache; failed partial downloads are removed by downloader.
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (AggregateException)
            {
                // Cancellation callbacks belong to transports/consumers; they must not
                // prevent deterministic release-manager teardown.
            }
            finally
            {
                lock (_gate) _shutdownSignalCompleted = true;
                DisposeResourcesIfReady();
            }
            // Resource disposal is deferred until all refreshes and installs have unwound.
        }

        private static string GetDownloadCacheDirectory(string installRoot, string downloadRoot)
        {
            string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
            if (Path.DirectorySeparatorChar == '\\\\')
                canonicalRoot = canonicalRoot.ToUpperInvariant();
            byte[] identity = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalRoot));
            var builder = new System.Text.StringBuilder(identity.Length * 2);
            foreach (byte value in identity) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return Path.Combine(downloadRoot, ".library-" + builder);
        }

        private bool TryAcquireSharedEpisodeOperation(string episodeId)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_installRoot));
            string key = root + "\\n" + episodeId;
            lock (SharedEpisodeOperationsGate)
                return SharedEpisodeOperations.Add(key);
        }

        private void ReleaseSharedEpisodeOperation(string episodeId)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_installRoot));
            string key = root + "\\n" + episodeId;
            lock (SharedEpisodeOperationsGate)
                SharedEpisodeOperations.Remove(key);
        }

        private static void EnsureNoLinkedAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Episode library root or one of its parent directories is a symbolic link or reparse point.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                    break;
                current = parent;
            }
        }

        private void CompleteRefreshOperation()
        {
            lock (_gate)
            {
                if (_activeRefreshes > 0) _activeRefreshes--;
            }
            DisposeResourcesIfReady();
        }

        private void DisposeDownloaderIfReady() => DisposeResourcesIfReady();

        private void DisposeResourcesIfReady()
        {
            bool disposeDownloader = false;
            bool disposeLifetimeResources = false;
            lock (_gate)
            {
                if (!_disposed) return;
                if (_ownsDownloader && !_downloaderDisposed && _operations.Count == 0)
                {
                    _downloaderDisposed = true;
                    disposeDownloader = true;
                }
                if (!_lifetimeResourcesDisposed && _shutdownSignalCompleted &&
                    _operations.Count == 0 && _activeRefreshes == 0)
                {
                    _lifetimeResourcesDisposed = true;
                    disposeLifetimeResources = true;
                }
            }

            if (disposeDownloader) _downloader.Dispose();
            if (disposeLifetimeResources)
            {
                _refreshGate.Dispose();
                _lifetimeCancellation.Dispose();
            }
        }

        private static ReleaseCatalogueResult CatalogueFailure(string code, string message) =>
            new ReleaseCatalogueResult { Success = false, ErrorCode = code, Error = message };

        private static EpisodeManagerOperationResult Failure(string code, string message, string episodeId) =>
            new EpisodeManagerOperationResult { Success = false, EpisodeId = episodeId, ErrorCode = code, Error = message };
    }
}

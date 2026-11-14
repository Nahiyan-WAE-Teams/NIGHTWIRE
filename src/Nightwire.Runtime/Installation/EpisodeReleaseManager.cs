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
        private readonly object _gate = new object();
        private readonly HashSet<string> _operations = new HashSet<string>(StringComparer.Ordinal);
        private VerifiedReleaseCatalogue? _catalogue;

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
            _ownsDownloader = downloader == null;
            _downloader = downloader ?? new EpisodePackageDownloader();
            _installer = installer ?? new EpisodePackageInstaller();
            _loader = loader ?? new EpisodePackageLoader();
        }

        /// <summary>Must run before sessions or installs are opened.</summary>
        public EpisodeInstallRecoveryResult RecoverAtStartup()
        {
            return _installer.RecoverInterruptedInstalls(_installRoot);
        }

        public async Task<ReleaseCatalogueResult> RefreshCatalogueAsync(DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            try { await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                return CatalogueFailure("catalogue_fetch_cancelled", "Release catalogue refresh was cancelled.");
            }

            try
            {
                byte[] envelope;
                try
                {
                    envelope = await _transport.FetchAsync(_catalogueUri, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return CatalogueFailure("catalogue_fetch_cancelled", "Release catalogue refresh was cancelled.");
                }
                catch (Exception exception)
                {
                    return CatalogueFailure("catalogue_fetch_failed", exception.Message);
                }

                var accepted = _acceptance.Accept(envelope, utcNow);
                if (accepted.Success && accepted.Catalogue != null)
                {
                    lock (_gate) _catalogue = accepted.Catalogue;
                }
                return accepted;
            }
            finally { _refreshGate.Release(); }
        }

        public IReadOnlyList<EpisodeLibraryEntry> GetLibrary()
        {
            VerifiedReleaseCatalogue? catalogue;
            lock (_gate) catalogue = _catalogue;
            var releases = catalogue?.Releases.ToDictionary(x => x.EpisodeId, StringComparer.Ordinal)
                ?? new Dictionary<string, EpisodeReleaseEntry>(StringComparer.Ordinal);
            var ids = new HashSet<string>(releases.Keys, StringComparer.Ordinal);
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
            EpisodeReleaseEntry? release;
            lock (_gate)
            {
                release = _catalogue?.Releases.FirstOrDefault(x =>
                    string.Equals(x.EpisodeId, episodeId, StringComparison.Ordinal));
                if (release == null) return Failure("release_not_found", "Episode is not present in the currently accepted release catalogue.", episodeId);
                if (!_operations.Add(episodeId))
                    return Failure("episode_operation_in_progress", "Another download or install is already running for this episode.", episodeId);
            }

            IDisposable? lease = null;
            string? archivePath = null;
            try
            {
                if (!_leaseProvider.TryAcquireInstallLease(episodeId, out lease) || lease == null)
                    return Failure("episode_session_active", "Episode cannot be updated while a gameplay session is active.", episodeId);
                if (!Version.TryParse(release.MinimumRuntimeVersion, out var minimumRuntimeVersion) ||
                    _runtimeVersion < minimumRuntimeVersion)
                    return Failure("runtime_version_incompatible", "This episode requires a newer or unsupported runtime version.", episodeId);

                Directory.CreateDirectory(_downloadRoot);
                Directory.CreateDirectory(_installRoot);
                archivePath = Path.Combine(_downloadRoot, episodeId + "-" + release.EpisodeVersion + ".zip");
                var download = await _downloader.DownloadReleaseAsync(release, archivePath, progress, cancellationToken)
                    .ConfigureAwait(false);
                if (!download.Success)
                    return new EpisodeManagerOperationResult
                    {
                        Success = false, EpisodeId = episodeId, EpisodeVersion = release.EpisodeVersion,
                        ErrorCode = download.ErrorCode, Error = download.Error, Download = download
                    };

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
                }
                // Keep a successfully downloaded archive as a cache; failed partial downloads are removed by downloader.
            }
        }

        public void Dispose()
        {
            if (_ownsDownloader) _downloader.Dispose();
            _refreshGate.Dispose();
        }

        private static ReleaseCatalogueResult CatalogueFailure(string code, string message) =>
            new ReleaseCatalogueResult { Success = false, ErrorCode = code, Error = message };

        private static EpisodeManagerOperationResult Failure(string code, string message, string episodeId) =>
            new EpisodeManagerOperationResult { Success = false, EpisodeId = episodeId, ErrorCode = code, Error = message };
    }
}

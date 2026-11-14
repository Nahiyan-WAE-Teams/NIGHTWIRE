using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Nightwire.Runtime.Installation
{
    public sealed class EpisodeDownloadOptions
    {
        public long MaximumDownloadBytes { get; set; } = 536870912;
        public int BufferSizeBytes { get; set; } = 81920;
    }

    public sealed class EpisodeDownloadProgress
    {
        public long BytesReceived { get; internal set; }
        public long? TotalBytes { get; internal set; }
    }

    public sealed class EpisodeDownloadResult
    {
        public bool Success { get; internal set; }
        public string? FilePath { get; internal set; }
        public long BytesDownloaded { get; internal set; }
        public string? Sha256 { get; internal set; }
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
    }

    /// <summary>
    /// Streams an HTTPS episode archive to a temporary file and atomically publishes it only after validation.
    /// The optional expected hash must come from a trusted release catalogue, not from the archive itself.
    /// </summary>
    public sealed class EpisodePackageDownloader : IDisposable
    {
        private readonly HttpClient _client;
        private readonly bool _ownsClient;
        private readonly EpisodeDownloadOptions _options;

        public EpisodePackageDownloader(EpisodeDownloadOptions? options = null, HttpClient? client = null)
        {
            _options = options ?? new EpisodeDownloadOptions();
            if (_options.MaximumDownloadBytes <= 0 || _options.BufferSizeBytes < 4096)
                throw new ArgumentOutOfRangeException(nameof(options), "Download limits are invalid.");
            if (client == null)
            {
                _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                _ownsClient = true;
            }
            else _client = client;
        }

        public async Task<EpisodeDownloadResult> DownloadAsync(Uri source, string destinationPath,
            string? expectedSha256 = null, IProgress<EpisodeDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default, long? expectedSizeBytes = null)
        {
            string? temporary = null;
            try
            {
                if (source == null || !source.IsAbsoluteUri || source.Scheme != Uri.UriSchemeHttps ||
                    !string.IsNullOrEmpty(source.UserInfo))
                    return Error("https_required", "Episode downloads require an absolute HTTPS URL without embedded credentials.");
                if (!string.IsNullOrEmpty(expectedSha256) && !IsSha256(expectedSha256))
                    return Error("invalid_expected_hash", "Expected SHA-256 must contain 64 hexadecimal characters.");
                if (expectedSizeBytes.HasValue && (expectedSizeBytes.Value <= 0 || expectedSizeBytes.Value > _options.MaximumDownloadBytes))
                    return Error("expected_size_invalid", "Trusted release archive size is outside the configured download limit.");
                if (string.IsNullOrWhiteSpace(destinationPath))
                    return Error("destination_required", "A download destination is required.");

                string destination = Path.GetFullPath(destinationPath);
                string? directory = Path.GetDirectoryName(destination);
                if (string.IsNullOrEmpty(directory)) return Error("destination_invalid", "Download destination directory is invalid.");
                EnsureNoLinkedAncestors(destination);
                Directory.CreateDirectory(directory);
                EnsureNoLinkedAncestors(destination);
                temporary = Path.Combine(directory, ".download-" + Guid.NewGuid().ToString("N") + ".part");

                using var request = new HttpRequestMessage(HttpMethod.Get, source);
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;
                if (statusCode >= 300 && statusCode <= 399)
                    return Error("redirect_not_allowed", "Episode download redirects are disabled; use the final HTTPS release URL.");
                if (!response.IsSuccessStatusCode)
                    return Error("http_error", "Episode server returned HTTP " + (int)response.StatusCode + ".");
                if (response.Content.Headers.ContentLength.HasValue &&
                    response.Content.Headers.ContentLength.Value > _options.MaximumDownloadBytes)
                    return Error("download_too_large", "Episode archive exceeds the configured download limit.");
                if (expectedSizeBytes.HasValue && response.Content.Headers.ContentLength.HasValue &&
                    response.Content.Headers.ContentLength.Value != expectedSizeBytes.Value)
                    return Error("download_size_mismatch", "Server content length differs from the signed release catalogue.");

                long? total = response.Content.Headers.ContentLength;
                long received = 0;
                using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    _options.BufferSizeBytes, true);
                using var sha = SHA256.Create();
                byte[] buffer = new byte[_options.BufferSizeBytes];
                while (true)
                {
                    int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    received += read;
                    if (received > _options.MaximumDownloadBytes)
                        return Error("download_too_large", "Episode archive exceeded the configured download limit.");
                    await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    progress?.Report(new EpisodeDownloadProgress { BytesReceived = received, TotalBytes = total });
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string digest = ToHex(sha.Hash!);
                if (total.HasValue && received != total.Value)
                    return Error("download_truncated", "Downloaded byte count did not match the server's content length.");
                if (expectedSizeBytes.HasValue && received != expectedSizeBytes.Value)
                    return Error("download_size_mismatch", "Downloaded byte count differs from the signed release catalogue.");
                if (!string.IsNullOrEmpty(expectedSha256) &&
                    !string.Equals(digest, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    return Error("download_hash_mismatch", "Downloaded archive does not match the trusted release SHA-256.");

                File.Move(temporary, destination, true);
                temporary = null;
                return new EpisodeDownloadResult
                {
                    Success = true, FilePath = destination, BytesDownloaded = received, Sha256 = digest
                };
            }
            catch (OperationCanceledException)
            {
                return Error("download_cancelled", "Episode download was cancelled.");
            }
            catch (HttpRequestException exception) { return Error("network_error", exception.Message); }
            catch (UnauthorizedAccessException exception) { return Error("download_access_denied", exception.Message); }
            catch (IOException exception) { return Error("download_io_failed", exception.Message); }
            finally
            {
                if (temporary != null && File.Exists(temporary))
                {
                    try { File.Delete(temporary); } catch { }
                }
            }
        }

        public Task<EpisodeDownloadResult> DownloadReleaseAsync(EpisodeReleaseEntry release, string destinationPath,
            IProgress<EpisodeDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            return DownloadAsync(release.ArchiveUrl, destinationPath, release.ArchiveSha256,
                progress, cancellationToken, release.ArchiveSizeBytes);
        }

        private static void EnsureNoLinkedAncestors(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Download destination or one of its parent directories is a symbolic link or reparse point.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
                    break;
                current = parent;
            }
        }

        private static bool IsSha256(string value)
        {
            if (value.Length != 64) return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        private static string ToHex(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private static EpisodeDownloadResult Error(string code, string message) =>
            new EpisodeDownloadResult { Success = false, ErrorCode = code, Error = message };

        public void Dispose()
        {
            if (_ownsClient) _client.Dispose();
        }
    }
}

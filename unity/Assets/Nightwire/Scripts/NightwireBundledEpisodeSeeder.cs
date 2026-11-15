using System;
using System.Collections;
using System.IO;
using Nightwire.Runtime.Installation;
using Nightwire.Runtime.Loading;
using UnityEngine;
using UnityEngine.Networking;

namespace Nightwire.Unity
{
    /// <summary>
    /// Installs the bundled Episode 001 sample before the application root opens the library.
    /// This is a local demo seed, not the production signed-catalogue update path.
    /// </summary>
    public sealed class NightwireBundledEpisodeSeeder : MonoBehaviour
    {
        [SerializeField] private string episodeId = "nightwire.ep001";
        [SerializeField] private string episodeVersion = "1.0.0";
        [SerializeField] private long maximumBundledArchiveBytes = 536870912;

        public IEnumerator EnsureInstalled(Action<bool> completed)
        {
            string dataRoot = Path.Combine(Application.persistentDataPath, "Nightwire");
            string installRoot = Path.Combine(dataRoot, "Episodes");
            string downloadRoot = Path.Combine(dataRoot, "Downloads");
            string destination = Path.Combine(installRoot, episodeId);
            var installer = new EpisodePackageInstaller();
            bool recoverySucceeded = true;
            bool existingValid = false;
            try
            {
                installer.RecoverInterruptedInstalls(installRoot);
                if (Directory.Exists(destination))
                {
                    try
                    {
                        var existing = new EpisodePackageLoader().Load(destination);
                        existingValid =
                            string.Equals(existing.Descriptor.EpisodeId, episodeId, StringComparison.Ordinal) &&
                            string.Equals(existing.Descriptor.EpisodeVersion, episodeVersion, StringComparison.Ordinal);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("NIGHTWIRE bundled episode will repair an invalid local copy: " + exception.Message, this);
                    }
                }
            }
            catch (Exception exception)
            {
                recoverySucceeded = false;
                Debug.LogException(exception, this);
            }

            if (!recoverySucceeded)
            {
                completed?.Invoke(false);
                yield break;
            }
            if (existingValid)
            {
                completed?.Invoke(true);
                yield break;
            }

            string streamingArchivePath = Path.Combine(Application.streamingAssetsPath, "Nightwire", "Episode_001.zip");
            string archiveUrl = streamingArchivePath.StartsWith("jar:", StringComparison.OrdinalIgnoreCase)
                ? streamingArchivePath
                : new Uri(streamingArchivePath).AbsoluteUri;
            using (var request = UnityWebRequest.Get(archiveUrl))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("NIGHTWIRE bundled episode could not be read: " + request.error, this);
                    completed?.Invoke(false);
                    yield break;
                }

                try
                {
                    byte[] archiveBytes = request.downloadHandler.data;
                    if (archiveBytes == null || archiveBytes.Length == 0 ||
                        archiveBytes.LongLength > maximumBundledArchiveBytes)
                        throw new InvalidDataException("Bundled episode archive is empty or exceeds its configured size limit.");

                    Directory.CreateDirectory(downloadRoot);
                    string downloadedArchivePath = Path.Combine(downloadRoot, "bundled-episode-001.zip");
                    File.WriteAllBytes(downloadedArchivePath, archiveBytes);
                    var result = installer.InstallArchive(downloadedArchivePath, installRoot, episodeId, episodeVersion);
                    if (!result.Success)
                        throw new InvalidDataException("Bundled episode installation failed: " + result.Error);
                    completed?.Invoke(true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    completed?.Invoke(false);
                }
            }
        }
    }
}

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
            bool success = false;
            string dataRoot = Path.Combine(Application.persistentDataPath, "Nightwire");
            string installRoot = Path.Combine(dataRoot, "Episodes");
            string downloadRoot = Path.Combine(dataRoot, "Downloads");
            string destination = Path.Combine(installRoot, episodeId);

            try
            {
                var installer = new EpisodePackageInstaller();
                installer.RecoverInterruptedInstalls(installRoot);
                if (Directory.Exists(destination))
                {
                    try
                    {
                        var existing = new EpisodePackageLoader().Load(destination);
                        if (string.Equals(existing.Descriptor.EpisodeId, episodeId, StringComparison.Ordinal) &&
                            string.Equals(existing.Descriptor.EpisodeVersion, episodeVersion, StringComparison.Ordinal))
                        {
                            success = true;
                            completed?.Invoke(true);
                            yield break;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("NIGHTWIRE bundled episode will repair an invalid local copy: " + exception.Message, this);
                    }
                }

                string archiveUrl = Path.Combine(Application.streamingAssetsPath, "Nightwire", "Episode_001.zip")
                    .Replace('\\', '/');
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

                    byte[] archiveBytes = request.downloadHandler.data;
                    if (archiveBytes == null || archiveBytes.Length == 0 ||
                        archiveBytes.LongLength > maximumBundledArchiveBytes)
                    {
                        Debug.LogError("NIGHTWIRE bundled episode archive is empty or exceeds its configured size limit.", this);
                        completed?.Invoke(false);
                        yield break;
                    }

                    Directory.CreateDirectory(downloadRoot);
                    string archivePath = Path.Combine(downloadRoot, "bundled-episode-001.zip");
                    File.WriteAllBytes(archivePath, archiveBytes);
                    var result = installer.InstallArchive(archivePath, installRoot, episodeId, episodeVersion);
                    if (!result.Success)
                    {
                        Debug.LogError("NIGHTWIRE bundled episode installation failed: " + result.Error, this);
                        completed?.Invoke(false);
                        yield break;
                    }
                    success = true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            completed?.Invoke(success);
        }
    }
}

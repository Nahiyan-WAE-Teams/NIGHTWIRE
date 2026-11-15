using System;
using System.Collections;
using System.IO;
using System.Linq;
using Nightwire.Runtime.Installation;
using Nightwire.Runtime.Saves;
using UnityEngine;

namespace Nightwire.Unity
{
    /// <summary>
    /// Unity composition root for the episode archive and single active runtime session.
    /// Security-sensitive release services are injected by a platform-specific provider.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class NightwireApplicationRoot : MonoBehaviour
    {
        [SerializeField] private EpisodeLibraryViewController libraryView;
        [SerializeField] private Nightwire.Unity.UI.MessengerScreenController messengerView;
        [SerializeField] private NightwirePlatformServicesBehaviour platformServices;
        [SerializeField] private NightwireBundledEpisodeSeeder bundledEpisodeSeeder;
        [SerializeField] private string runtimeVersion = "0.1.0";

        private HttpReleaseCatalogueTransport catalogueTransport;
        private EpisodeReleaseManager releaseManager;
        private EpisodeLibraryPresentation libraryPresentation;
        private EpisodeAccessCoordinator accessCoordinator;
        private EpisodeSessionHost sessionHost;
        private SaveStore saveStore;
        private bool initialized;

        public EpisodeLibraryPresentation Library => libraryPresentation;
        public EpisodeSessionHost Sessions => sessionHost;

        private bool Initialize()
        {
            if (initialized) return true;
            if (libraryView == null) libraryView = FindObjectOfType<EpisodeLibraryViewController>();
            if (libraryView == null || messengerView == null || platformServices == null)
            {
                Debug.LogError("NIGHTWIRE bootstrap requires episode library and messenger view controllers plus a platform services provider.", this);
                return false;
            }
            if (!Version.TryParse(runtimeVersion, out _))
            {
                Debug.LogError("NIGHTWIRE runtimeVersion must be numeric, for example 0.1.0.", this);
                return false;
            }

            try
            {
                Uri catalogueUri = platformServices.CatalogueUri;
                var trustedKeys = platformServices.GetTrustedReleaseKeys();
                var sequenceStore = platformServices.CreateCatalogueSequenceStore();
                if (catalogueUri == null || trustedKeys == null || trustedKeys.Count == 0 || sequenceStore == null)
                    throw new InvalidOperationException("Platform provider did not supply a catalogue URL, trusted public keys, and protected sequence storage.");

                string dataRoot = Path.Combine(Application.persistentDataPath, "Nightwire");
                string installRoot = Path.Combine(dataRoot, "Episodes");
                string downloadRoot = Path.Combine(dataRoot, "Downloads");
                string saveRoot = Path.Combine(dataRoot, "Saves");
                Directory.CreateDirectory(dataRoot);

                accessCoordinator = new EpisodeAccessCoordinator();
                saveStore = new SaveStore(saveRoot);
                var verifier = new ReleaseCatalogueVerifier(trustedKeys);
                var acceptance = new ReleaseCatalogueAcceptanceService(verifier, sequenceStore);
                catalogueTransport = new HttpReleaseCatalogueTransport();
                releaseManager = new EpisodeReleaseManager(catalogueUri, installRoot, downloadRoot,
                    catalogueTransport, acceptance, accessCoordinator, runtimeVersion);
                var recovery = releaseManager.RecoverAtStartup();
                foreach (string warning in recovery.Warnings)
                    Debug.LogWarning("NIGHTWIRE install recovery: " + warning, this);

                sessionHost = new EpisodeSessionHost(accessCoordinator, saveStore, runtimeVersion);
                libraryPresentation = new EpisodeLibraryPresentation(releaseManager);
                libraryView.Bind(libraryPresentation);
                libraryView.PlayEpisodeRequested += PlayEpisodeRequested;
                if (messengerView != null)
                {
                    messengerView.SetVisible(false);
                    messengerView.CloseRequested += MessengerCloseRequested;
                }
                initialized = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DisposeServices();
                return false;
            }
        }

        private IEnumerator Start()
        {
            if (bundledEpisodeSeeder == null)
                bundledEpisodeSeeder = FindObjectOfType<NightwireBundledEpisodeSeeder>();

            if (bundledEpisodeSeeder != null)
            {
                bool seeded = false;
                yield return bundledEpisodeSeeder.EnsureInstalled(result => seeded = result);
                if (!seeded)
                    Debug.LogWarning("NIGHTWIRE bundled demo episode was not installed. The library will still open for locally installed episodes.", this);
            }

            if (!Initialize())
            {
                enabled = false;
                yield break;
            }

            libraryPresentation.ReloadInstalledLibrary();
            if (platformServices == null || !platformServices.ShouldRefreshCatalogue)
                yield break;

            var refreshTask = libraryPresentation.RefreshAsync(DateTime.UtcNow);
            while (!refreshTask.IsCompleted)
                yield return null;

            if (refreshTask.IsFaulted)
                Debug.LogException(refreshTask.Exception, this);
            else if (refreshTask.IsCanceled)
                Debug.LogWarning("NIGHTWIRE release catalogue refresh was cancelled.", this);
            else if (!refreshTask.Result.Success)
                Debug.LogWarning("NIGHTWIRE release catalogue is unavailable; installed episodes remain accessible offline. " + refreshTask.Result.Error, this);
        }

        private void PlayEpisodeRequested(string episodeId)
        {
            if (!initialized || releaseManager == null || sessionHost == null) return;
            var entry = releaseManager.GetLibrary().FirstOrDefault(x =>
                string.Equals(x.EpisodeId, episodeId, StringComparison.Ordinal));
            if (entry == null || string.IsNullOrEmpty(entry.InstallDirectory))
            {
                Debug.LogWarning("NIGHTWIRE cannot locate a verified installed episode: " + episodeId, this);
                libraryPresentation?.ReloadInstalledLibrary();
                return;
            }

            var result = sessionHost.OpenEpisode(episodeId, entry.InstallDirectory);
            if (!result.Success)
            {
                Debug.LogWarning("NIGHTWIRE could not open episode: " + result.Error, this);
                return;
            }

            try
            {
                messengerView.Bind(sessionHost);
                messengerView.SetVisible(true);
                libraryView.SetVisible(false);
                Debug.Log("NIGHTWIRE messenger opened: " + episodeId, this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                CloseActiveSession();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && sessionHost?.ActiveSession != null)
            {
                var result = sessionHost.SaveCheckpoint();
                if (result != null && !result.Success)
                    Debug.LogError("NIGHTWIRE pause save failed: " + result.Error, this);
            }
        }

        private void OnApplicationQuit()
        {
            CloseActiveSession();
        }

        private void OnDestroy()
        {
            if (libraryView != null) libraryView.PlayEpisodeRequested -= PlayEpisodeRequested;
            if (messengerView != null) messengerView.CloseRequested -= MessengerCloseRequested;
            CloseActiveSession();
            DisposeServices();
        }

        private void MessengerCloseRequested()
        {
            CloseActiveSession();
        }

        public void CloseActiveSession()
        {
            if (sessionHost?.ActiveSession != null)
            {
                var result = sessionHost.SaveAndClose();
                if (!result.Success)
                    Debug.LogError("NIGHTWIRE session close save failed: " + result.Error, this);
            }

            if (messengerView != null)
            {
                messengerView.SetVisible(false);
                messengerView.UnbindForSessionClose();
            }
            if (libraryView != null) libraryView.SetVisible(true);
        }

        private void DisposeServices()
        {
            libraryPresentation?.Dispose();
            libraryPresentation = null;
            releaseManager?.Dispose();
            releaseManager = null;
            catalogueTransport?.Dispose();
            catalogueTransport = null;
            sessionHost = null;
            saveStore = null;
            accessCoordinator = null;
            initialized = false;
        }
    }
}

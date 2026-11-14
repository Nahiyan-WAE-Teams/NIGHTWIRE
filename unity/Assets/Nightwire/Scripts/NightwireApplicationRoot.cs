using System;
using System.IO;
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
        [SerializeField] private NightwirePlatformServicesBehaviour platformServices;
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

        private void Awake()
        {
            if (!Initialize())
                enabled = false;
        }

        private bool Initialize()
        {
            if (initialized) return true;
            if (libraryView == null) libraryView = FindObjectOfType<EpisodeLibraryViewController>();
            if (libraryView == null || platformServices == null)
            {
                Debug.LogError("NIGHTWIRE bootstrap requires an EpisodeLibraryViewController and a platform services provider.", this);
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

        private async void Start()
        {
            if (!initialized || libraryPresentation == null) return;
            try
            {
                var result = await libraryPresentation.RefreshAsync(DateTime.UtcNow);
                if (!result.Success)
                    Debug.LogWarning("NIGHTWIRE release catalogue is unavailable; installed episodes remain accessible offline. " + result.Error, this);
            }
            catch (Exception exception)
            {
                if (this != null) Debug.LogException(exception, this);
            }
        }

        private void PlayEpisodeRequested(string episodeId)
        {
            if (!initialized || releaseManager == null || sessionHost == null) return;
            var entry = releaseManager.GetLibrary().Find(x =>
                string.Equals(x.EpisodeId, episodeId, StringComparison.Ordinal));
            if (entry == null || string.IsNullOrEmpty(entry.InstallDirectory))
            {
                Debug.LogWarning("NIGHTWIRE cannot locate a verified installed episode: " + episodeId, this);
                libraryPresentation?.ReloadInstalledLibrary();
                return;
            }

            var result = sessionHost.OpenEpisode(episodeId, entry.InstallDirectory);
            if (!result.Success)
                Debug.LogWarning("NIGHTWIRE could not open episode: " + result.Error, this);
            else
                Debug.Log("NIGHTWIRE episode session opened: " + episodeId, this);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && sessionHost?.ActiveSession != null)
            {
                var result = sessionHost.ActiveSession.SaveNow();
                if (!result.Success)
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
            CloseActiveSession();
            DisposeServices();
        }

        public void CloseActiveSession()
        {
            if (sessionHost?.ActiveSession == null) return;
            var result = sessionHost.SaveAndClose();
            if (!result.Success)
                Debug.LogError("NIGHTWIRE session close save failed: " + result.Error, this);
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

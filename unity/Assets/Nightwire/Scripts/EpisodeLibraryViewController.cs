using System;
using Nightwire.Runtime.Installation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nightwire.Unity.UI
{
    /// <summary>
    /// UI Toolkit view for the portable episode library presentation model.
    /// Bind the presentation model from the composition root after constructing runtime services.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class EpisodeLibraryViewController : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        private EpisodeLibraryPresentation presentation;
        private ScrollView episodeList;
        private Label catalogueStatus;
        private VisualElement emptyState;
        private Button refreshButton;
        private Button cancelButton;
        private bool bound;

        /// <summary>The game host handles opening/resuming a verified installed episode.</summary>
        public event Action<string> PlayEpisodeRequested;

        public void SetVisible(bool visible)
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                document.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Bind(EpisodeLibraryPresentation model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Unbind();
            presentation = model;
            TryBindView();
        }

        private void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            TryBindView();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void TryBindView()
        {
            if (bound || presentation == null || document == null || document.rootVisualElement == null)
                return;

            var root = document.rootVisualElement;
            episodeList = root.Q<ScrollView>("episode-list");
            catalogueStatus = root.Q<Label>("catalogue-status");
            emptyState = root.Q<VisualElement>("empty-state");
            refreshButton = root.Q<Button>("refresh-button");
            cancelButton = root.Q<Button>("cancel-button");

            if (episodeList == null || catalogueStatus == null || emptyState == null ||
                refreshButton == null || cancelButton == null)
            {
                Debug.LogError("NIGHTWIRE EpisodeLibraryView UXML is missing a required named element.", this);
                return;
            }

            refreshButton.clicked += RefreshClicked;
            cancelButton.clicked += CancelClicked;
            presentation.Changed += Render;
            bound = true;
            Render(presentation.Snapshot);
        }

        private void Unbind()
        {
            if (bound)
            {
                refreshButton.clicked -= RefreshClicked;
                cancelButton.clicked -= CancelClicked;
                presentation.Changed -= Render;
            }
            bound = false;
            episodeList = null;
            catalogueStatus = null;
            emptyState = null;
            refreshButton = null;
            cancelButton = null;
        }

        private async void RefreshClicked()
        {
            if (presentation == null) return;
            refreshButton.SetEnabled(false);
            try
            {
                var result = await presentation.RefreshAsync(DateTime.UtcNow);
                if (!result.Success && this != null)
                    Debug.LogWarning("NIGHTWIRE catalogue refresh: " + result.Error, this);
            }
            catch (Exception exception)
            {
                if (this != null) Debug.LogException(exception, this);
            }
            finally
            {
                if (this != null && refreshButton != null)
                    refreshButton.SetEnabled(true);
            }
        }

        private void CancelClicked()
        {
            presentation?.CancelCurrentOperation();
        }

        private async void ActionClicked(string episodeId, Button button, EpisodeLibraryRow episode)
        {
            if (CanPlay(episode))
            {
                PlayEpisodeRequested?.Invoke(episodeId);
                return;
            }
            if (presentation == null) return;
            button.SetEnabled(false);
            try
            {
                var result = await presentation.InstallOrUpdateAsync(episodeId);
                if (!result.Success && this != null)
                    Debug.LogWarning("NIGHTWIRE episode install: " + result.Error, this);
            }
            catch (Exception exception)
            {
                if (this != null) Debug.LogException(exception, this);
            }
            finally
            {
                if (this != null && button != null)
                    button.SetEnabled(true);
            }
        }

        private void Render(EpisodeLibrarySnapshot snapshot)
        {
            if (!bound || episodeList == null) return;
            catalogueStatus.text = snapshot.IsRefreshing
                ? "Checking signed episode catalogue…"
                : snapshot.CatalogueError != null
                    ? "Catalogue unavailable · " + snapshot.CatalogueError
                    : snapshot.CatalogueAvailable
                        ? "Archive synchronized · installed episodes remain available offline"
                        : "Connect to check for new episodes";

            episodeList.Clear();
            foreach (var episode in snapshot.Episodes)
                episodeList.Add(BuildEpisodeCard(episode, snapshot.IsOperationBusy));

            emptyState.style.display = snapshot.Episodes.Count == 0
                ? DisplayStyle.Flex : DisplayStyle.None;
            refreshButton.SetEnabled(!snapshot.IsRefreshing && !snapshot.IsOperationBusy);
            cancelButton.SetEnabled(snapshot.IsOperationBusy);
        }

        private VisualElement BuildEpisodeCard(EpisodeLibraryRow episode, bool operationBusy)
        {
            var card = new VisualElement();
            card.AddToClassList("episode-card");

            var top = new VisualElement();
            top.AddToClassList("episode-card-top");
            var name = new Label(FormatEpisodeName(episode.EpisodeId));
            name.AddToClassList("episode-name");
            var status = new Label(StatusText(episode));
            status.AddToClassList("episode-status");
            top.Add(name);
            top.Add(status);
            card.Add(top);

            var meta = new Label(VersionText(episode));
            meta.AddToClassList("episode-meta");
            card.Add(meta);

            if (!string.IsNullOrEmpty(episode.Error))
            {
                var error = new Label(episode.Error);
                error.AddToClassList("episode-meta");
                card.Add(error);
            }

            var actions = new VisualElement();
            actions.AddToClassList("episode-actions");
            var action = new Button { text = ActionText(episode) };
            action.AddToClassList("episode-action");
            action.SetEnabled(!operationBusy && !episode.IsBusy && (CanInstall(episode) || CanPlay(episode)));
            string id = episode.EpisodeId;
            action.clicked += () => ActionClicked(id, action, episode);
            actions.Add(action);

            if (episode.IsBusy || episode.BytesReceived > 0)
            {
                var progress = new ProgressBar { lowValue = 0, highValue = 100 };
                progress.AddToClassList("episode-progress");
                progress.value = episode.ProgressFraction.HasValue
                    ? (float)(episode.ProgressFraction.Value * 100.0) : 0;
                progress.title = episode.TotalBytes.HasValue
                    ? FormatBytes(episode.BytesReceived) + " / " + FormatBytes(episode.TotalBytes.Value)
                    : FormatBytes(episode.BytesReceived);
                actions.Add(progress);
            }
            card.Add(actions);
            return card;
        }

        private static bool CanPlay(EpisodeLibraryRow episode)
        {
            return episode.Status == EpisodeLibraryStatus.InstalledOffline ||
                   episode.Status == EpisodeLibraryStatus.InstalledButNotInCatalogue;
        }

        private static bool CanInstall(EpisodeLibraryRow episode)
        {
            return episode.Status == EpisodeLibraryStatus.AvailableToDownload ||
                   episode.Status == EpisodeLibraryStatus.UpdateAvailable;
        }

        private static string ActionText(EpisodeLibraryRow episode)
        {
            if (episode.IsBusy) return "WORKING…";
            if (CanPlay(episode)) return "PLAY OFFLINE";
            if (episode.Status == EpisodeLibraryStatus.UpdateAvailable) return "UPDATE";
            if (episode.Status == EpisodeLibraryStatus.InstalledPackageInvalid) return "UNAVAILABLE";
            return "DOWNLOAD";
        }

        private static string StatusText(EpisodeLibraryRow episode)
        {
            switch (episode.Status)
            {
                case EpisodeLibraryStatus.InstalledOffline: return "VERIFIED · READY OFFLINE";
                case EpisodeLibraryStatus.InstalledButNotInCatalogue: return "LOCAL ARCHIVE · READY OFFLINE";
                case EpisodeLibraryStatus.UpdateAvailable: return "NEW RELEASE AVAILABLE";
                case EpisodeLibraryStatus.InstalledPackageInvalid: return "LOCAL PACKAGE FAILED VERIFICATION";
                default: return "AVAILABLE TO DOWNLOAD";
            }
        }

        private static string VersionText(EpisodeLibraryRow episode)
        {
            if (episode.InstalledVersion != null && episode.AvailableVersion != null)
                return "Installed " + episode.InstalledVersion + "   ·   Latest " + episode.AvailableVersion;
            if (episode.InstalledVersion != null) return "Installed version " + episode.InstalledVersion;
            return episode.AvailableVersion == null
                ? "Installed content could not be verified"
                : "Version " + episode.AvailableVersion + "   ·   Requires runtime " + episode.MinimumRuntimeVersion;
        }

        private static string FormatEpisodeName(string episodeId)
        {
            return episodeId.Replace("nightwire.", string.Empty).Replace('.', ' ').Replace('_', ' ').ToUpperInvariant();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
        }
    }
}

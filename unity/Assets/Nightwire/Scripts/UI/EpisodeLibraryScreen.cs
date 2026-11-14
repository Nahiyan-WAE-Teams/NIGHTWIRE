using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nightwire.Runtime.Installation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nightwire.Presentation
{
    /// <summary>
    /// UI Toolkit episode library. The composition root injects a configured release manager;
    /// this view never owns signing keys, storage, transport, or the manager lifetime.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class EpisodeLibraryScreen : MonoBehaviour
    {
        [SerializeField] private string _title = "NIGHTWIRE";
        [SerializeField] private string _subtitle = "EPISODE ARCHIVE";
        private EpisodeReleaseManager _manager;
        private UIDocument _document;
        private VisualElement _root;
        private ScrollView _episodeList;
        private Label _status;
        private Button _refreshButton;
        private CancellationTokenSource _lifetime;
        private bool _busy;

        public event Action<string> OpenEpisodeRequested;

        public void Configure(EpisodeReleaseManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (_root != null) RenderLibrary();
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _lifetime = new CancellationTokenSource();
            BuildInterface();
            if (_manager != null) RenderLibrary();
            else SetStatus("Episode services are not configured.");
        }

        private void OnDestroy()
        {
            if (_lifetime != null)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
                _lifetime = null;
            }
        }

        private void BuildInterface()
        {
            _root = _document.rootVisualElement;
            _root.Clear();
            _root.AddToClassList("nightwire-library");

            var page = new VisualElement();
            page.AddToClassList("page");
            _root.Add(page);

            var header = new VisualElement();
            header.AddToClassList("header");
            page.Add(header);

            var brand = new Label(_title);
            brand.AddToClassList("brand");
            header.Add(brand);

            var subtitle = new Label(_subtitle);
            subtitle.AddToClassList("subtitle");
            header.Add(subtitle);

            var headingRow = new VisualElement();
            headingRow.AddToClassList("heading-row");
            page.Add(headingRow);

            var heading = new Label("Your episodes");
            heading.AddToClassList("section-title");
            headingRow.Add(heading);

            _refreshButton = new Button(RefreshClicked) { text = "REFRESH" };
            _refreshButton.AddToClassList("refresh-button");
            StyleActionButton(_refreshButton);
            headingRow.Add(_refreshButton);

            _status = new Label("Connect to the archive to check releases.");
            _status.AddToClassList("status");
            page.Add(_status);

            _episodeList = new ScrollView(ScrollViewMode.Vertical);
            _episodeList.AddToClassList("episode-list");
            page.Add(_episodeList);

            var footer = new Label("DOWNLOADED EPISODES CAN BE PLAYED OFFLINE");
            footer.AddToClassList("footer");
            page.Add(footer);

            ApplyStyles(_root);
        }

        private async void RefreshClicked()
        {
            if (_manager == null || _busy) return;
            SetBusy(true);
            SetStatus("Verifying release catalogue…");
            try
            {
                var result = await _manager.RefreshCatalogueAsync(DateTime.UtcNow, _lifetime.Token);
                if (result.Success)
                {
                    SetStatus("Catalogue verified.");
                    RenderLibrary();
                }
                else SetStatus("Catalogue unavailable: " + SafeMessage(result.Error));
            }
            catch (OperationCanceledException) { SetStatus("Refresh cancelled."); }
            catch (Exception exception) { SetStatus("Refresh failed: " + SafeMessage(exception.Message)); }
            finally { if (this != null) SetBusy(false); }
        }

        private void RenderLibrary()
        {
            if (_manager == null || _episodeList == null) return;
            _episodeList.Clear();
            var entries = _manager.GetLibrary();
            if (entries.Count == 0)
            {
                var empty = new Label("No episodes are listed yet. Refresh the catalogue to check for releases.");
                empty.AddToClassList("empty-state");
                _episodeList.Add(empty);
                return;
            }

            foreach (var entry in entries)
            {
                var card = new VisualElement();
                card.AddToClassList("episode-card");
                card.style.backgroundColor = new Color(0.055f, 0.07f, 0.13f);
                card.style.paddingLeft = 16;
                card.style.paddingRight = 16;
                card.style.paddingTop = 16;
                card.style.paddingBottom = 16;
                card.style.marginBottom = 12;
                card.style.borderTopLeftRadius = 10;
                card.style.borderTopRightRadius = 10;
                card.style.borderBottomLeftRadius = 10;
                card.style.borderBottomRightRadius = 10;
                _episodeList.Add(card);

                var titleRow = new VisualElement();
                titleRow.AddToClassList("card-title-row");
                card.Add(titleRow);

                var title = new Label(FormatEpisodeTitle(entry.EpisodeId));
                title.AddToClassList("episode-title");
                title.style.fontSize = 15;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleRow.Add(title);

                var version = new Label(entry.InstalledVersion != null
                    ? "INSTALLED  ·  " + entry.InstalledVersion
                    : entry.AvailableVersion != null ? "AVAILABLE  ·  " + entry.AvailableVersion : "LOCAL PACKAGE");
                version.AddToClassList("version");
                version.style.fontSize = 9;
                version.style.color = new Color(0.72f, 0.48f, 0.91f);
                titleRow.Add(version);

                var detail = new Label(StatusText(entry.Status));
                detail.AddToClassList("detail");
                detail.style.fontSize = 11;
                detail.style.color = new Color(0.64f, 0.69f, 0.8f);
                detail.style.marginTop = 8;
                detail.style.marginBottom = 12;
                card.Add(detail);

                if (entry.Status == EpisodeLibraryStatus.InstalledOffline ||
                    entry.Status == EpisodeLibraryStatus.InstalledButNotInCatalogue)
                {
                    var open = new Button(() => OpenEpisodeRequested?.Invoke(entry.EpisodeId))
                    {
                        text = "OPEN EPISODE"
                    };
                    open.AddToClassList("primary-button");
                    StyleActionButton(open);
                    card.Add(open);
                }
                else if (entry.Status == EpisodeLibraryStatus.AvailableToDownload ||
                         entry.Status == EpisodeLibraryStatus.UpdateAvailable)
                {
                    string id = entry.EpisodeId;
                    var progressLabel = new Label();
                    progressLabel.AddToClassList("progress-label");
                    var progressBar = new ProgressBar { lowValue = 0, highValue = 100, value = 0 };
                    progressBar.AddToClassList("download-progress");
                    progressBar.style.marginTop = 8;
                    var action = new Button(() => InstallClicked(id, progressLabel, progressBar))
                    {
                        text = entry.Status == EpisodeLibraryStatus.UpdateAvailable ? "UPDATE EPISODE" : "DOWNLOAD EPISODE"
                    };
                    action.AddToClassList("primary-button");
                    StyleActionButton(action);
                    card.Add(action);
                    card.Add(progressLabel);
                    card.Add(progressBar);
                }
                else
                {
                    var warning = new Label("This local package failed verification. It will not be opened.");
                    warning.AddToClassList("warning");
                    card.Add(warning);
                }
            }
        }

        private async void InstallClicked(string episodeId, Label progressLabel, ProgressBar progressBar)
        {
            if (_manager == null || _busy) return;
            SetBusy(true);
            progressLabel.text = "Preparing secure download…";
            progressBar.value = 0;
            var progress = new Progress<EpisodeDownloadProgress>(value =>
            {
                if (progressLabel == null || progressBar == null) return;
                if (value.TotalBytes.HasValue && value.TotalBytes.Value > 0)
                {
                    progressBar.value = (float)(100d * value.BytesReceived / value.TotalBytes.Value);
                    progressLabel.text = FormatBytes(value.BytesReceived) + " / " + FormatBytes(value.TotalBytes.Value);
                }
                else progressLabel.text = FormatBytes(value.BytesReceived) + " downloaded";
            });

            try
            {
                var result = await _manager.InstallOrUpdateAsync(episodeId, progress, _lifetime.Token);
                if (result.Success)
                {
                    progressBar.value = 100;
                    progressLabel.text = "Installed and verified. Available offline.";
                    SetStatus("Episode installation complete.");
                }
                else
                {
                    progressLabel.text = "Installation failed: " + SafeMessage(result.Error);
                    SetStatus("Could not install " + episodeId + ".");
                }
                RenderLibrary();
            }
            catch (OperationCanceledException)
            {
                progressLabel.text = "Download cancelled.";
                SetStatus("Episode operation cancelled.");
            }
            catch (Exception exception)
            {
                progressLabel.text = "Installation failed: " + SafeMessage(exception.Message);
                SetStatus("Episode operation failed.");
            }
            finally { if (this != null) SetBusy(false); }
        }

        private static void StyleActionButton(Button button)
        {
            button.style.backgroundColor = new Color(0.34f, 0.12f, 0.48f);
            button.style.color = new Color(0.97f, 0.96f, 1f);
            button.style.paddingTop = 10;
            button.style.paddingBottom = 10;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (_refreshButton != null) _refreshButton.SetEnabled(!busy);
            if (_episodeList != null)
            {
                foreach (var button in _episodeList.Query<Button>().ToList())
                    button.SetEnabled(!busy);
            }
        }

        private void SetStatus(string message)
        {
            if (_status != null) _status.text = message;
        }

        private static string StatusText(EpisodeLibraryStatus status)
        {
            switch (status)
            {
                case EpisodeLibraryStatus.InstalledOffline: return "Ready to play without a network connection.";
                case EpisodeLibraryStatus.UpdateAvailable: return "A newer verified release is available.";
                case EpisodeLibraryStatus.AvailableToDownload: return "Download this episode to play offline.";
                case EpisodeLibraryStatus.InstalledButNotInCatalogue: return "Installed locally. It is not in the latest catalogue.";
                default: return "Package verification failed.";
            }
        }

        private static string FormatEpisodeTitle(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown episode";
            return id.Replace('_', ' ').Replace('.', ' ').ToUpperInvariant();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.0") + " KB";
            return bytes + " B";
        }

        private static string SafeMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message) ? "Unknown error" : message;
        }

        private static void ApplyStyles(VisualElement root)
        {
            root.style.flexGrow = 1;
            root.style.backgroundColor = new Color(0.025f, 0.035f, 0.075f);
            root.style.color = new Color(0.91f, 0.93f, 0.98f);

            var page = root.Q<VisualElement>(className: "page");
            page.style.flexGrow = 1;
            page.style.paddingLeft = 22;
            page.style.paddingRight = 22;
            page.style.paddingTop = 28;
            page.style.paddingBottom = 18;

            var header = root.Q<VisualElement>(className: "header");
            header.style.marginBottom = 28;
            var brand = root.Q<Label>(className: "brand");
            brand.style.fontSize = 28;
            brand.style.unityFontStyleAndWeight = FontStyle.Bold;
            brand.style.letterSpacing = 4;
            brand.style.color = new Color(0.92f, 0.93f, 1f);
            var subtitle = root.Q<Label>(className: "subtitle");
            subtitle.style.fontSize = 10;
            subtitle.style.letterSpacing = 3;
            subtitle.style.color = new Color(0.72f, 0.39f, 0.91f);

            var heading = root.Q<Label>(className: "section-title");
            heading.style.fontSize = 20;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            var row = root.Q<VisualElement>(className: "heading-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;

            var status = root.Q<Label>(className: "status");
            status.style.color = new Color(0.62f, 0.68f, 0.79f);
            status.style.fontSize = 12;
            status.style.marginTop = 8;
            status.style.marginBottom = 14;

            var list = root.Q<ScrollView>(className: "episode-list");
            list.style.flexGrow = 1;
            var footer = root.Q<Label>(className: "footer");
            footer.style.fontSize = 9;
            footer.style.letterSpacing = 1.4f;
            footer.style.color = new Color(0.55f, 0.6f, 0.72f);
            footer.style.marginTop = 14;
            footer.style.unityTextAlign = TextAnchor.MiddleCenter;
        }
    }
}

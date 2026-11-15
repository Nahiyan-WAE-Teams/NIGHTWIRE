using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nightwire.Runtime.Domain;
using Nightwire.Unity;
using Nightwire.Runtime.Sessions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Nightwire.Unity.UI
{
    /// <summary>
    /// First playable messenger presentation. All authored dialogue, choices, and media references
    /// are read from the verified episode package; player progress remains in RuntimeSessionCoordinator.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MessengerScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private long maximumInlineImageBytes = 12582912;
        [SerializeField] private long maximumInlineMediaBytes = 268435456;

        private EpisodeSessionHost sessionHost;
        private RuntimeSessionCoordinator session;
        private VisualElement root;
        private ScrollView messageList;
        private Label contactName;
        private Label contactStatus;
        private Label episodeTitle;
        private VisualElement typingIndicator;
        private VisualElement choiceList;
        private Label responseHeading;
        private Label conversationStatus;
        private Button backButton;
        private string locale;
        private string activeConversationId;
        private bool bound;
        private bool isRendering;
        private readonly Dictionary<string, Texture2D> imageCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private AudioSource mediaAudioSource;
        private AudioClip activeAudioClip;
        private Coroutine audioLoadCoroutine;
        private string activeAudioAssetId;
        private Button activeAudioButton;
        private Label activeAudioStatus;
        private bool audioPaused;
        private VideoPlayer mediaVideoPlayer;
        private RenderTexture videoRenderTexture;
        private string activeVideoAssetId;
        private Button activeVideoButton;
        private Label activeVideoStatus;

        public event Action CloseRequested;

        public void Bind(EpisodeSessionHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            Unbind();
            sessionHost = host;
            session = host.ActiveSession;
            if (session == null || session.Package == null || session.Progress == null)
                throw new InvalidOperationException("An active episode session is required to bind the messenger.");

            locale = session.Package.Descriptor.DefaultLocale;
            if (document == null) document = GetComponent<UIDocument>();
            TryBindView();
        }

        public void SetVisible(bool visible)
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (document != null && document.rootVisualElement != null)
                document.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (session == null && document != null && document.rootVisualElement != null)
                document.rootVisualElement.style.display = DisplayStyle.None;
            TryBindView();
        }

        private void Start()
        {
            if (session == null)
                SetVisible(false);
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Update()
        {
            if (!bound || session == null || !session.IsActive || session.Progress == null)
                return;

            string currentId = session.Progress.ActiveConversationId ?? string.Empty;
            if (!string.Equals(currentId, activeConversationId, StringComparison.Ordinal))
            {
                ActivateCurrentConversation();
                RenderConversation();
                return;
            }

            if (string.IsNullOrEmpty(currentId) ||
                !session.Progress.ConversationProgress.TryGetValue(currentId, out var progress) ||
                progress.IsComplete || progress.WaitingChoiceMessageId != null)
                return;

            var tick = session.AdvanceConversation(Time.deltaTime);
            if (!tick.Success)
            {
                SetStatus(tick.Error ?? "Conversation could not advance.");
                return;
            }

            if (tick.NewlyDeliveredMessageIds.Count > 0)
            {
                foreach (string messageId in tick.NewlyDeliveredMessageIds)
                {
                    var read = session.MarkMessageRead(messageId);
                    if (!read.Success)
                        Debug.LogWarning("NIGHTWIRE message read: " + read.Error, this);
                }
                RenderConversation();
            }
        }

        private void TryBindView()
        {
            if (bound || session == null || document == null || document.rootVisualElement == null)
                return;

            root = document.rootVisualElement.Q<VisualElement>("messenger-root");
            messageList = root?.Q<ScrollView>("message-list");
            contactName = root?.Q<Label>("contact-name");
            contactStatus = root?.Q<Label>("contact-status");
            episodeTitle = root?.Q<Label>("episode-title");
            typingIndicator = root?.Q<VisualElement>("typing-indicator");
            choiceList = root?.Q<VisualElement>("choice-list");
            responseHeading = root?.Q<Label>("response-heading");
            conversationStatus = root?.Q<Label>("conversation-status");
            backButton = root?.Q<Button>("back-button");

            if (root == null || messageList == null || contactName == null || contactStatus == null ||
                episodeTitle == null || typingIndicator == null || choiceList == null ||
                responseHeading == null || conversationStatus == null || backButton == null)
            {
                Debug.LogError("NIGHTWIRE messenger UXML is missing a required named element.", this);
                return;
            }

            backButton.clicked += BackClicked;
            bound = true;
            ActivateCurrentConversation();
            RenderConversation();
        }

        public void UnbindForSessionClose()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (bound && backButton != null)
                backButton.clicked -= BackClicked;
            bound = false;
            session = null;
            sessionHost = null;
            root = null;
            messageList = null;
            contactName = null;
            contactStatus = null;
            episodeTitle = null;
            typingIndicator = null;
            choiceList = null;
            responseHeading = null;
            conversationStatus = null;
            backButton = null;
            activeConversationId = null;
            StopMediaPlayback();
            ClearImageCache();
        }

        private void ActivateCurrentConversation()
        {
            if (session == null || session.Progress == null || session.Package == null)
                return;

            activeConversationId = session.Progress.ActiveConversationId ?? string.Empty;
            if (activeConversationId.Length == 0)
                return;

            if (!session.Progress.ConversationProgress.ContainsKey(activeConversationId))
            {
                var begin = session.BeginConversation(activeConversationId);
                if (!begin.Success)
                    SetStatus(begin.Error ?? "Conversation could not begin.");
            }
        }

        private void RenderConversation()
        {
            if (!bound || isRendering || session == null || session.Package == null ||
                session.Progress == null || messageList == null)
                return;

            isRendering = true;
            try
            {
                string conversationId = session.Progress.ActiveConversationId ?? string.Empty;
                activeConversationId = conversationId;
                messageList.Clear();
                choiceList.Clear();

                episodeTitle.text = Localize(session.Package.Descriptor.TitleKey, session.Package.Descriptor.EpisodeId);
                if (conversationId.Length == 0 || !session.Package.Conversations.TryGetValue(conversationId, out var conversation))
                {
                    contactName.text = "NIGHTWIRE";
                    contactStatus.text = "LOCAL EPISODE ARCHIVE";
                    responseHeading.text = "NO ACTIVE CONVERSATION";
                    typingIndicator.style.display = DisplayStyle.None;
                    SetStatus("The episode has no conversation waiting to open.");
                    return;
                }

                string otherParticipant = FindOtherParticipant(conversation);
                contactName.text = CharacterName(otherParticipant);
                contactStatus.text = CharacterStatus(otherParticipant);
                var progress = session.Progress.ConversationProgress.TryGetValue(conversationId, out var current)
                    ? current : null;

                if (conversation.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in messages.EnumerateArray())
                    {
                        string messageId = ReadString(message, "message_id");
                        if (progress == null || !progress.DeliveredMessageIds.Contains(messageId))
                            continue;

                        messageList.Add(BuildMessage(message));
                        if (!progress.ReadMessageIds.Contains(messageId))
                        {
                            var read = session.MarkMessageRead(messageId);
                            if (!read.Success)
                                Debug.LogWarning("NIGHTWIRE message read: " + read.Error, this);
                        }

                        if (progress.WaitingChoiceMessageId == messageId)
                            BuildChoices(message);
                    }
                }

                bool waitingChoice = progress != null && progress.WaitingChoiceMessageId != null;
                bool complete = progress != null && progress.IsComplete;
                bool waitingMessage = progress != null && !complete && !waitingChoice && progress.NextMessageId != null;
                typingIndicator.style.display = waitingMessage ? DisplayStyle.Flex : DisplayStyle.None;
                responseHeading.text = waitingChoice ? "CHOOSE YOUR RESPONSE" : "STORY RESPONSE";
                SetStatus(waitingChoice ? "Your choice will shape what happens next."
                    : complete ? "END OF CONVERSATION"
                    : waitingMessage ? "Waiting for the next transmission…"
                    : "Conversation synchronized.");
                if (messageList.childCount > 0)
                    messageList.ScrollTo(messageList[messageList.childCount - 1]);
            }
            finally
            {
                isRendering = false;
            }
        }

        private VisualElement BuildMessage(JsonElement message)
        {
            string senderId = ReadString(message, "sender_id");
            bool outgoing = string.Equals(senderId, "player", StringComparison.Ordinal);
            string type = ReadString(message, "type");

            var row = new VisualElement();
            row.AddToClassList("message-row");
            row.AddToClassList(outgoing ? "message-row-outgoing" : "message-row-incoming");

            var bubble = new VisualElement();
            bubble.AddToClassList("message-bubble");
            if (outgoing) bubble.AddToClassList("message-bubble-outgoing");

            if (!outgoing)
            {
                var sender = new Label(CharacterName(senderId));
                sender.AddToClassList("message-sender");
                bubble.Add(sender);
            }

            string textKey = type == "choice" ? ReadString(message, "prompt_key") : ReadString(message, "text_key");
            string text = textKey.Length > 0 ? Localize(textKey, string.Empty) : string.Empty;
            if (text.Length > 0)
            {
                var body = new Label(text);
                body.AddToClassList(type == "choice" ? "choice-prompt" : "message-body");
                bubble.Add(body);
            }

            if (type == "image")
                AddImageAttachment(bubble, ReadString(message, "asset_id"));
            else if (type == "audio")
                AddAudioAttachment(bubble, ReadString(message, "asset_id"));
            else if (type == "video")
                AddVideoAttachment(bubble, ReadString(message, "asset_id"));
            else if (type == "document")
            {
                var attachment = new Label("DOCUMENT ATTACHMENT · STORED IN EPISODE");
                attachment.AddToClassList("message-media-note");
                bubble.Add(attachment);
            }
            else if (type == "call_invitation")
            {
                var call = new Label("INCOMING CALL · " + ReadString(message, "call_id"));
                call.AddToClassList("message-media-note");
                bubble.Add(call);
            }

            if (text.Length == 0 && type != "image" && type != "audio" && type != "video" &&
                type != "document" && type != "call_invitation")
            {
                var fallback = new Label("[" + type.ToUpperInvariant() + " MESSAGE]");
                fallback.AddToClassList("message-body");
                bubble.Add(fallback);
            }

            var time = new Label(outgoing ? "YOU" : "DELIVERED");
            time.AddToClassList("message-time");
            bubble.Add(time);
            row.Add(bubble);
            return row;
        }

        private void AddAudioAttachment(VisualElement bubble, string assetId)
        {
            var player = new VisualElement();
            player.AddToClassList("message-media-player");
            var status = new Label("VOICE MESSAGE · READY");
            status.AddToClassList("message-media-status");
            var button = new Button { text = "PLAY AUDIO" };
            button.AddToClassList("message-media-control");
            button.clicked += () => ToggleAudio(assetId, button, status);
            player.Add(status);
            player.Add(button);
            bubble.Add(player);
        }

        private void AddVideoAttachment(VisualElement bubble, string assetId)
        {
            var player = new VisualElement();
            player.AddToClassList("message-media-player");
            var preview = new Image();
            preview.AddToClassList("message-video-preview");
            preview.scaleMode = ScaleMode.ScaleToFit;
            var status = new Label("VIDEO MESSAGE · READY");
            status.AddToClassList("message-media-status");
            var button = new Button { text = "PLAY VIDEO" };
            button.AddToClassList("message-media-control");
            button.clicked += () => ToggleVideo(assetId, preview, button, status);
            player.Add(preview);
            player.Add(status);
            player.Add(button);
            bubble.Add(player);
        }

        private void ToggleAudio(string assetId, Button button, Label status)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                status.text = "AUDIO REFERENCE MISSING";
                return;
            }

            EnsureAudioSource();
            if (string.Equals(activeAudioAssetId, assetId, StringComparison.Ordinal) && activeAudioClip != null)
            {
                if (mediaAudioSource.isPlaying)
                {
                    mediaAudioSource.Pause();
                    audioPaused = true;
                    button.text = "RESUME AUDIO";
                    status.text = "PAUSED";
                }
                else if (audioPaused)
                {
                    mediaAudioSource.UnPause();
                    audioPaused = false;
                    button.text = "PAUSE AUDIO";
                    status.text = "PLAYING";
                }
                else
                {
                    mediaAudioSource.time = 0;
                    mediaAudioSource.Play();
                    button.text = "PAUSE AUDIO";
                    status.text = "PLAYING";
                }
                return;
            }

            if (audioLoadCoroutine != null) StopCoroutine(audioLoadCoroutine);
            mediaAudioSource.Stop();
            activeAudioAssetId = assetId;
            activeAudioButton = button;
            activeAudioStatus = status;
            activeAudioClip = null;
            audioPaused = false;
            button.SetEnabled(false);
            status.text = "VERIFYING AUDIO…";
            audioLoadCoroutine = StartCoroutine(LoadAndPlayAudio(assetId, button, status));
        }

        private System.Collections.IEnumerator LoadAndPlayAudio(string assetId, Button button, Label status)
        {
            string path;
            try { path = CacheVerifiedMediaAsset(assetId); }
            catch (Exception exception)
            {
                status.text = "AUDIO UNAVAILABLE · " + exception.Message;
                button.SetEnabled(true);
                audioLoadCoroutine = null;
                yield break;
            }

            string url = new Uri(path).AbsoluteUri;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, GetAudioType(path)))
            {
                request.timeout = 60;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    status.text = "AUDIO DECODE FAILED · " + request.error;
                    button.SetEnabled(true);
                    audioLoadCoroutine = null;
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    status.text = "AUDIO DECODE FAILED";
                    button.SetEnabled(true);
                    audioLoadCoroutine = null;
                    yield break;
                }

                if (activeAudioClip != null) Destroy(activeAudioClip);
                activeAudioClip = clip;
                activeAudioClip.name = assetId;
                mediaAudioSource.clip = activeAudioClip;
                mediaAudioSource.Play();
                button.SetEnabled(true);
                button.text = "PAUSE AUDIO";
                status.text = "PLAYING";
                audioLoadCoroutine = null;
            }
        }

        private void ToggleVideo(string assetId, Image preview, Button button, Label status)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                status.text = "VIDEO REFERENCE MISSING";
                return;
            }

            EnsureVideoPlayer();
            if (string.Equals(activeVideoAssetId, assetId, StringComparison.Ordinal) &&
                mediaVideoPlayer.isPrepared)
            {
                if (mediaVideoPlayer.isPlaying)
                {
                    mediaVideoPlayer.Pause();
                    button.text = "RESUME VIDEO";
                    status.text = "PAUSED";
                }
                else
                {
                    mediaVideoPlayer.Play();
                    button.text = "PAUSE VIDEO";
                    status.text = "PLAYING";
                }
                return;
            }

            string path;
            try { path = CacheVerifiedMediaAsset(assetId); }
            catch (Exception exception)
            {
                status.text = "VIDEO UNAVAILABLE · " + exception.Message;
                return;
            }

            mediaVideoPlayer.Stop();
            activeVideoAssetId = assetId;
            activeVideoButton = button;
            activeVideoStatus = status;
            preview.image = videoRenderTexture;
            button.SetEnabled(false);
            button.text = "PREPARING VIDEO";
            status.text = "PREPARING VIDEO…";
            mediaVideoPlayer.url = new Uri(path).AbsoluteUri;
            mediaVideoPlayer.Prepare();
        }

        private void EnsureAudioSource()
        {
            if (mediaAudioSource == null)
                mediaAudioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            mediaAudioSource.playOnAwake = false;
            mediaAudioSource.loop = false;
        }

        private void EnsureVideoPlayer()
        {
            EnsureAudioSource();
            if (videoRenderTexture == null)
            {
                videoRenderTexture = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32);
                videoRenderTexture.Create();
            }
            if (mediaVideoPlayer == null)
            {
                mediaVideoPlayer = GetComponent<VideoPlayer>() ?? gameObject.AddComponent<VideoPlayer>();
                mediaVideoPlayer.playOnAwake = false;
                mediaVideoPlayer.isLooping = false;
                mediaVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
                mediaVideoPlayer.targetTexture = videoRenderTexture;
                mediaVideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
                mediaVideoPlayer.EnableAudioTrack(0, true);
                mediaVideoPlayer.SetTargetAudioSource(0, mediaAudioSource);
                mediaVideoPlayer.prepareCompleted += VideoPrepared;
                mediaVideoPlayer.loopPointReached += VideoCompleted;
                mediaVideoPlayer.errorReceived += VideoFailed;
            }
        }

        private void VideoPrepared(VideoPlayer player)
        {
            if (player != mediaVideoPlayer) return;
            player.Play();
            if (activeVideoButton != null)
            {
                activeVideoButton.SetEnabled(true);
                activeVideoButton.text = "PAUSE VIDEO";
            }
            if (activeVideoStatus != null) activeVideoStatus.text = "PLAYING";
        }

        private void VideoCompleted(VideoPlayer player)
        {
            if (player != mediaVideoPlayer) return;
            if (activeVideoButton != null) activeVideoButton.text = "REPLAY VIDEO";
            if (activeVideoStatus != null) activeVideoStatus.text = "PLAYBACK COMPLETE";
        }

        private void VideoFailed(VideoPlayer player, string message)
        {
            if (player != mediaVideoPlayer) return;
            if (activeVideoButton != null)
            {
                activeVideoButton.SetEnabled(true);
                activeVideoButton.text = "RETRY VIDEO";
            }
            if (activeVideoStatus != null) activeVideoStatus.text = "VIDEO PLAYBACK FAILED · " + message;
        }

        private string CacheVerifiedMediaAsset(string assetId)
        {
            if (session == null || session.Package == null ||
                !session.Package.AssetsById.TryGetValue(assetId, out var metadata))
                throw new InvalidOperationException("The media asset is not part of the active episode manifest.");

            var opened = session.OpenAsset(assetId);
            if (!opened.Success || opened.Asset == null)
                throw new InvalidOperationException(opened.Error ?? "The media asset is locked or unavailable.");

            using (opened.Asset)
            {
                if (opened.Asset.SizeBytes <= 0 || opened.Asset.SizeBytes > maximumInlineMediaBytes)
                    throw new InvalidOperationException("The media asset exceeds the inline playback size limit.");

                string extension = Path.GetExtension(metadata.Path);
                if (extension.Length > 10 || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    extension = string.Empty;

                byte[] identity = Encoding.UTF8.GetBytes(assetId);
                string fileId;
                using (var sha = SHA256.Create())
                    fileId = BitConverter.ToString(sha.ComputeHash(identity)).Replace("-", string.Empty).ToLowerInvariant();

                string directory = Path.Combine(Application.temporaryCachePath, "NightwireMedia");
                Directory.CreateDirectory(directory);
                string destination = Path.Combine(directory, fileId + extension);
                string temporary = destination + ".tmp";
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long copied = 0;
                    int read;
                    while ((read = opened.Asset.Content.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        copied += read;
                        if (copied > maximumInlineMediaBytes)
                            throw new InvalidDataException("The media asset exceeded its playback size limit while being copied.");
                        output.Write(buffer, 0, read);
                    }
                    output.Flush();
                }

                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                return destination;
            }
        }

        private static AudioType GetAudioType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                case ".m4a":
                case ".aac": return AudioType.ACC;
                default: return AudioType.UNKNOWN;
            }
        }

        private void StopMediaPlayback()
        {
            if (audioLoadCoroutine != null)
            {
                StopCoroutine(audioLoadCoroutine);
                audioLoadCoroutine = null;
            }
            if (mediaAudioSource != null) mediaAudioSource.Stop();
            if (activeAudioClip != null)
            {
                Destroy(activeAudioClip);
                activeAudioClip = null;
            }
            activeAudioAssetId = null;
            activeAudioButton = null;
            activeAudioStatus = null;
            audioPaused = false;

            if (mediaVideoPlayer != null) mediaVideoPlayer.Stop();
            activeVideoAssetId = null;
            activeVideoButton = null;
            activeVideoStatus = null;
            if (videoRenderTexture != null)
            {
                videoRenderTexture.Release();
                Destroy(videoRenderTexture);
                videoRenderTexture = null;
            }
        }

        private void AddImageAttachment(VisualElement bubble, string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                bubble.Add(new Label("IMAGE REFERENCE MISSING"));
                return;
            }

            if (!imageCache.TryGetValue(assetId, out var texture))
            {
                var opened = session.OpenAsset(assetId);
                if (!opened.Success || opened.Asset == null)
                {
                    var unavailable = new Label("IMAGE UNAVAILABLE · " + assetId);
                    unavailable.AddToClassList("message-media-note");
                    bubble.Add(unavailable);
                    return;
                }

                using (opened.Asset)
                {
                    if (opened.Asset.SizeBytes > maximumInlineImageBytes)
                    {
                        var tooLarge = new Label("IMAGE STORED IN EPISODE · TOO LARGE FOR INLINE PREVIEW");
                        tooLarge.AddToClassList("message-media-note");
                        bubble.Add(tooLarge);
                        return;
                    }

                    using (var memory = new MemoryStream())
                    {
                        opened.Asset.Content.CopyTo(memory);
                        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(memory.ToArray(), true))
                        {
                            Destroy(texture);
                            var invalid = new Label("IMAGE FORMAT NOT SUPPORTED");
                            invalid.AddToClassList("message-media-note");
                            bubble.Add(invalid);
                            return;
                        }
                    }
                }
                imageCache.Add(assetId, texture);
            }

            var image = new UnityEngine.UIElements.Image
            {
                image = texture,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("message-image");
            bubble.Add(image);
        }

        private void BuildChoices(JsonElement message)
        {
            if (!message.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
                return;

            foreach (var option in options.EnumerateArray())
            {
                string optionId = ReadString(option, "option_id");
                string textKey = ReadString(option, "text_key");
                var button = new Button { text = Localize(textKey, optionId) };
                button.AddToClassList("choice-button");
                string capturedOption = optionId;
                button.clicked += () => ChooseOption(capturedOption);
                choiceList.Add(button);
            }
        }

        private void ChooseOption(string optionId)
        {
            if (session == null) return;
            var result = session.SelectChoice(optionId);
            if (!result.Success)
            {
                SetStatus(result.Error ?? "Choice could not be selected.");
                return;
            }
            RenderConversation();
        }

        private void BackClicked()
        {
            CloseRequested?.Invoke();
        }

        private string FindOtherParticipant(JsonElement conversation)
        {
            if (conversation.TryGetProperty("participants", out var participants) &&
                participants.ValueKind == JsonValueKind.Array)
            {
                foreach (var participant in participants.EnumerateArray())
                {
                    if (participant.ValueKind != JsonValueKind.String) continue;
                    string id = participant.GetString() ?? string.Empty;
                    if (!string.Equals(id, "player", StringComparison.Ordinal))
                        return id;
                }
            }
            return "nightwire";
        }

        private string CharacterName(string characterId)
        {
            if (session?.Package == null || !session.Package.CharacterProfiles.TryGetValue(characterId, out var profile))
                return characterId.Replace('_', ' ').ToUpperInvariant();
            string key = ReadString(profile, "display_name_key");
            return Localize(key, characterId);
        }

        private string CharacterStatus(string characterId)
        {
            if (session?.Package == null || !session.Package.CharacterProfiles.TryGetValue(characterId, out var profile))
                return "LOCAL STORY CHANNEL";
            return Localize(ReadString(profile, "status_key"), "LOCAL STORY CHANNEL");
        }

        private string Localize(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key) || session?.Package == null)
                return fallback;
            if (session.Package.Localization.TryGetValue(locale, out var dictionary) &&
                dictionary.TryGetValue(key, out var value))
                return value;
            if (session.Package.Localization.TryGetValue(session.Package.Descriptor.DefaultLocale, out dictionary) &&
                dictionary.TryGetValue(key, out value))
                return value;
            return fallback;
        }

        private static string ReadString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(property, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private void SetStatus(string value)
        {
            if (conversationStatus != null)
                conversationStatus.text = value;
        }

        private void ClearImageCache()
        {
            foreach (var texture in imageCache.Values)
                if (texture != null) Destroy(texture);
            imageCache.Clear();
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}

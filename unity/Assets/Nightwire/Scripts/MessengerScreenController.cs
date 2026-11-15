using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
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
        private ScrollView socialFeedList;
        private ScrollView evidenceList;
        private VisualElement responseDock;
        private Button messagesTabButton;
        private Button feedTabButton;
        private Button evidenceTabButton;
        private Label contactName;
        private Label contactStatus;
        private Label groupParticipants;
        private Label episodeTitle;
        private VisualElement typingIndicator;
        private VisualElement choiceList;
        private Label responseHeading;
        private Label conversationStatus;
        private Button backButton;
        private Button inboxOpenButton;
        private Button inboxCloseButton;
        private VisualElement conversationInbox;
        private ScrollView conversationInboxList;
        private Button profileOpenButton;
        private Button contactsOpenButton;
        private Button contactsCloseButton;
        private ScrollView contactsList;
        private VisualElement contactsOverlay;
        private Button profileCloseButton;
        private VisualElement profileOverlay;
        private Label profileName;
        private Label profileStatus;
        private Label profileBio;
        private VisualElement callScreen;
        private Label callKicker;
        private Label callContact;
        private Image callVideoSurface;
        private Label callAudioAvatar;
        private Label callEffectOverlay;
        private Label callSpeaker;
        private Label callSubtitle;
        private Label callTimer;
        private Label callStatus;
        private Button callRetryButton;
        private Button minigameEntryButton;
        private VisualElement minigameScreen;
        private Label minigameTitle;
        private Label minigameInstructions;
        private Label minigameProgress;
        private VisualElement minigameChallengeContent;
        private Label minigameFeedback;
        private Button minigameContinueButton;
        private string activeMinigameId;
        private double minigameTickAccumulator;
        private readonly List<string> selectedSequence = new List<string>();
        private string selectedEvidenceItemId;
        private string activeContactId;
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
        private string activeCallId;
        private bool callMediaReady;
        private double callTickAccumulatorSeconds;
        private float callEffectRemainingSeconds;
        private long callLastMediaPositionMilliseconds;
        private bool callUsesVideo;
        private bool callPausedForApp;
        private AudioSource callAudioSource;
        private AudioClip callAudioClip;
        private Coroutine callAudioLoadCoroutine;
        private VideoPlayer callVideoPlayer;
        private RenderTexture callVideoTexture;

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
            UpdateMediaPlayback();
            UpdateCallPlayback();
            UpdateMinigamePlayback();
            if (!string.IsNullOrEmpty(activeCallId)) return;
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
            socialFeedList = root?.Q<ScrollView>("social-feed-list");
            evidenceList = root?.Q<ScrollView>("evidence-list");
            responseDock = root?.Q<VisualElement>("response-dock");
            messagesTabButton = root?.Q<Button>("messages-tab");
            feedTabButton = root?.Q<Button>("feed-tab");
            evidenceTabButton = root?.Q<Button>("evidence-tab");
            contactName = root?.Q<Label>("contact-name");
            contactStatus = root?.Q<Label>("contact-status");
            groupParticipants = root?.Q<Label>("group-participants");
            episodeTitle = root?.Q<Label>("episode-title");
            typingIndicator = root?.Q<VisualElement>("typing-indicator");
            choiceList = root?.Q<VisualElement>("choice-list");
            responseHeading = root?.Q<Label>("response-heading");
            conversationStatus = root?.Q<Label>("conversation-status");
            backButton = root?.Q<Button>("back-button");
            inboxOpenButton = root?.Q<Button>("inbox-open-button");
            inboxCloseButton = root?.Q<Button>("inbox-close-button");
            conversationInbox = root?.Q<VisualElement>("conversation-inbox");
            conversationInboxList = root?.Q<ScrollView>("conversation-inbox-list");
            profileOpenButton = root?.Q<Button>("profile-open-button");
            contactsOpenButton = root?.Q<Button>("contacts-open-button");
            contactsCloseButton = root?.Q<Button>("contacts-close-button");
            contactsList = root?.Q<ScrollView>("contacts-list");
            contactsOverlay = root?.Q<VisualElement>("contacts-overlay");
            profileCloseButton = root?.Q<Button>("profile-close-button");
            profileOverlay = root?.Q<VisualElement>("profile-overlay");
            profileName = root?.Q<Label>("profile-name");
            profileStatus = root?.Q<Label>("profile-status");
            profileBio = root?.Q<Label>("profile-bio");
            callScreen = root?.Q<VisualElement>("call-screen");
            callKicker = root?.Q<Label>("call-kicker");
            callContact = root?.Q<Label>("call-contact");
            callVideoSurface = root?.Q<Image>("call-video-surface");
            callAudioAvatar = root?.Q<Label>("call-audio-avatar");
            callEffectOverlay = root?.Q<Label>("call-effect-overlay");
            callSpeaker = root?.Q<Label>("call-speaker");
            callSubtitle = root?.Q<Label>("call-subtitle");
            callTimer = root?.Q<Label>("call-timer");
            callStatus = root?.Q<Label>("call-status");
            callRetryButton = root?.Q<Button>("call-retry-button");
            minigameEntryButton = root?.Q<Button>("minigame-entry-button");
            minigameScreen = root?.Q<VisualElement>("minigame-screen");
            minigameTitle = root?.Q<Label>("minigame-title");
            minigameInstructions = root?.Q<Label>("minigame-instructions");
            minigameProgress = root?.Q<Label>("minigame-progress");
            minigameChallengeContent = root?.Q<VisualElement>("minigame-challenge-content");
            minigameFeedback = root?.Q<Label>("minigame-feedback");
            minigameContinueButton = root?.Q<Button>("minigame-continue-button");

            if (root == null || messageList == null || socialFeedList == null || evidenceList == null ||
                responseDock == null || messagesTabButton == null || feedTabButton == null ||
                evidenceTabButton == null || contactName == null || contactStatus == null ||
                groupParticipants == null ||
                episodeTitle == null || typingIndicator == null || choiceList == null ||
                responseHeading == null || conversationStatus == null || backButton == null ||
                profileOpenButton == null || contactsOpenButton == null || contactsCloseButton == null ||
                contactsList == null || contactsOverlay == null || profileCloseButton == null || profileOverlay == null ||
                profileName == null || profileStatus == null || profileBio == null || inboxOpenButton == null || inboxCloseButton == null ||
                conversationInbox == null || conversationInboxList == null ||
                callScreen == null || callKicker == null || callContact == null ||
                callVideoSurface == null || callAudioAvatar == null || callEffectOverlay == null ||
                callSpeaker == null || callSubtitle == null || callTimer == null ||
                callStatus == null || callRetryButton == null ||
                minigameEntryButton == null || minigameScreen == null || minigameTitle == null ||
                minigameInstructions == null || minigameProgress == null ||
                minigameChallengeContent == null || minigameFeedback == null || minigameContinueButton == null)
            {
                Debug.LogError("NIGHTWIRE messenger UXML is missing a required named element.", this);
                return;
            }

            backButton.clicked += BackClicked;
            inboxOpenButton.clicked += ShowConversationInbox;
            inboxCloseButton.clicked += HideConversationInbox;
            messagesTabButton.clicked += ShowMessagesTab;
            feedTabButton.clicked += ShowFeedTab;
            evidenceTabButton.clicked += ShowEvidenceTab;
            profileOpenButton.clicked += ShowContactProfile;
            contactsOpenButton.clicked += ShowContactDirectory;
            contactsCloseButton.clicked += HideContactDirectory;
            profileCloseButton.clicked += HideContactProfile;
            callRetryButton.clicked += RetryCallMedia;
            minigameEntryButton.clicked += StartPendingMinigame;
            minigameContinueButton.clicked += CloseMinigamePresentation;
            bound = true;
            ShowMessagesTab();
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
            if (bound && inboxOpenButton != null)
                inboxOpenButton.clicked -= ShowConversationInbox;
            if (bound && inboxCloseButton != null)
                inboxCloseButton.clicked -= HideConversationInbox;
            if (bound && messagesTabButton != null)
                messagesTabButton.clicked -= ShowMessagesTab;
            if (bound && feedTabButton != null)
                feedTabButton.clicked -= ShowFeedTab;
            if (bound && evidenceTabButton != null)
                evidenceTabButton.clicked -= ShowEvidenceTab;
            if (bound && profileOpenButton != null)
                profileOpenButton.clicked -= ShowContactProfile;
            if (bound && contactsOpenButton != null)
                contactsOpenButton.clicked -= ShowContactDirectory;
            if (bound && contactsCloseButton != null)
                contactsCloseButton.clicked -= HideContactDirectory;
            if (bound && profileCloseButton != null)
                profileCloseButton.clicked -= HideContactProfile;
            if (bound && callRetryButton != null)
                callRetryButton.clicked -= RetryCallMedia;
            if (bound && minigameEntryButton != null)
                minigameEntryButton.clicked -= StartPendingMinigame;
            if (bound && minigameContinueButton != null)
                minigameContinueButton.clicked -= CloseMinigamePresentation;
            bound = false;
            session = null;
            sessionHost = null;
            root = null;
            messageList = null;
            socialFeedList = null;
            evidenceList = null;
            responseDock = null;
            messagesTabButton = null;
            feedTabButton = null;
            evidenceTabButton = null;
            contactName = null;
            contactStatus = null;
            groupParticipants = null;
            episodeTitle = null;
            typingIndicator = null;
            choiceList = null;
            responseHeading = null;
            conversationStatus = null;
            backButton = null;
            inboxOpenButton = null;
            inboxCloseButton = null;
            conversationInbox = null;
            conversationInboxList = null;
            profileOpenButton = null;
            contactsOpenButton = null;
            contactsCloseButton = null;
            contactsList = null;
            contactsOverlay = null;
            profileCloseButton = null;
            profileOverlay = null;
            profileName = null;
            profileStatus = null;
            profileBio = null;
            callScreen = null;
            callKicker = null;
            callContact = null;
            callVideoSurface = null;
            callAudioAvatar = null;
            callEffectOverlay = null;
            callSpeaker = null;
            callSubtitle = null;
            callTimer = null;
            callStatus = null;
            callRetryButton = null;
            minigameEntryButton = null;
            minigameScreen = null;
            minigameTitle = null;
            minigameInstructions = null;
            minigameProgress = null;
            minigameChallengeContent = null;
            minigameFeedback = null;
            minigameContinueButton = null;
            activeContactId = null;
            activeConversationId = null;
            StopMediaPlayback();
            StopCallMedia();
            activeMinigameId = null;
            selectedSequence.Clear();
            selectedEvidenceItemId = null;
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

        private void ShowMessagesTab()
        {
            SetActiveTab("messages");
            if (messageList != null) messageList.style.display = DisplayStyle.Flex;
            if (socialFeedList != null) socialFeedList.style.display = DisplayStyle.None;
            if (evidenceList != null) evidenceList.style.display = DisplayStyle.None;
            if (responseDock != null) responseDock.style.display = DisplayStyle.Flex;
            if (typingIndicator != null) typingIndicator.style.display = DisplayStyle.Flex;
        }

        private void ShowFeedTab()
        {
            SetActiveTab("feed");
            if (messageList != null) messageList.style.display = DisplayStyle.None;
            if (socialFeedList != null) socialFeedList.style.display = DisplayStyle.Flex;
            if (evidenceList != null) evidenceList.style.display = DisplayStyle.None;
            if (responseDock != null) responseDock.style.display = DisplayStyle.None;
            if (typingIndicator != null) typingIndicator.style.display = DisplayStyle.None;
            RenderSocialFeed();
        }

        private void ShowEvidenceTab()
        {
            SetActiveTab("evidence");
            if (messageList != null) messageList.style.display = DisplayStyle.None;
            if (socialFeedList != null) socialFeedList.style.display = DisplayStyle.None;
            if (evidenceList != null) evidenceList.style.display = DisplayStyle.Flex;
            if (responseDock != null) responseDock.style.display = DisplayStyle.None;
            if (typingIndicator != null) typingIndicator.style.display = DisplayStyle.None;
            RenderEvidenceLibrary();
        }

        private void SetActiveTab(string active)
        {
            messagesTabButton?.EnableInClassList("messenger-tab-active", active == "messages");
            feedTabButton?.EnableInClassList("messenger-tab-active", active == "feed");
            evidenceTabButton?.EnableInClassList("messenger-tab-active", active == "evidence");
        }

        private void RenderSocialFeed()
        {
            if (socialFeedList == null || session?.Package == null) return;
            socialFeedList.Clear();
            var posts = session.Package.CharacterPosts.Values
                .OrderBy(post => ReadString(post, "published_at"), StringComparer.Ordinal)
                .ToArray();
            if (posts.Length == 0)
            {
                var empty = new Label("NO SOCIAL ACTIVITY IN THIS EPISODE YET.");
                empty.AddToClassList("evidence-empty");
                socialFeedList.Add(empty);
                return;
            }

            foreach (var post in posts)
            {
                var card = new VisualElement();
                card.AddToClassList("feed-card");
                string authorId = ReadString(post, "author_id");
                var author = new Label(CharacterName(authorId));
                author.AddToClassList("feed-author");
                card.Add(author);
                var date = new Label(ReadString(post, "published_at"));
                date.AddToClassList("feed-date");
                card.Add(date);
                string bodyKey = ReadString(post, "body_key");
                var body = new Label(Localize(bodyKey, "Post content unavailable."));
                body.AddToClassList("feed-body");
                card.Add(body);

                if (post.TryGetProperty("media_asset_ids", out var mediaIds) &&
                    mediaIds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mediaId in mediaIds.EnumerateArray())
                    {
                        if (mediaId.ValueKind != JsonValueKind.String) continue;
                        string assetId = mediaId.GetString() ?? string.Empty;
                        if (assetId.Length == 0) continue;
                        if (session.Package.AssetsById.TryGetValue(assetId, out var asset) &&
                            string.Equals(asset.Type, "image", StringComparison.OrdinalIgnoreCase))
                            AddImageAttachment(card, assetId);
                        else
                        {
                            var mediaNote = new Label(session.Progress.UnlockedAssetIds.Contains(assetId)
                                ? "ATTACHMENT · " + assetId : "LOCKED ATTACHMENT");
                            mediaNote.AddToClassList("message-media-note");
                            card.Add(mediaNote);
                        }
                    }
                }

                if (post.TryGetProperty("comments", out var comments) && comments.ValueKind == JsonValueKind.Array)
                {
                    foreach (var comment in comments.EnumerateArray())
                    {
                        string commentAuthor = CharacterName(ReadString(comment, "author_id"));
                        string commentText = Localize(ReadString(comment, "body_key"), string.Empty);
                        if (commentText.Length == 0) continue;
                        var line = new Label(commentAuthor + "  ·  " + commentText);
                        line.AddToClassList("feed-comment");
                        card.Add(line);
                    }
                }
                socialFeedList.Add(card);
            }
        }

        private void RenderEvidenceLibrary()
        {
            if (evidenceList == null || session?.Package == null || session.Progress == null) return;
            evidenceList.Clear();
            var unlocked = session.Package.AssetsById.Values
                .Where(asset => session.Progress.UnlockedAssetIds.Contains(asset.AssetId))
                .OrderBy(asset => asset.AssetId, StringComparer.Ordinal)
                .ToArray();
            if (unlocked.Length == 0)
            {
                var empty = new Label("NO EVIDENCE UNLOCKED YET. KEEP FOLLOWING THE STORY.");
                empty.AddToClassList("evidence-empty");
                evidenceList.Add(empty);
                return;
            }

            foreach (var asset in unlocked)
            {
                var card = new VisualElement();
                card.AddToClassList("evidence-card");
                var title = new Label(asset.AssetId.Replace('_', ' ').ToUpperInvariant());
                title.AddToClassList("evidence-title");
                card.Add(title);
                var meta = new Label(asset.Type.ToUpperInvariant() + "  ·  " + asset.MimeType);
                meta.AddToClassList("evidence-meta");
                card.Add(meta);

                if (string.Equals(asset.Type, "image", StringComparison.OrdinalIgnoreCase))
                    AddImageAttachment(card, asset.AssetId);
                else if (string.Equals(asset.Type, "audio", StringComparison.OrdinalIgnoreCase))
                    AddAudioAttachment(card, asset.AssetId);
                else if (string.Equals(asset.Type, "video", StringComparison.OrdinalIgnoreCase))
                    AddVideoAttachment(card, asset.AssetId);
                else if (string.Equals(asset.Type, "document", StringComparison.OrdinalIgnoreCase))
                    AddDocumentAttachment(card, asset.AssetId, asset.Path);
                else
                {
                    var note = new Label("This evidence type is stored in the verified episode archive.");
                    note.AddToClassList("message-media-note");
                    card.Add(note);
                }
                evidenceList.Add(card);
            }
        }

        private void AddDocumentAttachment(VisualElement container, string assetId, string relativePath)
        {
            var opened = session?.OpenAsset(assetId);
            if (opened == null || !opened.Success || opened.Asset == null)
            {
                var unavailable = new Label("DOCUMENT UNAVAILABLE · " + assetId);
                unavailable.AddToClassList("message-media-note");
                container.Add(unavailable);
                return;
            }

            using (opened.Asset)
            {
                string extension = Path.GetExtension(relativePath).ToLowerInvariant();
                bool textDocument = extension == ".txt" || extension == ".md" ||
                    extension == ".json" || opened.Asset.MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
                if (!textDocument || opened.Asset.SizeBytes > 1048576)
                {
                    var note = new Label("VERIFIED DOCUMENT · " + Path.GetFileName(relativePath) +
                        " · " + opened.Asset.SizeBytes.ToString() + " BYTES");
                    note.AddToClassList("message-media-note");
                    container.Add(note);
                    return;
                }

                using (var reader = new StreamReader(opened.Asset.Content, Encoding.UTF8, true, 4096, true))
                {
                    string content = reader.ReadToEnd();
                    var preview = new Label(content);
                    preview.AddToClassList("document-preview");
                    container.Add(preview);
                }
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

                var otherParticipants = FindOtherParticipants(conversation);
                activeContactId = otherParticipants.Count > 0 ? otherParticipants[0] : "nightwire";
                bool isGroupConversation = otherParticipants.Count > 1;
                if (isGroupConversation)
                {
                    contactName.text = "GROUP CHAT";
                    contactStatus.text = otherParticipants.Count.ToString() + " PARTICIPANTS · EPISODE CHANNEL";
                    groupParticipants.text = string.Join("  ·  ", otherParticipants.Select(CharacterName));
                    groupParticipants.style.display = DisplayStyle.Flex;
                    profileOpenButton.text = "VIEW MEMBER";
                }
                else
                {
                    contactName.text = CharacterName(activeContactId);
                    contactStatus.text = CharacterStatus(activeContactId);
                    groupParticipants.text = string.Empty;
                    groupParticipants.style.display = DisplayStyle.None;
                    profileOpenButton.text = "VIEW PROFILE";
                }
                bool hasPendingMinigame = !string.IsNullOrEmpty(session.Progress.PendingMinigameId);
                minigameEntryButton.style.display = hasPendingMinigame ? DisplayStyle.Flex : DisplayStyle.None;
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
                string callId = ReadString(message, "call_id");
                var call = new Label("INCOMING CALL · " + callId);
                call.AddToClassList("message-media-note");
                bubble.Add(call);
                var answer = new Button { text = "ANSWER CALL" };
                answer.AddToClassList("message-media-control");
                answer.SetEnabled(session?.Progress != null &&
                    string.Equals(session.Progress.PendingCallId, callId, StringComparison.Ordinal));
                answer.clicked += () => StartCallPresentation(callId);
                bubble.Add(answer);
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

        private void StartCallPresentation(string callId)
        {
            if (session == null || session.Package == null || session.Progress == null ||
                !string.Equals(session.Progress.PendingCallId, callId, StringComparison.Ordinal) ||
                !session.Package.Calls.TryGetValue(callId, out var definition))
            { SetStatus("THIS CALL IS NOT CURRENTLY AVAILABLE."); return; }
            StopMediaPlayback();
            var started = session.StartCallPlayback(callId);
            if (!started.Success || started.CallTimeline == null) { SetStatus(started.Error ?? "CALL COULD NOT START."); return; }
            StopCallMedia();
            activeCallId = callId;
            callMediaReady = false;
            callTickAccumulatorSeconds = 0;
            callLastMediaPositionMilliseconds = Math.Max(0, started.CallTimeline.ElapsedMilliseconds);
            callUsesVideo = string.Equals(ReadString(definition, "mode"), "video", StringComparison.Ordinal);
            callPausedForApp = false;
            callScreen.style.display = DisplayStyle.Flex;
            callKicker.text = "INCOMING TRANSMISSION · " + ReadString(definition, "mode").ToUpperInvariant();
            callContact.text = CallParticipantName(definition);
            callSpeaker.text = string.Empty; callSubtitle.text = string.Empty;
            callTimer.text = FormatCallTime(started.CallTimeline.ElapsedMilliseconds);
            callStatus.text = "VERIFYING ORIGINAL CALL MEDIA…"; callEffectOverlay.text = string.Empty;
            callRetryButton.style.display = DisplayStyle.None;
            BeginCallMedia(definition, started.CallTimeline.ElapsedMilliseconds);
        }

        private string CallParticipantName(JsonElement definition)
        {
            if (definition.TryGetProperty("participants", out var participants) && participants.ValueKind == JsonValueKind.Array)
                foreach (var participant in participants.EnumerateArray())
                    if (participant.ValueKind == JsonValueKind.String)
                    { string id = participant.GetString() ?? string.Empty; if (id.Length > 0 && id != "player") return CharacterName(id); }
            return "UNKNOWN CALLER";
        }

        private void RetryCallMedia()
        {
            if (string.IsNullOrEmpty(activeCallId) || session?.Package == null || !session.Package.Calls.TryGetValue(activeCallId, out var definition)) return;
            StopCallMedia(); callScreen.style.display = DisplayStyle.Flex; callRetryButton.style.display = DisplayStyle.None;
            callStatus.text = "RETRYING VERIFIED MEDIA…";
            long elapsed = session.Progress != null && session.Progress.CallProgress.TryGetValue(activeCallId, out var progress) ? progress.ElapsedMilliseconds : 0;
            BeginCallMedia(definition, elapsed);
        }

        private void BeginCallMedia(JsonElement definition, long elapsedMilliseconds)
        {
            if (!definition.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object) { FailCallMedia("CALL MEDIA METADATA IS MISSING."); return; }
            string mode = ReadString(definition, "mode");
            callUsesVideo = string.Equals(mode, "video", StringComparison.Ordinal);
            string assetId = ReadString(media, mode == "video" ? "video_asset_id" : "audio_asset_id");
            string expectedType = mode == "video" ? "video" : "audio";
            if (assetId.Length == 0) { FailCallMedia("CALL MEDIA REFERENCE IS MISSING."); return; }
            string path;
            try { path = CacheVerifiedMediaAsset(assetId, expectedType); }
            catch (Exception exception) { FailCallMedia("CALL MEDIA UNAVAILABLE · " + exception.Message); return; }
            if (mode == "video")
            {
                EnsureCallVideoPlayer(); callVideoSurface.image = callVideoTexture;
                callVideoSurface.style.display = DisplayStyle.Flex; callAudioAvatar.style.display = DisplayStyle.None;
                callVideoPlayer.url = new Uri(path).AbsoluteUri; callVideoPlayer.Prepare(); callStatus.text = "PREPARING VIDEO CALL…"; return;
            }
            if (!TryGetAudioType(path, out var audioType)) { FailCallMedia("UNSUPPORTED CALL AUDIO · USE MP3, WAV, OR OGG."); return; }
            EnsureCallAudioSource(); callAudioLoadCoroutine = StartCoroutine(LoadCallAudio(path, audioType, elapsedMilliseconds));
        }

        private System.Collections.IEnumerator LoadCallAudio(string path, AudioType audioType, long elapsedMilliseconds)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, audioType))
            {
                request.timeout = 60; yield return request.SendWebRequest();
                if (string.IsNullOrEmpty(activeCallId)) yield break;
                if (request.result != UnityWebRequest.Result.Success) { FailCallMedia("CALL AUDIO DECODE FAILED · " + request.error); yield break; }
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null) { FailCallMedia("CALL AUDIO DECODE FAILED."); yield break; }
                if (callAudioClip != null) Destroy(callAudioClip); callAudioClip = clip;
                callAudioSource.clip = callAudioClip;
                callAudioSource.time = Mathf.Clamp(elapsedMilliseconds / 1000f, 0f, Mathf.Max(0f, callAudioClip.length - 0.05f));
                callAudioSource.Play(); callAudioAvatar.style.display = DisplayStyle.Flex; callVideoSurface.style.display = DisplayStyle.None;
                callLastMediaPositionMilliseconds = Math.Max(0, (long)Math.Round(callAudioSource.time * 1000.0));
                callTickAccumulatorSeconds = 0;
                callMediaReady = true; callStatus.text = "CONNECTED · AUDIO TRANSMISSION"; callAudioLoadCoroutine = null;
            }
        }

        private void EnsureCallAudioSource()
        {
            if (callAudioSource == null) callAudioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            callAudioSource.playOnAwake = false; callAudioSource.loop = false;
        }

        private void EnsureCallVideoPlayer()
        {
            EnsureCallAudioSource();
            if (callVideoTexture == null) { callVideoTexture = new RenderTexture(960, 540, 0, RenderTextureFormat.ARGB32); callVideoTexture.Create(); }
            if (callVideoPlayer == null)
            {
                callVideoPlayer = gameObject.AddComponent<VideoPlayer>();
                callVideoPlayer.prepareCompleted += CallVideoPrepared; callVideoPlayer.errorReceived += CallVideoFailed;
            }
            callVideoPlayer.playOnAwake = false; callVideoPlayer.isLooping = false;
            callVideoPlayer.renderMode = VideoRenderMode.RenderTexture; callVideoPlayer.targetTexture = callVideoTexture;
            callVideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource; callVideoPlayer.EnableAudioTrack(0, true);
            callVideoPlayer.SetTargetAudioSource(0, callAudioSource);
        }

        private void CallVideoPrepared(VideoPlayer player)
        {
            if (player != callVideoPlayer || string.IsNullOrEmpty(activeCallId) || session?.Progress == null) return;
            long elapsed = session.Progress.CallProgress.TryGetValue(activeCallId, out var progress) ? progress.ElapsedMilliseconds : 0;
            if (player.length > 0) player.time = Math.Min(elapsed / 1000.0, Math.Max(0, player.length - 0.05));
            player.Play();
            callLastMediaPositionMilliseconds = Math.Max(0, (long)Math.Round(player.time * 1000.0));
            callTickAccumulatorSeconds = 0;
            callMediaReady = true; callStatus.text = "CONNECTED · VIDEO TRANSMISSION";
        }

        private void CallVideoFailed(VideoPlayer player, string message)
        { if (player == callVideoPlayer && !string.IsNullOrEmpty(activeCallId)) FailCallMedia("CALL VIDEO FAILED · " + message); }

        private void FailCallMedia(string message)
        { callMediaReady = false; if (callStatus != null) callStatus.text = message; if (callRetryButton != null) callRetryButton.style.display = DisplayStyle.Flex; }

        private void UpdateCallPlayback()
        {
            if (string.IsNullOrEmpty(activeCallId) || !callMediaReady || callPausedForApp || session == null || session.Progress == null) return;
            callEffectRemainingSeconds -= Time.unscaledDeltaTime;
            if (callEffectRemainingSeconds <= 0 && callEffectOverlay != null)
            {
                callEffectOverlay.text = string.Empty;
                callEffectOverlay.RemoveFromClassList("call-effect-signal_glitch");
                callEffectOverlay.RemoveFromClassList("call-effect-screen_flicker");
                callEffectOverlay.RemoveFromClassList("call-effect-static_burst");
                callEffectOverlay.RemoveFromClassList("call-effect-audio_distortion");
            }
            long mediaPositionMilliseconds = callUsesVideo && callVideoPlayer != null
                ? Math.Max(0, (long)Math.Round(callVideoPlayer.time * 1000.0))
                : callAudioSource != null ? Math.Max(0, (long)Math.Round(callAudioSource.time * 1000.0))
                : callLastMediaPositionMilliseconds;
            if (mediaPositionMilliseconds >= callLastMediaPositionMilliseconds)
                callTickAccumulatorSeconds += (mediaPositionMilliseconds - callLastMediaPositionMilliseconds) / 1000.0;
            callLastMediaPositionMilliseconds = mediaPositionMilliseconds;
            if (callTickAccumulatorSeconds < 1.0) return;
            long tickMilliseconds = (long)(callTickAccumulatorSeconds * 1000.0); callTickAccumulatorSeconds -= tickMilliseconds / 1000.0;
            var result = session.AdvanceCallPlayback(activeCallId, tickMilliseconds, locale);
            if (!result.Success || result.CallTimeline == null) { FailCallMedia(result.Error ?? "CALL TIMELINE FAILED."); return; }
            callTimer.text = FormatCallTime(result.CallTimeline.ElapsedMilliseconds);
            foreach (var subtitle in result.CallTimeline.Subtitles)
            {
                string speakerId = ReadString(subtitle, "speaker_id");
                callSpeaker.text = speakerId.Length > 0 ? CharacterName(speakerId) : string.Empty;
                callSubtitle.text = Localize(ReadString(subtitle, "text_key"), string.Empty);
            }
            foreach (var cue in result.CallTimeline.Cues) ApplyCallCue(cue);
            if (result.CallTimeline.Completed)
            {
                callStatus.text = "TRANSMISSION COMPLETE"; callScreen.style.display = DisplayStyle.None;
                StopCallMedia(); activeCallId = null; RenderConversation();
            }
        }

        private void ApplyCallCue(JsonElement cue)
        {
            string type = ReadString(cue, "type");
            if (type == "speaker_focus") { callSpeaker.text = CharacterName(ReadString(cue, "speaker_id")); return; }
            if (type == "overlay_text") { callEffectOverlay.text = Localize(ReadString(cue, "text_key"), string.Empty); callEffectRemainingSeconds = 2.0f; return; }
            if (type == "visual_effect")
            {
                string effect = ReadString(cue, "effect");
                callEffectOverlay.RemoveFromClassList("call-effect-signal_glitch");
                callEffectOverlay.RemoveFromClassList("call-effect-screen_flicker");
                callEffectOverlay.RemoveFromClassList("call-effect-static_burst");
                callEffectOverlay.RemoveFromClassList("call-effect-audio_distortion");
                callEffectOverlay.AddToClassList("call-effect-" + effect);
                callEffectOverlay.text = effect.Replace('_', ' ').ToUpperInvariant(); callEffectRemainingSeconds = 0.65f;
            }
        }

        private static string FormatCallTime(long milliseconds)
        { long seconds = Math.Max(0, milliseconds / 1000); return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); }

        private void StopCallMedia()
        {
            if (callAudioLoadCoroutine != null) { StopCoroutine(callAudioLoadCoroutine); callAudioLoadCoroutine = null; }
            if (callAudioSource != null) callAudioSource.Stop();
            if (callAudioClip != null) { Destroy(callAudioClip); callAudioClip = null; }
            if (callVideoPlayer != null) callVideoPlayer.Stop();
            if (callVideoTexture != null) { callVideoTexture.Release(); Destroy(callVideoTexture); callVideoTexture = null; }
            callMediaReady = false; callTickAccumulatorSeconds = 0; callEffectRemainingSeconds = 0;
            callLastMediaPositionMilliseconds = 0; callUsesVideo = false; callPausedForApp = false;
        }

        private void OnApplicationPause(bool paused)
        {
            if (string.IsNullOrEmpty(activeCallId) || !callMediaReady || callPausedForApp == paused)
                return;

            callPausedForApp = paused;
            if (callUsesVideo && callVideoPlayer != null)
            {
                if (paused) callVideoPlayer.Pause();
                else callVideoPlayer.Play();
            }
            else if (callAudioSource != null)
            {
                if (paused) callAudioSource.Pause();
                else callAudioSource.UnPause();
            }
        }

        private void StartPendingMinigame()
        {
            if (session?.Progress == null || session.Package == null || string.IsNullOrEmpty(session.Progress.PendingMinigameId)) return;
            string id = session.Progress.PendingMinigameId;
            if (!session.Package.Minigames.TryGetValue(id, out var definition)) { SetStatus("INVESTIGATION PACKAGE IS MISSING."); return; }
            var started = session.StartMinigame(id);
            if (!started.Success || started.Minigame == null) { SetStatus(started.Error ?? "INVESTIGATION COULD NOT OPEN."); return; }
            activeMinigameId = id; minigameTickAccumulator = 0; selectedSequence.Clear(); selectedEvidenceItemId = null;
            minigameScreen.style.display = DisplayStyle.Flex; minigameEntryButton.style.display = DisplayStyle.None;
            minigameTitle.text = Localize(ReadString(definition, "title_key"), id.ToUpperInvariant());
            minigameInstructions.text = Localize(ReadString(definition, "instructions_key"), "Follow the evidence and complete the task.");
            minigameFeedback.text = string.Empty; minigameContinueButton.style.display = DisplayStyle.None;
            BuildMinigameChallenge(definition); UpdateMinigameProgress(started.Minigame);
        }

        private void BuildMinigameChallenge(JsonElement definition)
        {
            minigameChallengeContent.Clear();
            string type = ReadString(definition, "type");
            JsonElement config = definition.GetProperty("configuration");
            if (type == "sequence_reconstruction")
            {
                var preview = new Label("YOUR SIGNAL SEQUENCE: —"); preview.AddToClassList("minigame-sequence-preview"); minigameChallengeContent.Add(preview);
                var symbols = new VisualElement(); symbols.style.flexDirection = FlexDirection.Row; symbols.style.flexWrap = Wrap.Wrap; minigameChallengeContent.Add(symbols);
                foreach (var symbol in config.GetProperty("symbols").EnumerateArray())
                {
                    string value = symbol.GetString() ?? string.Empty; var button = new Button { text = value }; button.AddToClassList("minigame-symbol-button");
                    button.clicked += () => { if (selectedSequence.Count < 32) selectedSequence.Add(value); preview.text = "YOUR SIGNAL SEQUENCE: " + string.Join(" / ", selectedSequence); }; symbols.Add(button);
                }
                var clear = new Button { text = "CLEAR SEQUENCE" }; clear.AddToClassList("minigame-evidence-button");
                clear.clicked += () => { selectedSequence.Clear(); preview.text = "YOUR SIGNAL SEQUENCE: —"; }; minigameChallengeContent.Add(clear);
                var submit = new Button { text = "TRANSMIT SEQUENCE" }; submit.AddToClassList("minigame-submit-button"); submit.clicked += SubmitMinigameSequence; minigameChallengeContent.Add(submit);
            }
            else if (type == "frequency_tuning")
            {
                double min = config.GetProperty("minimum_frequency").GetDouble(); double max = config.GetProperty("maximum_frequency").GetDouble();
                var valueLabel = new Label("TUNING: " + min.ToString("0.##")); valueLabel.AddToClassList("minigame-sequence-preview"); minigameChallengeContent.Add(valueLabel);
                var slider = new Slider(string.Empty, (float)min, (float)max) { value = (float)((min + max) / 2.0) };
                slider.RegisterValueChangedCallback(evt => valueLabel.text = "TUNING: " + evt.newValue.ToString("0.##")); minigameChallengeContent.Add(slider);
                var submit = new Button { text = "LOCK FREQUENCY" }; submit.AddToClassList("minigame-submit-button");
                submit.clicked += () => SubmitMinigameFrequency(slider.value); minigameChallengeContent.Add(submit);
            }
            else if (type == "evidence_matching")
            {
                var prompt = new Label("SELECT TWO ITEMS TO TEST A CONNECTION."); prompt.AddToClassList("minigame-sequence-preview"); minigameChallengeContent.Add(prompt);
                var items = new VisualElement(); items.style.flexDirection = FlexDirection.Row; items.style.flexWrap = Wrap.Wrap; minigameChallengeContent.Add(items);
                foreach (var item in config.GetProperty("items").EnumerateArray())
                {
                    string id = ReadString(item, "item_id"); string label = Localize(ReadString(item, "label_key"), id);
                    var button = new Button { text = label }; button.AddToClassList("minigame-evidence-button");
                    button.clicked += () => SelectEvidenceItem(id, prompt); items.Add(button);
                }
            }
        }

        private void SelectEvidenceItem(string itemId, Label prompt)
        {
            if (string.IsNullOrEmpty(selectedEvidenceItemId)) { selectedEvidenceItemId = itemId; prompt.text = "FIRST ITEM: " + itemId + " · SELECT A MATCH"; return; }
            if (selectedEvidenceItemId == itemId) { selectedEvidenceItemId = null; prompt.text = "SELECTION CLEARED · CHOOSE TWO ITEMS"; return; }
            string left = selectedEvidenceItemId; selectedEvidenceItemId = null;
            var result = session.MatchEvidence(activeMinigameId, left, itemId);
            HandleMinigameResult(result, result.Success ? "PAIR CHECKED." : result.Error);
            if (activeMinigameId != null && prompt != null) prompt.text = "SELECT TWO ITEMS TO TEST ANOTHER CONNECTION.";
        }

        private void SubmitMinigameSequence()
        {
            if (selectedSequence.Count == 0) { minigameFeedback.text = "ADD THE SIGNAL SYMBOLS FIRST."; return; }
            var result = session.SubmitSequence(activeMinigameId, selectedSequence);
            HandleMinigameResult(result, result.Success ? "SIGNAL TRANSMITTED." : result.Error);
        }

        private void SubmitMinigameFrequency(float frequency)
        {
            var result = session.SubmitFrequency(activeMinigameId, frequency);
            HandleMinigameResult(result, result.Success ? "FREQUENCY SUBMITTED." : result.Error);
        }

        private void HandleMinigameResult(RuntimeSessionResult result, string feedback)
        {
            if (result == null || !result.Success) { minigameFeedback.text = feedback ?? "INPUT COULD NOT BE ACCEPTED."; return; }
            if (result.Minigame != null) UpdateMinigameProgress(result.Minigame);
            if (result.Minigame != null && result.Minigame.Solved)
            {
                minigameFeedback.text = "SIGNAL VERIFIED · INVESTIGATION COMPLETE.";
                minigameContinueButton.style.display = DisplayStyle.Flex; activeMinigameId = null; return;
            }
            if (result.Minigame != null && result.Minigame.AttemptsExhausted)
            {
                minigameFeedback.text = "NO ATTEMPTS REMAIN. THE INVESTIGATION HAS STALLED.";
                minigameContinueButton.style.display = DisplayStyle.Flex; activeMinigameId = null; return;
            }
            minigameFeedback.text = feedback ?? "RESULT RECORDED.";
        }

        private void UpdateMinigameProgress(Nightwire.Runtime.Minigames.MinigameRuntimeResult result)
        {
            if (result == null || session?.Progress == null || string.IsNullOrEmpty(activeMinigameId)) return;
            double elapsed = session.Progress.MinigameProgress.TryGetValue(activeMinigameId, out var progress) ? progress.ElapsedSeconds : 0;
            string time = session.Package.Minigames.TryGetValue(activeMinigameId, out var definition) && definition.TryGetProperty("time_limit_seconds", out var limit)
                ? " · TIME " + Math.Max(0, (int)Math.Ceiling(limit.GetDouble() - elapsed)).ToString("00") + "s" : string.Empty;
            minigameProgress.text = "ATTEMPTS LEFT " + result.AttemptsRemaining.ToString("00") + time;
        }

        private void UpdateMinigamePlayback()
        {
            if (string.IsNullOrEmpty(activeMinigameId) || session?.Progress == null || session.Package == null) return;
            if (!session.Package.Minigames.TryGetValue(activeMinigameId, out var definition) || !definition.TryGetProperty("time_limit_seconds", out var limit)) return;
            minigameTickAccumulator += Time.unscaledDeltaTime; if (minigameTickAccumulator < 1.0) return;
            minigameTickAccumulator -= 1.0;
            var tick = session.AdvanceMinigameTime(activeMinigameId, 1.0);
            if (!tick.Success) { minigameFeedback.text = tick.Error ?? "INVESTIGATION TIMER FAILED."; return; }
            if (tick.Minigame != null) UpdateMinigameProgress(tick.Minigame);
            if (tick.Minigame != null && tick.Minigame.AttemptsExhausted)
            { minigameFeedback.text = "TIME EXPIRED · INVESTIGATION FAILED."; minigameContinueButton.style.display = DisplayStyle.Flex; activeMinigameId = null; }
        }

        private void CloseMinigamePresentation()
        {
            activeMinigameId = null; selectedSequence.Clear(); selectedEvidenceItemId = null; minigameTickAccumulator = 0;
            if (minigameScreen != null) minigameScreen.style.display = DisplayStyle.None;
            if (minigameContinueButton != null) minigameContinueButton.style.display = DisplayStyle.None;
            RenderConversation();
        }

        private void UpdateMediaPlayback()
        {
            if (activeAudioAssetId != null && !audioPaused && mediaAudioSource != null &&
                activeAudioClip != null && !mediaAudioSource.isPlaying)
            {
                if (activeAudioButton != null) activeAudioButton.text = "REPLAY AUDIO";
                if (activeAudioStatus != null) activeAudioStatus.text = "PLAYBACK COMPLETE";
                activeAudioAssetId = null;
            }
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

            StopVideoPlayback();
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
            try { path = CacheVerifiedMediaAsset(assetId, "audio"); }
            catch (Exception exception)
            {
                status.text = "AUDIO UNAVAILABLE · " + exception.Message;
                button.SetEnabled(true);
                audioLoadCoroutine = null;
                yield break;
            }

            if (!TryGetAudioType(path, out var audioType))
            {
                status.text = "UNSUPPORTED AUDIO FORMAT · USE MP3, WAV, OR OGG";
                button.SetEnabled(true);
                audioLoadCoroutine = null;
                yield break;
            }

            string url = new Uri(path).AbsoluteUri;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
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

            StopAudioPlayback();
            string path;
            try { path = CacheVerifiedMediaAsset(assetId, "video"); }
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
                mediaVideoPlayer.prepareCompleted += VideoPrepared;
                mediaVideoPlayer.loopPointReached += VideoCompleted;
                mediaVideoPlayer.errorReceived += VideoFailed;
            }
            mediaVideoPlayer.playOnAwake = false;
            mediaVideoPlayer.isLooping = false;
            mediaVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            mediaVideoPlayer.targetTexture = videoRenderTexture;
            mediaVideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            mediaVideoPlayer.EnableAudioTrack(0, true);
            mediaVideoPlayer.SetTargetAudioSource(0, mediaAudioSource);
        }

        private void VideoPrepared(VideoPlayer player)
        {
            if (player != mediaVideoPlayer || string.IsNullOrEmpty(activeVideoAssetId)) return;
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

        private string CacheVerifiedMediaAsset(string assetId, string expectedType)
        {
            if (session == null || session.Package == null ||
                !session.Package.AssetsById.TryGetValue(assetId, out var metadata))
                throw new InvalidOperationException("The media asset is not part of the active episode manifest.");
            if (!string.Equals(metadata.Type, expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The referenced asset type does not match the message type.");

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
                try
                {
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
                catch
                {
                    if (File.Exists(temporary))
                    {
                        try { File.Delete(temporary); } catch { }
                    }
                    throw;
                }
            }
        }

        private static bool TryGetAudioType(string path, out AudioType audioType)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3":
                    audioType = AudioType.MPEG;
                    return true;
                case ".wav":
                    audioType = AudioType.WAV;
                    return true;
                case ".ogg":
                    audioType = AudioType.OGGVORBIS;
                    return true;
                default:
                    audioType = AudioType.UNKNOWN;
                    return false;
            }
        }

        private void StopAudioPlayback()
        {
            if (audioLoadCoroutine != null)
            {
                StopCoroutine(audioLoadCoroutine);
                audioLoadCoroutine = null;
            }
            if (mediaAudioSource != null)
            {
                mediaAudioSource.Stop();
                mediaAudioSource.clip = null;
            }
            if (activeAudioClip != null)
            {
                Destroy(activeAudioClip);
                activeAudioClip = null;
            }
            if (activeAudioButton != null)
            {
                activeAudioButton.text = "PLAY AUDIO";
                activeAudioButton.SetEnabled(true);
            }
            if (activeAudioStatus != null) activeAudioStatus.text = "STOPPED";
            activeAudioAssetId = null;
            activeAudioButton = null;
            activeAudioStatus = null;
            audioPaused = false;
        }

        private void StopVideoPlayback()
        {
            if (mediaVideoPlayer != null) mediaVideoPlayer.Stop();
            if (activeVideoButton != null)
            {
                activeVideoButton.text = "PLAY VIDEO";
                activeVideoButton.SetEnabled(true);
            }
            if (activeVideoStatus != null) activeVideoStatus.text = "STOPPED";
            activeVideoAssetId = null;
            activeVideoButton = null;
            activeVideoStatus = null;
        }

        private void StopMediaPlayback()
        {
            StopAudioPlayback();
            StopVideoPlayback();
            if (mediaVideoPlayer != null) mediaVideoPlayer.targetTexture = null;
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

        private void ShowConversationInbox()
        {
            if (conversationInbox == null || session?.Package == null || session.Progress == null ||
                !string.IsNullOrEmpty(activeCallId) || !string.IsNullOrEmpty(activeMinigameId))
                return;
            RenderConversationInbox();
            conversationInbox.style.display = DisplayStyle.Flex;
        }

        private void HideConversationInbox()
        {
            if (conversationInbox != null)
                conversationInbox.style.display = DisplayStyle.None;
        }

        private void RenderConversationInbox()
        {
            if (conversationInboxList == null || session?.Package == null || session.Progress == null)
                return;

            conversationInboxList.Clear();
            var opened = session.Progress.ConversationProgress.Keys
                .Where(id => session.Package.Conversations.ContainsKey(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            if (opened.Length == 0)
            {
                var empty = new Label("NO CONVERSATIONS OPENED YET.");
                empty.AddToClassList("inbox-empty");
                conversationInboxList.Add(empty);
                return;
            }

            foreach (string conversationId in opened)
            {
                if (!session.Package.Conversations.TryGetValue(conversationId, out var conversation) ||
                    !session.Progress.ConversationProgress.TryGetValue(conversationId, out var progress))
                    continue;

                var participants = FindOtherParticipants(conversation);
                string title = participants.Count > 1
                    ? "GROUP CHAT · " + participants.Count.ToString() + " MEMBERS"
                    : participants.Count == 1 ? CharacterName(participants[0]) : "NIGHTWIRE";
                string preview = "NO MESSAGES YET";
                if (conversation.TryGetProperty("messages", out var messages) &&
                    messages.ValueKind == JsonValueKind.Array)
                {
                    var messageArray = messages.EnumerateArray().ToArray();
                    for (int i = messageArray.Length - 1; i >= 0; i--)
                    {
                        string messageId = ReadString(messageArray[i], "message_id");
                        if (!progress.DeliveredMessageIds.Contains(messageId))
                            continue;
                        string key = ReadString(messageArray[i], "text_key");
                        if (key.Length == 0)
                            key = ReadString(messageArray[i], "prompt_key");
                        preview = key.Length > 0
                            ? Localize(key, "MESSAGE RECEIVED")
                            : ReadString(messageArray[i], "type").ToUpperInvariant() + " RECEIVED";
                        break;
                    }
                }

                int unreadCount = progress.DeliveredMessageIds.Count(id => !progress.ReadMessageIds.Contains(id));
                string state = unreadCount > 0 ? "UNREAD " + unreadCount.ToString()
                    : progress.IsComplete ? "COMPLETE"
                    : progress.WaitingChoiceMessageId != null ? "YOUR RESPONSE"
                    : "OPEN";
                var item = new Button();
                item.AddToClassList("inbox-conversation-item");
                var textBlock = new VisualElement();
                textBlock.AddToClassList("inbox-conversation-copy");
                var name = new Label(title);
                name.AddToClassList("inbox-conversation-name");
                var last = new Label(preview);
                last.AddToClassList("inbox-conversation-preview");
                var status = new Label(state);
                status.AddToClassList("inbox-conversation-state");
                textBlock.Add(name);
                textBlock.Add(last);
                item.Add(textBlock);
                item.Add(status);
                bool isActive = string.Equals(conversationId, session.Progress.ActiveConversationId, StringComparison.Ordinal);
                if (isActive)
                    item.AddToClassList("inbox-conversation-active");
                item.clicked += () => OpenConversationFromInbox(conversationId);
                conversationInboxList.Add(item);
            }
        }

        private void OpenConversationFromInbox(string conversationId)
        {
            if (session == null || session.Progress == null ||
                !session.Progress.ConversationProgress.ContainsKey(conversationId))
                return;

            var result = session.BeginConversation(conversationId);
            if (!result.Success)
            {
                SetStatus(result.Error ?? "Could not open this conversation.");
                return;
            }

            HideConversationInbox();
            ActivateCurrentConversation();
            ShowMessagesTab();
            RenderConversation();
        }

        private void BackClicked()
        {
            CloseRequested?.Invoke();
        }

        private List<string> FindOtherParticipants(JsonElement conversation)
        {
            var result = new List<string>();
            if (conversation.TryGetProperty("participants", out var participants) &&
                participants.ValueKind == JsonValueKind.Array)
            {
                foreach (var participant in participants.EnumerateArray())
                {
                    if (participant.ValueKind != JsonValueKind.String) continue;
                    string id = participant.GetString() ?? string.Empty;
                    if (id.Length > 0 && !string.Equals(id, "player", StringComparison.Ordinal))
                        result.Add(id);
                }
            }
            return result;
        }

        private void ShowContactDirectory()
        {
            if (contactsOverlay == null || session?.Package == null)
                return;

            contactsList.Clear();
            foreach (var entry in session.Package.CharacterProfiles
                .Where(pair => !string.Equals(pair.Key, "player", StringComparison.Ordinal))
                .OrderBy(pair => CharacterName(pair.Key), StringComparer.Ordinal))
            {
                string characterId = entry.Key;
                var card = new Button();
                card.AddToClassList("contact-directory-card");
                var identity = new VisualElement();
                identity.AddToClassList("contact-directory-identity");
                string displayName = CharacterName(characterId);
                var avatar = new Label(displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "?");
                avatar.AddToClassList("contact-directory-avatar");
                identity.Add(avatar);
                var copy = new VisualElement();
                copy.AddToClassList("contact-directory-copy");
                var name = new Label(displayName);
                name.AddToClassList("contact-directory-name");
                var status = new Label(CharacterStatus(characterId));
                status.AddToClassList("contact-directory-status");
                var bio = new Label(Localize(ReadString(entry.Value, "bio_key"), "No profile information available."));
                bio.AddToClassList("contact-directory-bio");
                copy.Add(name);
                copy.Add(status);
                copy.Add(bio);
                identity.Add(copy);
                card.Add(identity);
                card.clicked += () => ShowContactProfile(characterId);
                contactsList.Add(card);
            }

            contactsOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideContactDirectory()
        {
            if (contactsOverlay != null)
                contactsOverlay.style.display = DisplayStyle.None;
        }

        private void ShowContactProfile()
        {
            if (string.IsNullOrEmpty(activeContactId))
                return;
            ShowContactProfile(activeContactId);
        }

        private void ShowContactProfile(string characterId)
        {
            if (profileOverlay == null || session?.Package == null ||
                string.IsNullOrEmpty(characterId) ||
                !session.Package.CharacterProfiles.TryGetValue(characterId, out var profile))
                return;

            HideContactDirectory();
            profileName.text = CharacterName(characterId);
            profileStatus.text = CharacterStatus(characterId);
            profileBio.text = Localize(ReadString(profile, "bio_key"), "No profile information available.");
            profileOverlay.style.display = DisplayStyle.Flex;
        }

        private void HideContactProfile()
        {
            if (profileOverlay != null)
                profileOverlay.style.display = DisplayStyle.None;
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

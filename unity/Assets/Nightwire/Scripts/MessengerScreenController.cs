using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Sessions;
using UnityEngine;
using UnityEngine.UIElements;

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

                        messageList.Add(BuildMessage(message, otherParticipant));
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

        private VisualElement BuildMessage(JsonElement message, string otherParticipant)
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
            else if (type == "audio" || type == "video" || type == "document")
            {
                var attachment = new Label(type.ToUpperInvariant() + " ATTACHMENT");
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

            var time = new Label(outgoing ? "YOU" : "NIGHTWIRE");
            time.AddToClassList("message-time");
            bubble.Add(time);
            row.Add(bubble);
            return row;
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

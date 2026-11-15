using System;
using System.Collections.Generic;
using System.IO;
using Nightwire.Runtime.Conversations;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Loading;
using Nightwire.Runtime.Progression;
using Nightwire.Runtime.Saves;
using Nightwire.Runtime.Calls;
using Nightwire.Runtime.Minigames;
using Nightwire.Runtime.Assets;

namespace Nightwire.Runtime.Sessions
{
    public sealed class RuntimeSessionResult
    {
        public bool Success { get; internal set; }
        public string? ErrorCode { get; internal set; }
        public string? Error { get; internal set; }
        public bool RecoveredFromBackup { get; internal set; }
        public bool Migrated { get; internal set; }
        public List<string> NewlyDeliveredMessageIds { get; } = new List<string>();
        public List<RuntimeCommand> Commands { get; } = new List<RuntimeCommand>();
        public CallRuntimeResult? CallTimeline { get; internal set; }
        public MinigameRuntimeResult? Minigame { get; internal set; }
    }

    /// <summary>
    /// Coordinates one loaded episode, its player progress, story events, conversations, and local saves.
    /// The Unity host owns this coordinator on its game thread and renders only the typed commands it returns.
    /// </summary>
    public sealed class RuntimeSessionCoordinator
    {
        private readonly EpisodePackageLoader _loader;
        private readonly SaveStore _saveStore;
        private readonly StoryEventEngine _storyEngine;
        private readonly ConversationRuntime _conversationRuntime;
        private readonly CallRuntime _callRuntime = new CallRuntime();
        private readonly MinigameRuntime _minigameRuntime = new MinigameRuntime();

        public LoadedEpisodePackage? Package { get; private set; }
        public EpisodeProgressState? Progress { get; private set; }
        public bool IsActive => Package != null && Progress != null;

        public RuntimeSessionCoordinator(SaveStore saveStore,
            EpisodePackageLoader? loader = null,
            StoryEventEngine? storyEngine = null,
            ConversationRuntime? conversationRuntime = null)
        {
            _saveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));
            _loader = loader ?? new EpisodePackageLoader();
            _storyEngine = storyEngine ?? new StoryEventEngine();
            _conversationRuntime = conversationRuntime ?? new ConversationRuntime(_storyEngine);
        }

        public RuntimeSessionResult StartNewSession(string episodeDirectory)
        {
            if (IsActive) return Fail("session_already_active", "Close the current session before starting another episode.");
            LoadedEpisodePackage package;
            try { package = _loader.Load(episodeDirectory); }
            catch (EpisodePackageLoadException exception) { return Fail(exception.ErrorCode, exception.Message); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { return Fail("package_load_failed", exception.Message); }
            return StartNewSession(package);
        }

        public RuntimeSessionResult StartNewSession(LoadedEpisodePackage package)
        {
            if (IsActive) return Fail("session_already_active", "Close the current session before starting another episode.");
            if (package == null) throw new ArgumentNullException(nameof(package));
            var state = NewProgress(package);
            var story = _storyEngine.ProcessTrigger(package.RuntimeDefinition, state,
                new StoryTrigger { Type = StoryTriggerType.EpisodeStarted });
            if (!story.Success) return Fail("entry_event_failed", story.Error ?? "Episode entry event failed.");
            try { _saveStore.SaveNew(state); }
            catch (SaveStoreException exception) { return Fail(exception.ErrorCode, exception.Message); }

            Package = package;
            Progress = state;
            var result = Success();
            AddStory(result, story);
            return result;
        }

        public RuntimeSessionResult ResumeSession(string episodeDirectory)
        {
            if (IsActive) return Fail("session_already_active", "Close the current session before resuming another episode.");
            LoadedEpisodePackage package;
            try { package = _loader.Load(episodeDirectory); }
            catch (EpisodePackageLoadException exception) { return Fail(exception.ErrorCode, exception.Message); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { return Fail("package_load_failed", exception.Message); }
            return ResumeSession(package);
        }

        public RuntimeSessionResult ResumeSession(LoadedEpisodePackage package)
        {
            if (IsActive) return Fail("session_already_active", "Close the current session before resuming another episode.");
            if (package == null) throw new ArgumentNullException(nameof(package));
            SaveLoadResult loaded;
            try { loaded = _saveStore.Load(package.Descriptor.EpisodeId, package.Descriptor.EpisodeVersion); }
            catch (SaveStoreException exception) { return Fail(exception.ErrorCode, exception.Message); }

            string? validationError = ValidateRestoredState(package, loaded.State);
            if (validationError != null) return Fail("save_state_incompatible", validationError);

            Package = package;
            Progress = loaded.State;
            var result = Success();
            result.RecoveredFromBackup = loaded.RecoveredFromBackup;
            result.Migrated = loaded.Migrated;
            return result;
        }

        public EpisodeAssetOpenResult OpenAsset(string assetId)
        {
            if (!TryContext(out var package, out var state, out var failure))
                return new EpisodeAssetOpenResult { Success = false, ErrorCode = failure!.ErrorCode, Error = failure.Error };
            return new EpisodeAssetResolver(package, state).Open(assetId);
        }

        public RuntimeSessionResult BeginConversation(string conversationId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var operation = _conversationRuntime.BeginConversation(package, state, conversationId);
            if (!operation.Success) return Fail("conversation_begin_failed", operation.Error ?? "Could not begin conversation.");
            return Persist(Success());
        }

        public RuntimeSessionResult AdvanceConversation(double elapsedSeconds)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var operation = _conversationRuntime.Advance(package, state, elapsedSeconds);
            if (!operation.Success) return Fail("conversation_advance_failed", operation.Error ?? "Could not advance conversation.");
            var result = Success();
            result.NewlyDeliveredMessageIds.AddRange(operation.NewlyDeliveredMessageIds);
            foreach (var story in operation.StoryResults) AddStory(result, story);

            // Elapsed delay is an in-memory clock while a message is pending. Persist only when
            // a delivery boundary is crossed (or the safety yield is reached), not every frame.
            // The host persists partial delay progress on pause/checkpoint through SaveNow().
            if (operation.NewlyDeliveredMessageIds.Count == 0 && !operation.YieldedAtSafetyLimit)
                return result;
            return Persist(result);
        }

        public RuntimeSessionResult MarkMessageRead(string messageId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var operation = _conversationRuntime.MarkRead(package, state, messageId);
            if (!operation.Success) return Fail("message_read_failed", operation.Error ?? "Could not mark message read.");
            var result = Success();
            foreach (var story in operation.StoryResults) AddStory(result, story);
            return Persist(result);
        }

        public RuntimeSessionResult SelectChoice(string optionId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var operation = _conversationRuntime.SelectChoice(package, state, optionId);
            if (!operation.Success) return Fail("choice_failed", operation.Error ?? "Could not select choice.");
            var result = Success();
            foreach (var story in operation.StoryResults) AddStory(result, story);
            return Persist(result);
        }

        public RuntimeSessionResult StartCallPlayback(string callId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var playback = _callRuntime.Start(package, state, callId);
            if (!playback.Success) return Fail("call_start_failed", playback.Error ?? "Could not start call.");
            var result = Success(); result.CallTimeline = playback;
            return Persist(result);
        }

        public RuntimeSessionResult AdvanceCallPlayback(string callId, long elapsedMilliseconds, string locale)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var playback = _callRuntime.Advance(package, state, callId, elapsedMilliseconds, locale);
            if (!playback.Success) return Fail("call_advance_failed", playback.Error ?? "Could not advance call.");
            var result = Success(); result.CallTimeline = playback;
            if (playback.Completed)
            {
                var story = _storyEngine.ProcessTrigger(package.RuntimeDefinition, state,
                    new StoryTrigger { Type = StoryTriggerType.CallEnded, ReferenceId = callId });
                if (!story.Success) return Fail("call_completion_event_failed", story.Error ?? "Call completion event failed.");
                state.PendingCallId = null; AddStory(result, story);
            }
            return Persist(result);
        }

        public RuntimeSessionResult StartMinigame(string minigameId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var game = _minigameRuntime.Start(package, state, minigameId);
            if (!game.Success) return Fail("minigame_start_failed", game.Error ?? "Could not start minigame.");
            var result = Success(); result.Minigame = game;
            return Persist(result);
        }

        public RuntimeSessionResult SubmitSequence(string minigameId, IReadOnlyList<string> sequence)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            return HandleMinigameResult(package, state, minigameId, _minigameRuntime.SubmitSequence(package, state, minigameId, sequence));
        }

        public RuntimeSessionResult SubmitFrequency(string minigameId, double frequency)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            return HandleMinigameResult(package, state, minigameId, _minigameRuntime.SubmitFrequency(package, state, minigameId, frequency));
        }

        public RuntimeSessionResult MatchEvidence(string minigameId, string leftItemId, string rightItemId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            return HandleMinigameResult(package, state, minigameId, _minigameRuntime.MatchEvidence(package, state, minigameId, leftItemId, rightItemId));
        }

        public RuntimeSessionResult AdvanceMinigameTime(string minigameId, double elapsedSeconds)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            var game = _minigameRuntime.AddElapsedTime(package, state, minigameId, elapsedSeconds);
            if (!game.Success) return Fail("minigame_time_failed", game.Error ?? "Could not advance minigame.");
            var result = Success(); result.Minigame = game;
            if (game.AttemptsExhausted) state.PendingMinigameId = null;
            return Persist(result);
        }

        private RuntimeSessionResult HandleMinigameResult(LoadedEpisodePackage package, EpisodeProgressState state,
            string minigameId, MinigameRuntimeResult game)
        {
            if (!game.Success) return Fail("minigame_input_failed", game.Error ?? "Minigame input failed.");
            var result = Success(); result.Minigame = game;
            if (game.Solved)
            {
                var story = _storyEngine.ProcessTrigger(package.RuntimeDefinition, state,
                    new StoryTrigger { Type = StoryTriggerType.MinigameCompleted, ReferenceId = minigameId });
                if (!story.Success) return Fail("minigame_completion_event_failed", story.Error ?? "Minigame completion event failed.");
                state.PendingMinigameId = null; AddStory(result, story);
            }
            else if (game.AttemptsExhausted) state.PendingMinigameId = null;
            return Persist(result);
        }

        public RuntimeSessionResult EndCall(string callId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            if (!string.Equals(state.PendingCallId, callId, StringComparison.Ordinal))
                return Fail("call_not_pending", "The requested call is not the active pending call.");
            if (!state.CallProgress.TryGetValue(callId, out var callProgress) || !callProgress.IsComplete)
                return Fail("call_not_finished", "The prerecorded call timeline must reach its authored duration before completion.");
            state.PendingCallId = null;
            return ProcessAndPersist(package, state,
                new StoryTrigger { Type = StoryTriggerType.CallEnded, ReferenceId = callId });
        }

        public RuntimeSessionResult CompleteMinigame(string minigameId)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            if (!string.Equals(state.PendingMinigameId, minigameId, StringComparison.Ordinal))
                return Fail("minigame_not_pending", "The requested minigame is not the active pending minigame.");
            if (!state.MinigameProgress.TryGetValue(minigameId, out var minigameProgress) || !minigameProgress.IsComplete)
                return Fail("minigame_not_solved", "The minigame must be solved through its runtime before completion.");
            state.PendingMinigameId = null;
            return ProcessAndPersist(package, state,
                new StoryTrigger { Type = StoryTriggerType.MinigameCompleted, ReferenceId = minigameId });
        }

        public RuntimeSessionResult ProcessExternalTrigger(StoryTrigger trigger)
        {
            if (!TryContext(out var package, out var state, out var failure)) return failure!;
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            return ProcessAndPersist(package, state, trigger);
        }

        public RuntimeSessionResult SaveNow()
        {
            if (!TryContext(out _, out var state, out var failure)) return failure!;
            return Persist(Success());
        }

        /// <summary>Raised after the runtime has cleared its active package and progress.</summary>
        public event Action? SessionClosed;

        public void CloseSession()
        {
            bool wasActive = IsActive;
            Package = null;
            Progress = null;
            if (!wasActive) return;

            // A host may own an exclusive episode-installation lease for this session.
            // One faulty observer must not prevent the runtime from completing shutdown.
            var handlers = SessionClosed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch { /* Session state is already closed; observers cannot reverse it. */ }
            }
        }

        private RuntimeSessionResult ProcessAndPersist(LoadedEpisodePackage package, EpisodeProgressState state, StoryTrigger trigger)
        {
            var story = _storyEngine.ProcessTrigger(package.RuntimeDefinition, state, trigger);
            if (!story.Success) return Fail("story_event_failed", story.Error ?? "Story event processing failed.");
            var result = Success();
            AddStory(result, story);
            return Persist(result);
        }

        private RuntimeSessionResult Persist(RuntimeSessionResult result)
        {
            try { _saveStore.Save(Progress!); }
            catch (SaveStoreException exception)
            {
                CloseSession();
                return Fail(exception.ErrorCode, exception.Message);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                CloseSession();
                return Fail("save_write_failed", exception.Message);
            }
            return result;
        }

        private bool TryContext(out LoadedEpisodePackage package, out EpisodeProgressState state, out RuntimeSessionResult? failure)
        {
            package = Package!;
            state = Progress!;
            failure = null;
            if (package != null && state != null) return true;
            failure = Fail("session_not_active", "Load or start an episode before using runtime session operations.");
            return false;
        }

        private static EpisodeProgressState NewProgress(LoadedEpisodePackage package) =>
            new EpisodeProgressState
            {
                EpisodeId = package.Descriptor.EpisodeId,
                EpisodeVersion = package.Descriptor.EpisodeVersion
            };

        private static string? ValidateRestoredState(LoadedEpisodePackage package, EpisodeProgressState state)
        {
            if (!string.Equals(state.EpisodeId, package.Descriptor.EpisodeId, StringComparison.Ordinal))
                return "Save episode identity does not match the installed package.";
            if (!string.IsNullOrEmpty(state.ActiveConversationId) &&
                !package.Conversations.ContainsKey(state.ActiveConversationId))
                return "Save points to a conversation that is absent from this episode version.";
            if (state.PendingCallId != null && !package.Calls.ContainsKey(state.PendingCallId))
                return "Save points to a call that is absent from this episode version.";
            if (state.PendingMinigameId != null && !package.Minigames.ContainsKey(state.PendingMinigameId))
                return "Save points to a minigame that is absent from this episode version.";
            foreach (var pair in state.CallProgress)
                if (!package.Calls.ContainsKey(pair.Key)) return "Saved call progress is not present in this episode version.";
            foreach (var pair in state.MinigameProgress)
                if (!package.Minigames.ContainsKey(pair.Key)) return "Saved minigame progress is not present in this episode version.";
            foreach (var pair in state.ConversationProgress)
            {
                if (!package.Conversations.TryGetValue(pair.Key, out var conversation))
                    return $"Saved conversation progress '{pair.Key}' is not present in this episode.";
                var messageIds = new HashSet<string>(StringComparer.Ordinal);
                if (conversation.TryGetProperty("messages", out var messages))
                    foreach (var message in messages.EnumerateArray())
                        if (message.TryGetProperty("message_id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String)
                            messageIds.Add(id.GetString() ?? string.Empty);
                var progress = pair.Value;
                if ((progress.CurrentMessageId != null && !messageIds.Contains(progress.CurrentMessageId)) ||
                    (progress.NextMessageId != null && !messageIds.Contains(progress.NextMessageId)) ||
                    (progress.WaitingChoiceMessageId != null && !messageIds.Contains(progress.WaitingChoiceMessageId)))
                    return $"Saved progress for '{pair.Key}' references a message that is absent from this episode.";
            }
            return null;
        }

        private static void AddStory(RuntimeSessionResult target, StoryExecutionResult story)
        {
            target.Commands.AddRange(story.Commands);
        }

        private static RuntimeSessionResult Success() => new RuntimeSessionResult { Success = true };

        private static RuntimeSessionResult Fail(string code, string error) =>
            new RuntimeSessionResult { Success = false, ErrorCode = code, Error = error };
    }
}

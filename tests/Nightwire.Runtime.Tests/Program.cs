using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nightwire.Runtime.Loading;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Progression;
using Nightwire.Runtime.Conversations;
using Nightwire.Runtime.Saves;
using Nightwire.Runtime.Sessions;
using Nightwire.Runtime.Calls;
using Nightwire.Runtime.Minigames;
using Nightwire.Runtime.Assets;
using Nightwire.Runtime.Installation;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

internal static class Program
{
    private static int Main()
    {
        var failures = 0;
        Run("episode-start event opens conversation and sets flag", EpisodeStartExecutes, ref failures);
        Run("one-shot event cannot execute twice", OneShotEventIsIdempotent, ref failures);
        Run("invalid action batch does not partially mutate state", InvalidBatchIsAtomic, ref failures);
        Run("asset unlock and call start produce typed commands", AssetAndCallActionsExecute, ref failures);
        Run("episode loader verifies and indexes the sample package", EpisodeLoaderLoadsSample, ref failures);
        Run("episode loader rejects modified manifest content", EpisodeLoaderRejectsTampering, ref failures);
        Run("episode loader rejects unlisted files", EpisodeLoaderRejectsUnlistedFiles, ref failures);
        Run("episode loader rejects linked parent directories before reading package files", EpisodeLoaderRejectsLinkedParentDirectory, ref failures);
        Run("episode loader rejects a linked ancestor of the package root", EpisodeLoaderRejectsLinkedRootAncestor, ref failures);
        Run("episode loader rejects an older runtime", EpisodeLoaderRejectsOldRuntime, ref failures);
        Run("conversation runtime advances by authored delay and persists read state", ConversationRuntimePersistsProgress, ref failures);
        Run("conversation choices select the authored branch", ConversationRuntimeBranchesOnChoice, ref failures);
        Run("choice effects update story flags and relationship scores", ConversationChoiceEffectsPersist, ref failures);
        Run("save store round-trips player progress", SaveStoreRoundTripsProgress, ref failures);
        Run("concurrent new-game creation cannot overwrite an episode save", SaveStoreSerializesConcurrentNewGameCreation, ref failures);
        Run("save store rejects a symlinked episode directory", SaveStoreRejectsSymlinkedEpisodeDirectory, ref failures);
        Run("save store rejects a symlinked ancestor of its root", SaveStoreRejectsSymlinkedRootAncestor, ref failures);
        Run("save store recovers from a corrupt primary using backup", SaveStoreRecoversBackup, ref failures);
        Run("save store rejects a linked backup publication temporary file", SaveStoreRejectsLinkedBackupTemporaryFile, ref failures);
        Run("save store rejects a linked backup-restore temporary file", SaveStoreRejectsLinkedRestoreTemporaryFile, ref failures);
        Run("save store rejects a different episode identity", SaveStoreRejectsEpisodeMismatch, ref failures);
        Run("save store detects episode-version metadata tampering", SaveStoreRejectsMetadataTampering, ref failures);
        Run("save store applies an explicit episode migration", SaveStoreMigratesEpisodeVersion, ref failures);
        Run("save store normalizes migration exceptions without modifying the persisted save", SaveStoreNormalizesMigrationFailure, ref failures);
        Run("session coordinator starts an episode and persists its entry event", SessionStartsAndPersists, ref failures);
        Run("runtime session close notifies hosts once and isolates observer failures", RuntimeSessionCloseNotifiesHosts, ref failures);
        Run("closed runtime session cannot checkpoint stale progress", ClosedSessionCannotCheckpointStaleProgress, ref failures);
        Run("session coordinator resumes conversation progress", SessionResumesConversation, ref failures);
        Run("conversation delay ticks do not write a save every frame", ConversationDelayTicksAvoidSaveWrites, ref failures);
        Run("session coordinator prevents accidental new-game overwrite", SessionPreventsOverwrite, ref failures);
        Run("prerecorded call timeline emits cues and completes", CallRuntimeAdvancesTimeline, ref failures);
        Run("session coordinator dispatches call completion event", SessionDispatchesCallCompletion, ref failures);
        Run("sequence minigame validates and solves authored input", MinigameRuntimeSolvesSequence, ref failures);
        Run("frequency minigame applies authored tolerance", MinigameRuntimeSolvesFrequency, ref failures);
        Run("evidence minigame requires every authored pair", MinigameRuntimeMatchesEvidence, ref failures);
        Run("save store round-trips call and minigame progress", SaveStoreRoundTripsActivities, ref failures);
        Run("session coordinator dispatches solved minigame event", SessionDispatchesMinigameCompletion, ref failures);
        Run("asset resolver enforces unlocks and opens verified read-only content", AssetResolverEnforcesUnlockAndIntegrity, ref failures);
        Run("asset resolver rejects content modified after package load", AssetResolverRejectsPostLoadTampering, ref failures);
        Run("episode installer installs and updates a verified package", EpisodeInstallerInstallsAndUpdates, ref failures);
        Run("episode installer rejects ZIP path traversal", EpisodeInstallerRejectsTraversal, ref failures);
        Run("episode installer rejects ZIP symbolic links and reparse points", EpisodeInstallerRejectsArchiveLinks, ref failures);
        Run("episode installer rejects ZIP special files", EpisodeInstallerRejectsArchiveSpecialFiles, ref failures);
        Run("episode installer rejects ZIP file-directory path collisions", EpisodeInstallerRejectsPathTypeConflicts, ref failures);
        Run("episode downloader verifies a trusted archive hash", EpisodeDownloaderChecksTrustedHash, ref failures);
        Run("episode download and catalogue transports reject followed redirects", TransportsRejectFollowedRedirects, ref failures);
        Run("episode downloader rejects a linked destination ancestor", EpisodeDownloaderRejectsLinkedDestinationAncestor, ref failures);
        Run("signed release catalogue verifies signatures, rejects rollback, duplicate JSON keys, and malformed runtime versions", ReleaseCatalogueVerifiesSignatureAndSequence, ref failures);
        Run("installer startup recovery restores interrupted update backup", InstallerRecoveryRestoresPreviousInstall, ref failures);
        Run("installer recovery restores a valid backup over an invalid destination", InstallerRecoveryRestoresBackupOverInvalidDestination, ref failures);
        Run("installer recovery cleans crash-leftover quarantine only after validating active episode", InstallerRecoveryCleansValidatedQuarantine, ref failures);
        Run("installer rejects a symlinked ancestor of its install root", InstallerRejectsSymlinkedRootAncestor, ref failures);
        Run("installer rejects signed release identity mismatch before replacing existing content", InstallerRejectsReleaseIdentityMismatch, ref failures);
        Run("release manager performs one-time install recovery before library access", ReleaseManagerRecoversBeforeLibraryAccess, ref failures);
        Run("release manager refreshes signed catalogue and installs offline episode", ReleaseManagerInstallsOfflineEpisode, ref failures);
        Run("release manager cancels active catalogue refresh safely on disposal", ReleaseManagerDisposalCancelsRefresh, ref failures);
        Run("release manager cancels active install and releases its lease on disposal", ReleaseManagerDisposalCancelsInstall, ref failures);
        Run("release managers serialize same-episode operations across instances", ReleaseManagersSerializeSharedEpisodeOperation, ref failures);
        Run("release managers isolate download caches when sharing a download root", ReleaseManagersIsolateSharedDownloadRoot, ref failures);
        Run("episode library presentation cancels refresh and suppresses events after disposal", LibraryPresentationDisposalCancelsRefresh, ref failures);
        Run("episode access gate excludes session and installer ownership", EpisodeAccessGateExcludesSessionAndInstall, ref failures);
        Run("episode access gate coordinates independent instances and case variants", EpisodeAccessGateCoordinatesInstances, ref failures);
        Console.WriteLine(failures == 0 ? "All runtime tests passed." : $"{failures} runtime test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test, ref int failures)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
        }
    }

    private static void EpisodeStartExecutes()
    {
        var episode = CreateEpisode();
        var state = CreateState();
        var result = new StoryEventEngine().ProcessTrigger(
            episode, state, new StoryTrigger { Type = StoryTriggerType.EpisodeStarted });

        Assert(result.Success, result.Error ?? "Expected success.");
        Assert(state.ActiveConversationId == "conversation.opening", "Opening conversation was not selected.");
        Assert(state.StoryFlags.TryGetValue("episode_started", out var value) && value.Kind == StoryFlagValueKind.Boolean && value.BooleanValue, "Story flag was not set.");
        Assert(result.Commands.Count == 2, "Expected two typed commands.");
    }

    private static void OneShotEventIsIdempotent()
    {
        var episode = CreateEpisode();
        var state = CreateState();
        var engine = new StoryEventEngine();
        var trigger = new StoryTrigger { Type = StoryTriggerType.EpisodeStarted };

        var first = engine.ProcessTrigger(episode, state, trigger);
        var second = engine.ProcessTrigger(episode, state, trigger);

        Assert(first.ExecutedEventIds.Count == 1, "First trigger should execute one event.");
        Assert(second.ExecutedEventIds.Count == 0, "Second trigger should not replay a one-shot event.");
    }

    private static void InvalidBatchIsAtomic()
    {
        var episode = CreateEpisode();
        var validSibling = new StoryEventDefinition
        {
            EventId = "event.valid_sibling",
            TriggerType = StoryTriggerType.MessageCompleted,
            TriggerReferenceId = "message.missing"
        };
        validSibling.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.SetStoryFlag,
            Flag = "must_not_be_written",
            FlagValue = StoryFlagValue.FromBoolean(true)
        });
        episode.Events.Add(validSibling);

        var invalidEvent = new StoryEventDefinition
        {
            EventId = "event.invalid",
            TriggerType = StoryTriggerType.MessageCompleted,
            TriggerReferenceId = "message.missing"
        };
        invalidEvent.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.SetStoryFlag,
            Flag = "must_not_be_written",
            FlagValue = StoryFlagValue.FromBoolean(true)
        });
        invalidEvent.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.OpenConversation,
            TargetId = "conversation.missing"
        });
        episode.Events.Add(invalidEvent);

        var state = CreateState();
        var result = new StoryEventEngine().ProcessTrigger(
            episode, state, new StoryTrigger
            {
                Type = StoryTriggerType.MessageCompleted,
                ReferenceId = "message.missing"
            });

        Assert(!result.Success, "Invalid event should fail.");
        Assert(!state.StoryFlags.ContainsKey("must_not_be_written"), "Invalid batch partially changed story state.");
        Assert(!state.CompletedEventIds.Contains("event.invalid"), "Invalid event was marked complete.");
    }

    private static void AssetAndCallActionsExecute()
    {
        var episode = CreateEpisode();
        var storyEvent = new StoryEventDefinition
        {
            EventId = "event.call",
            TriggerType = StoryTriggerType.ChoiceSelected,
            TriggerReferenceId = "option.accept"
        };
        storyEvent.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.UnlockAsset,
            TargetId = "asset.photo"
        });
        storyEvent.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.StartCall,
            TargetId = "call.anisha"
        });
        episode.Events.Add(storyEvent);

        var state = CreateState();
        var result = new StoryEventEngine().ProcessTrigger(
            episode, state, new StoryTrigger
            {
                Type = StoryTriggerType.ChoiceSelected,
                ReferenceId = "option.accept"
            });

        Assert(result.Success, result.Error ?? "Expected success.");
        Assert(state.UnlockedAssetIds.Contains("asset.photo"), "Asset was not unlocked.");
        Assert(state.PendingCallId == "call.anisha", "Call was not queued.");
        Assert(result.Commands.Count == 2, "Expected two runtime commands.");
    }


    private static void EpisodeLoaderLoadsSample()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        Assert(package.Descriptor.EpisodeId == "nightwire.ep001", "Episode identity was not loaded.");
        Assert(package.Conversations.ContainsKey("anisha.day01"), "Conversation was not indexed.");
        Assert(package.CharacterProfiles.ContainsKey("anisha"), "Character profile was not indexed.");
        Assert(package.CharacterPosts.ContainsKey("anisha.post01"), "Character post was not indexed.");
        Assert(package.RuntimeDefinition.Events.Count == 2, "Story events were not parsed.");
        Assert(package.Localization["en"].ContainsKey("episode.title"), "Localization was not loaded.");
    }

    private static void EpisodeLoaderRejectsTampering()
    {
        string copy = CopySamplePackage();
        try
        {
            string conversation = Path.Combine(copy, "Characters", "Anisha", "Conversations", "day01.json");
            File.AppendAllText(conversation, " ");
            bool rejected = false;
            try { new EpisodePackageLoader().Load(copy); }
            catch (EpisodePackageLoadException exception) { rejected = exception.ErrorCode == "size_mismatch" || exception.ErrorCode == "hash_mismatch"; }
            Assert(rejected, "Tampered package file was not rejected.");
        }
        finally { Directory.Delete(copy, true); }
    }

    private static void EpisodeLoaderRejectsUnlistedFiles()
    {
        string copy = CopySamplePackage();
        try
        {
            File.WriteAllText(Path.Combine(copy, "unexpected.txt"), "not in manifest");
            bool rejected = false;
            try { new EpisodePackageLoader().Load(copy); }
            catch (EpisodePackageLoadException exception) { rejected = exception.ErrorCode == "unlisted_file"; }
            Assert(rejected, "Unlisted package file was not rejected.");
        }
        finally { Directory.Delete(copy, true); }
    }


    private static void EpisodeLoaderRejectsLinkedParentDirectory()
    {
        string copy = CopySamplePackage();
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-linked-" + Guid.NewGuid().ToString("N"));
        string linkedDirectory = Path.Combine(copy, "Characters", "Anisha");
        try
        {
            Directory.Move(linkedDirectory, outside);
            File.AppendAllText(Path.Combine(outside, "profile.json"), "tampered");
            Directory.CreateSymbolicLink(linkedDirectory, outside);

            bool rejectedBeforeReading = false;
            try { new EpisodePackageLoader().Load(copy); }
            catch (EpisodePackageLoadException exception) { rejectedBeforeReading = exception.ErrorCode == "unsafe_link"; }
            Assert(rejectedBeforeReading, "Loader read through a linked parent directory before rejecting the package.");
        }
        finally
        {
            if (Directory.Exists(linkedDirectory)) Directory.Delete(linkedDirectory);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
            if (Directory.Exists(copy)) Directory.Delete(copy, true);
        }
    }

    private static void EpisodeLoaderRejectsLinkedRootAncestor()
    {
        string package = CopySamplePackage();
        string parent = Path.Combine(Path.GetTempPath(), "nightwire-package-parent-" + Guid.NewGuid().ToString("N"));
        string linked = Path.Combine(parent, "linked");
        Directory.CreateDirectory(parent);
        try
        {
            Directory.CreateSymbolicLink(linked, Path.GetDirectoryName(package)!);
            string linkedPackage = Path.Combine(linked, Path.GetFileName(package));
            bool rejected = false;
            try { new EpisodePackageLoader().Load(linkedPackage); }
            catch (EpisodePackageLoadException exception) { rejected = exception.ErrorCode == "unsafe_root"; }
            Assert(rejected, "Loader accepted a package root beneath a linked ancestor.");
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
            if (Directory.Exists(package)) Directory.Delete(package, true);
        }
    }

    private static void EpisodeLoaderRejectsOldRuntime()
    {
        string root = FindRepositoryRoot();
        var loader = new EpisodePackageLoader(new EpisodePackageLoaderOptions { RuntimeVersion = "0.0.9" });
        bool rejected = false;
        try { loader.Load(Path.Combine(root, "examples", "Episode_001")); }
        catch (EpisodePackageLoadException exception) { rejected = exception.ErrorCode == "runtime_too_old"; }
        Assert(rejected, "Episode requiring a newer runtime was not rejected.");
    }


    private static void ConversationRuntimePersistsProgress()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        var state = CreateState();
        var runtime = new ConversationRuntime();

        var begun = runtime.BeginConversation(package, state, "anisha.day01");
        Assert(begun.Success, begun.Error ?? "Conversation did not begin.");

        var early = runtime.Advance(package, state, 1.1);
        Assert(early.Success && early.NewlyDeliveredMessageIds.Count == 0, "Message arrived before its authored delay.");
        var delivered = runtime.Advance(package, state, 0.1);
        Assert(delivered.Success && delivered.NewlyDeliveredMessageIds.Contains("anisha.day01.001"), "First message was not delivered.");

        var read = runtime.MarkRead(package, state, "anisha.day01.001");
        Assert(read.Success, read.Error ?? "Message could not be marked read.");
        Assert(state.ConversationProgress["anisha.day01"].ReadMessageIds.Contains("anisha.day01.001"),
            "Read state was not persisted in player progress.");

        Assert(delivered.NewlyDeliveredMessageIds.Contains("anisha.day01.002"),
            "Zero-delay choice prompt was not delivered after the first message.");
        Assert(state.ConversationProgress["anisha.day01"].WaitingChoiceMessageId == "anisha.day01.002",
            "Conversation did not pause for the player's choice.");
    }

    private static void ConversationRuntimeBranchesOnChoice()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        var state = CreateState();
        var runtime = new ConversationRuntime();

        Assert(runtime.BeginConversation(package, state, "anisha.day01").Success, "Conversation did not begin.");
        runtime.Advance(package, state, 1.2);
        runtime.Advance(package, state, 0);
        var selected = runtime.SelectChoice(package, state, "stay_quiet");
        Assert(selected.Success, selected.Error ?? "Choice selection failed.");

        var progress = state.ConversationProgress["anisha.day01"];
        Assert(progress.SelectedOptions["anisha.day01.002"] == "stay_quiet", "Selected option was not saved.");
        var branch = runtime.Advance(package, state, 0.8);
        Assert(branch.Success && branch.NewlyDeliveredMessageIds.Contains("anisha.day01.004"),
            "Runtime did not follow the selected choice branch.");
        Assert(!progress.DeliveredMessageIds.Contains("anisha.day01.003"),
            "Runtime delivered a message from the unselected branch.");
    }


    private static void ConversationChoiceEffectsPersist()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        using var document = JsonDocument.Parse(@"{
            ""conversation_id"": ""choice.effects"",
            ""messages"": [{
                ""message_id"": ""choice.effects.prompt"",
                ""type"": ""choice"",
                ""sender_id"": ""anisha"",
                ""prompt_key"": ""choice.effects.prompt"",
                ""options"": [{
                    ""option_id"": ""trust"",
                    ""text_key"": ""choice.effects.trust"",
                    ""next_message_id"": null,
                    ""set_flags"": [""anisha_trusted""],
                    ""relationship_delta"": 5
                }]
            }]
        }");
        package.Conversations["choice.effects"] = document.RootElement.Clone();

        var state = CreateState();
        var runtime = new ConversationRuntime();
        Assert(runtime.BeginConversation(package, state, "choice.effects").Success, "Choice conversation did not begin.");
        Assert(runtime.Advance(package, state, 0).Success, "Choice prompt did not deliver.");
        var selected = runtime.SelectChoice(package, state, "trust");
        Assert(selected.Success, selected.Error ?? "Choice selection failed.");
        Assert(state.StoryFlags.TryGetValue("anisha_trusted", out var flag) &&
            flag.Kind == StoryFlagValueKind.Boolean && flag.BooleanValue, "Choice flag was not applied.");
        Assert(state.RelationshipScores.TryGetValue("anisha", out var score) && score == 5,
            "Relationship delta was not applied to the choice sender.");
    }


    private static void SaveStoreRoundTripsProgress()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = CreateState();
            state.ActiveConversationId = "anisha.day01";
            state.CompletedEventIds.Add("event.opening");
            state.UnlockedAssetIds.Add("asset.photo");
            state.StoryFlags["trust"] = StoryFlagValue.FromNumber(2.5);
            state.RelationshipScores["anisha"] = 12;
            state.Notifications.Add(new RuntimeNotification { NotificationId = "n1", TitleKey = "title", BodyKey = "body" });
            var conversation = new ConversationProgressState
            {
                CurrentMessageId = "m2", NextMessageId = "m3", ElapsedDelaySeconds = 0.4
            };
            conversation.DeliveredMessageIds.Add("m1");
            conversation.ReadMessageIds.Add("m1");
            conversation.SelectedOptions["m2"] = "option.a";
            state.ConversationProgress.Add("anisha.day01", conversation);

            var store = new SaveStore(root);
            store.Save(state);
            var loaded = store.Load("nightwire.ep001", "1.0.0").State;
            Assert(loaded.ActiveConversationId == "anisha.day01", "Active conversation did not round-trip.");
            Assert(loaded.StoryFlags["trust"].Kind == StoryFlagValueKind.Number &&
                Math.Abs(loaded.StoryFlags["trust"].NumberValue - 2.5) < 0.000001, "Typed story flag did not round-trip.");
            Assert(loaded.RelationshipScores["anisha"] == 12, "Relationship score did not round-trip.");
            Assert(loaded.ConversationProgress["anisha.day01"].SelectedOptions["m2"] == "option.a",
                "Selected conversation option did not round-trip.");
            Assert(loaded.Notifications.Count == 1 && loaded.Notifications[0].NotificationId == "n1",
                "Notifications did not round-trip.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SaveStoreSerializesConcurrentNewGameCreation()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-new-race-" + Guid.NewGuid().ToString("N"));
        try
        {
            var firstStore = new SaveStore(root);
            var secondStore = new SaveStore(root);
            var firstState = CreateState();
            firstState.RelationshipScores["anisha"] = 11;
            var secondState = CreateState();
            secondState.RelationshipScores["anisha"] = 22;
            using var start = new ManualResetEventSlim(false);

            Task<bool> first = Task.Run(() =>
            {
                start.Wait();
                try { firstStore.SaveNew(firstState); return true; }
                catch (SaveStoreException exception) when (exception.ErrorCode == "save_exists") { return false; }
            });
            Task<bool> second = Task.Run(() =>
            {
                start.Wait();
                try { secondStore.SaveNew(secondState); return true; }
                catch (SaveStoreException exception) when (exception.ErrorCode == "save_exists") { return false; }
            });

            start.Set();
            Task.WaitAll(first, second);
            Assert(first.Result != second.Result,
                "Concurrent new-game creation did not admit exactly one initial save.");

            int storedScore = firstStore.Load("nightwire.ep001", "1.0.0").State.RelationshipScores["anisha"];
            Assert(storedScore == 11 || storedScore == 22,
                "Concurrent new-game creation left an unexpected or overwritten save.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SaveStoreRejectsSymlinkedEpisodeDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-link-root-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-save-link-outside-" + Guid.NewGuid().ToString("N"));
        string episodeDirectory = Path.Combine(root, "nightwire.ep001");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(episodeDirectory, outside);
            bool rejected = false;
            try { new SaveStore(root).Save(CreateState()); }
            catch (SaveStoreException exception) { rejected = exception.ErrorCode == "save_path_unsafe"; }
            Assert(rejected, "Save store followed a symbolic link outside its save root.");
            Assert(!Directory.EnumerateFileSystemEntries(outside).Any(),
                "Save store wrote data through the symbolic link.");
        }
        finally
        {
            if (Directory.Exists(episodeDirectory)) Directory.Delete(episodeDirectory);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void SaveStoreRejectsSymlinkedRootAncestor()
    {
        string parent = Path.Combine(Path.GetTempPath(), "nightwire-save-parent-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-save-outside-" + Guid.NewGuid().ToString("N"));
        string linked = Path.Combine(parent, "linked");
        string saveRoot = Path.Combine(linked, "saves");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(linked, outside);
            bool rejected = false;
            try { _ = new SaveStore(saveRoot); }
            catch (SaveStoreException exception) { rejected = exception.ErrorCode == "save_path_unsafe"; }
            Assert(rejected, "Save store accepted a linked ancestor directory.");
            Assert(!Directory.Exists(Path.Combine(outside, "saves")),
                "Save store created its root through a linked ancestor.");
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }

    private static void SaveStoreRecoversBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            var state = CreateState();
            state.RelationshipScores["anisha"] = 1;
            store.Save(state);
            state.RelationshipScores["anisha"] = 2;
            store.Save(state);

            File.WriteAllText(store.GetSavePath("nightwire.ep001"), "{corrupt");
            File.WriteAllText(store.GetSavePath("nightwire.ep001") + ".restore.tmp", "stale interrupted restore");
            var loaded = store.Load("nightwire.ep001", "1.0.0");
            Assert(loaded.RecoveredFromBackup, "Load did not report backup recovery.");
            Assert(loaded.State.RelationshipScores["anisha"] == 1, "Backup did not restore the last known-good save.");
            Assert(!File.ReadAllText(store.GetSavePath("nightwire.ep001")).StartsWith("{corrupt", StringComparison.Ordinal),
                "Recovered backup was not restored to the primary save path.");
            var secondLoad = store.Load("nightwire.ep001", "1.0.0");
            Assert(!secondLoad.RecoveredFromBackup, "Restored primary save was still treated as corrupt.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SaveStoreRejectsLinkedBackupTemporaryFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-backup-link-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-save-backup-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            var first = CreateState();
            first.RelationshipScores["anisha"] = 1;
            store.Save(first);

            string primary = store.GetSavePath("nightwire.ep001");
            string backupTemporary = primary + ".bak.tmp";
            File.WriteAllText(outside, "must-not-be-overwritten");
            File.CreateSymbolicLink(backupTemporary, outside);

            var second = CreateState();
            second.RelationshipScores["anisha"] = 2;
            bool rejected = false;
            try { store.Save(second); }
            catch (SaveStoreException exception) { rejected = exception.ErrorCode == "save_path_unsafe"; }

            Assert(rejected, "Save accepted a linked backup publication temporary path.");
            Assert(File.ReadAllText(outside) == "must-not-be-overwritten",
                "Save wrote through the linked backup temporary path.");
            Assert(store.Load("nightwire.ep001", "1.0.0").State.RelationshipScores["anisha"] == 1,
                "A rejected backup publication changed the committed primary save.");
        }
        finally
        {
            string backupTemporary = Path.Combine(root, "nightwire.ep001", "save.json.bak.tmp");
            if (File.Exists(backupTemporary)) File.Delete(backupTemporary);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    private static void SaveStoreRejectsLinkedRestoreTemporaryFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-restore-link-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-save-restore-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            var state = CreateState();
            state.RelationshipScores["anisha"] = 1;
            store.Save(state);
            state.RelationshipScores["anisha"] = 2;
            store.Save(state);

            string primary = store.GetSavePath("nightwire.ep001");
            string restoreTemporary = primary + ".restore.tmp";
            File.WriteAllText(primary, "{corrupt");
            File.WriteAllText(outside, "do-not-overwrite");
            File.CreateSymbolicLink(restoreTemporary, outside);

            bool rejected = false;
            try { store.Load("nightwire.ep001", "1.0.0"); }
            catch (SaveStoreException exception) { rejected = exception.ErrorCode == "save_path_unsafe"; }

            Assert(rejected, "Save recovery accepted a linked temporary restore path.");
            Assert(File.ReadAllText(outside) == "do-not-overwrite",
                "Save recovery wrote through the linked temporary restore path.");
        }
        finally
        {
            string restoreTemporary = Path.Combine(root, "nightwire.ep001", "save.json.restore.tmp");
            if (File.Exists(restoreTemporary)) File.Delete(restoreTemporary);
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    private static void SaveStoreRejectsEpisodeMismatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            store.Save(CreateState());
            string mismatchedPath = store.GetSavePath("nightwire.ep002");
            Directory.CreateDirectory(Path.GetDirectoryName(mismatchedPath)!);
            File.Copy(store.GetSavePath("nightwire.ep001"), mismatchedPath);
            bool rejected = false;
            try { store.Load("nightwire.ep002", "1.0.0"); }
            catch (SaveStoreException exception) { rejected = exception.ErrorCode == "episode_id_mismatch"; }
            Assert(rejected, "A save copied under another episode directory was accepted.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }


    private static void SaveStoreRejectsMetadataTampering()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            store.Save(CreateState());
            string path = store.GetSavePath("nightwire.ep001");
            string json = File.ReadAllText(path);
            json = json.Replace("\\"episodeVersion\\":\\"1.0.0\\"", "\\"episodeVersion\\":\\"1.1.0\\"");
            File.WriteAllText(path, json);
            bool rejected = false;
            try { store.Load("nightwire.ep001", "1.1.0"); }
            catch (SaveStoreException exception) { rejected = exception.ErrorCode == "save_invalid"; }
            Assert(rejected, "Edited episode-version metadata bypassed the save checksum.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SaveStoreMigratesEpisodeVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            store.Save(CreateState());
            store.RegisterMigration(new TestEpisodeMigration());
            var loaded = store.Load("nightwire.ep001", "1.1.0");
            Assert(loaded.Migrated, "Load did not report the explicit migration.");
            Assert(loaded.State.EpisodeVersion == "1.1.0", "Episode version was not advanced.");
            Assert(loaded.State.StoryFlags.ContainsKey("migration_applied"), "Migration did not update player state.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }


    private static void SaveStoreNormalizesMigrationFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-migration-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(root);
            store.Save(CreateState());
            store.RegisterMigration(new FailingEpisodeMigration());

            bool normalized = false;
            try { store.Load("nightwire.ep001", "1.1.0"); }
            catch (SaveStoreException exception)
            {
                normalized = exception.ErrorCode == "migration_failed" &&
                    exception.InnerException is InvalidOperationException;
            }

            Assert(normalized, "Migration exception was not converted to the stable migration_failed error.");
            var original = store.Load("nightwire.ep001", "1.0.0");
            Assert(original.State.EpisodeVersion == "1.0.0",
                "A failed in-memory migration modified the persisted save.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void SessionStartsAndPersists()
    {
        string root = FindRepositoryRoot();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(saves);
            var coordinator = new RuntimeSessionCoordinator(store);
            var result = coordinator.StartNewSession(Path.Combine(root, "examples", "Episode_001"));
            Assert(result.Success, result.Error ?? "Session did not start.");
            Assert(coordinator.Progress != null && coordinator.Progress.EpisodeId == "nightwire.ep001",
                "Session did not create episode progress.");
            Assert(coordinator.Progress!.CompletedEventIds.Contains("event.episode_start"),
                "Entry event was not processed.");
            Assert(result.Commands.Count > 0, "Entry event commands were not returned to the host.");
            Assert(File.Exists(store.GetSavePath("nightwire.ep001")), "New session was not saved.");
        }
        finally { if (Directory.Exists(saves)) Directory.Delete(saves, true); }
    }

    private static void RuntimeSessionCloseNotifiesHosts()
    {
        string episode = CopySamplePackage();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-close-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new SaveStoreOptions();
            var coordinator = new RuntimeSessionCoordinator(new SaveStore(saves, options));
            Assert(coordinator.StartNewSession(episode).Success, "Session did not start.");
            int notifications = 0;
            coordinator.SessionClosed += () => throw new InvalidOperationException("Observer failure must be isolated.");
            coordinator.SessionClosed += () => notifications++;
            RuntimeSessionResult? observerCheckpoint = null;
            coordinator.SessionClosed += () => observerCheckpoint = coordinator.SaveNow();

            // Force the next persistence operation to fail after the session has become active.
            options.MaximumSaveBytes = 1;
            RuntimeSessionResult failedSave = coordinator.SaveNow();

            Assert(!failedSave.Success && failedSave.ErrorCode == "save_too_large",
                "The controlled save failure was not reported.");
            Assert(!coordinator.IsActive, "A failed save did not close the runtime session.");
            Assert(notifications == 1, "A failed save must notify hosts exactly once.");
            Assert(observerCheckpoint != null && !observerCheckpoint.Success &&
                observerCheckpoint.ErrorCode == "session_not_active",
                "A close observer re-entered the coordinator and checkpointed a session that was already closed.");
            coordinator.CloseSession();
            Assert(notifications == 1, "Closing an already inactive session emitted another notification.");
        }
        finally
        {
            if (Directory.Exists(episode)) Directory.Delete(episode, true);
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    private static void ClosedSessionCannotCheckpointStaleProgress()
    {
        string episode = CopySamplePackage();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-closed-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore(saves);
            var coordinator = new RuntimeSessionCoordinator(store);
            Assert(coordinator.StartNewSession(episode).Success, "Session did not start.");
            EpisodeProgressState capturedProgress = coordinator.Progress!;
            capturedProgress.RelationshipScores["anisha"] = 7;
            Assert(coordinator.SaveNow().Success, "Initial checkpoint failed.");

            coordinator.CloseSession();
            capturedProgress.RelationshipScores["anisha"] = 99;
            RuntimeSessionResult staleCheckpoint = coordinator.SaveNow();

            Assert(!staleCheckpoint.Success && staleCheckpoint.ErrorCode == "session_not_active",
                "A closed session accepted a new checkpoint.");
            var persisted = store.Load("nightwire.ep001", "1.0.0").State;
            Assert(persisted.RelationshipScores["anisha"] == 7,
                "A stale progress object changed the committed save after session closure.");
        }
        finally
        {
            if (Directory.Exists(episode)) Directory.Delete(episode, true);
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    private static void SessionResumesConversation()
    {
        string root = FindRepositoryRoot();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string episode = Path.Combine(root, "examples", "Episode_001");
            var store = new SaveStore(saves);
            var first = new RuntimeSessionCoordinator(store);
            Assert(first.StartNewSession(episode).Success, "New session did not start.");
            Assert(first.BeginConversation("anisha.day01").Success, "Conversation did not begin.");
            var advanced = first.AdvanceConversation(1.2);
            Assert(advanced.Success && advanced.NewlyDeliveredMessageIds.Count > 0,
                "Conversation did not advance.");
            first.CloseSession();

            var resumed = new RuntimeSessionCoordinator(store);
            var result = resumed.ResumeSession(episode);
            Assert(result.Success, result.Error ?? "Session did not resume.");
            Assert(resumed.Progress!.ConversationProgress.ContainsKey("anisha.day01"),
                "Conversation progress was not restored.");
            Assert(resumed.Progress.ConversationProgress["anisha.day01"].DeliveredMessageIds.Count > 0,
                "Delivered messages were not restored.");
        }
        finally { if (Directory.Exists(saves)) Directory.Delete(saves, true); }
    }

    private static void ConversationDelayTicksAvoidSaveWrites()
    {
        string root = FindRepositoryRoot();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string episode = Path.Combine(root, "examples", "Episode_001");
            var store = new SaveStore(saves);
            var coordinator = new RuntimeSessionCoordinator(store);
            Assert(coordinator.StartNewSession(episode).Success, "New session did not start.");
            Assert(coordinator.BeginConversation("anisha.day01").Success, "Conversation did not begin.");

            string savePath = store.GetSavePath("nightwire.ep001");
            byte[] beforeTick = File.ReadAllBytes(savePath);
            var tick = coordinator.AdvanceConversation(0.2);
            Assert(tick.Success && tick.NewlyDeliveredMessageIds.Count == 0,
                "Short delay tick should not deliver the first message.");
            byte[] afterTick = File.ReadAllBytes(savePath);
            Assert(beforeTick.SequenceEqual(afterTick),
                "A non-delivery frame unexpectedly rewrote the save file.");

            Assert(coordinator.SaveNow().Success, "Explicit checkpoint did not save.");
            var checkpoint = store.Load("nightwire.ep001", coordinator.Progress!.EpisodeVersion);
            Assert(checkpoint.State.ConversationProgress["anisha.day01"].ElapsedDelaySeconds >= 0.2,
                "Explicit checkpoint did not persist the elapsed delay.");
        }
        finally { if (Directory.Exists(saves)) Directory.Delete(saves, true); }
    }

    private static void SessionPreventsOverwrite()
    {
        string root = FindRepositoryRoot();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string episode = Path.Combine(root, "examples", "Episode_001");
            var coordinator = new RuntimeSessionCoordinator(new SaveStore(saves));
            Assert(coordinator.StartNewSession(episode).Success, "Initial session did not start.");
            coordinator.CloseSession();
            var second = coordinator.StartNewSession(episode);
            Assert(!second.Success && second.ErrorCode == "save_exists",
                "Starting a new game overwrote an existing save.");
        }
        finally { if (Directory.Exists(saves)) Directory.Delete(saves, true); }
    }


    private static void CallRuntimeAdvancesTimeline()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        using var doc = JsonDocument.Parse(@"{""call_id"":""call.test"",""duration_ms"":2000,""cues"":[{""cue_id"":""cue.glitch"",""at_ms"":500,""type"":""visual_effect"",""effect"":""signal_glitch""}],""subtitle_tracks"":[{""locale"":""en"",""entries"":[{""start_ms"":0,""end_ms"":700,""text_key"":""call.line""}]}]}");
        package.Calls["call.test"] = doc.RootElement.Clone();
        var state = CreateState();
        state.PendingCallId = "call.test";
        var runtime = new CallRuntime();
        Assert(runtime.Start(package, state, "call.test").Success, "Call did not start.");
        var first = runtime.Advance(package, state, "call.test", 600, "en");
        Assert(first.Success && first.Cues.Count == 1, "Cue was not emitted.");
        Assert(first.Subtitles.Count == 1, "Subtitle did not appear at its authored start time.");
        var second = runtime.Advance(package, state, "call.test", 200, "en");
        Assert(second.Subtitles.Count == 0, "Subtitle was emitted more than once.");
        var final = runtime.Advance(package, state, "call.test", 1200, "en");
        Assert(final.Completed && final.ElapsedMilliseconds == 2000, "Call did not complete.");
    }


    private static void SessionDispatchesCallCompletion()
    {
        string root = FindRepositoryRoot();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new RuntimeSessionCoordinator(new SaveStore(saves));
            Assert(coordinator.StartNewSession(Path.Combine(root, "examples", "Episode_001")).Success,
                "Session did not start.");
            var package = coordinator.Package!;
            using var call = JsonDocument.Parse(@"{""call_id"":""call.test"",""duration_ms"":1000,""cues"":[],""subtitle_tracks"":[]}");
            package.Calls["call.test"] = call.RootElement.Clone();
            package.RuntimeDefinition.CallIds.Add("call.test");
            var completion = new StoryEventDefinition
            {
                EventId = "event.call.test.complete",
                TriggerType = StoryTriggerType.CallEnded,
                TriggerReferenceId = "call.test"
            };
            completion.Actions.Add(new StoryActionDefinition
            {
                Type = StoryActionType.SetStoryFlag,
                Flag = "call_test_finished",
                FlagValue = StoryFlagValue.FromBoolean(true)
            });
            package.RuntimeDefinition.Events.Add(completion);
            coordinator.Progress!.PendingCallId = "call.test";
            Assert(coordinator.StartCallPlayback("call.test").Success, "Call playback did not start.");
            var advanced = coordinator.AdvanceCallPlayback("call.test", 1000, "en");
            Assert(advanced.Success && advanced.CallTimeline != null && advanced.CallTimeline.Completed,
                advanced.Error ?? "Call timeline did not complete.");
            Assert(coordinator.Progress.PendingCallId == null, "Completed call remained pending.");
            Assert(coordinator.Progress.StoryFlags.TryGetValue("call_test_finished", out var flag) &&
                flag.Kind == StoryFlagValueKind.Boolean && flag.BooleanValue,
                "Call completion event did not update story state.");
        }
        finally { if (Directory.Exists(saves)) Directory.Delete(saves, true); }
    }

    private static void MinigameRuntimeSolvesSequence()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        var state = CreateState();
        state.PendingMinigameId = "minigame.signal_sequence";
        var runtime = new MinigameRuntime();
        Assert(runtime.Start(package, state, "minigame.signal_sequence").Success, "Minigame did not start.");
        var wrong = runtime.SubmitSequence(package, state, "minigame.signal_sequence", new[] { "high", "low", "low" });
        Assert(wrong.Success && !wrong.Solved && wrong.AttemptsRemaining == 2, "Wrong sequence was not counted.");
        var solved = runtime.SubmitSequence(package, state, "minigame.signal_sequence", new[] { "low", "high", "low" });
        Assert(solved.Success && solved.Solved && state.MinigameProgress["minigame.signal_sequence"].IsComplete,
            "Correct sequence did not solve the minigame.");
    }


    private static void MinigameRuntimeSolvesFrequency()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        using var doc = JsonDocument.Parse(@"{""minigame_id"":""game.frequency"",""type"":""frequency_tuning"",""attempt_limit"":2,""configuration"":{""minimum_frequency"":80,""maximum_frequency"":120,""target_frequency"":100,""tolerance"":2}}");
        package.Minigames["game.frequency"] = doc.RootElement.Clone();
        var state = CreateState(); state.PendingMinigameId = "game.frequency";
        var runtime = new MinigameRuntime();
        Assert(runtime.Start(package, state, "game.frequency").Success, "Frequency minigame did not start.");
        var result = runtime.SubmitFrequency(package, state, "game.frequency", 101.5);
        Assert(result.Success && result.Solved && state.MinigameProgress["game.frequency"].IsComplete,
            "Frequency inside tolerance did not solve the puzzle.");
    }

    private static void MinigameRuntimeMatchesEvidence()
    {
        string root = FindRepositoryRoot();
        var package = new EpisodePackageLoader().Load(Path.Combine(root, "examples", "Episode_001"));
        using var doc = JsonDocument.Parse(@"{""minigame_id"":""game.evidence"",""type"":""evidence_matching"",""attempt_limit"":4,""configuration"":{""items"":[{""item_id"":""photo""},{""item_id"":""time""},{""item_id"":""place""},{""item_id"":""signal""}],""pairs"":[{""left_item_id"":""photo"",""right_item_id"":""place""},{""left_item_id"":""time"",""right_item_id"":""signal""}]}}");
        package.Minigames["game.evidence"] = doc.RootElement.Clone();
        var state = CreateState(); state.PendingMinigameId = "game.evidence";
        var runtime = new MinigameRuntime();
        Assert(runtime.Start(package, state, "game.evidence").Success, "Evidence minigame did not start.");
        var first = runtime.MatchEvidence(package, state, "game.evidence", "photo", "place");
        Assert(first.Success && !first.Solved, "One matching pair incorrectly completed the puzzle.");
        var second = runtime.MatchEvidence(package, state, "game.evidence", "time", "signal");
        Assert(second.Success && second.Solved && state.MinigameProgress["game.evidence"].IsComplete,
            "All matching pairs did not complete the puzzle.");
    }

    private static void SaveStoreRoundTripsActivities()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = CreateState();
            var call = new CallProgressState { ElapsedMilliseconds = 1250 };
            call.FiredCueIds.Add("cue.one");
            call.FiredSubtitleIds.Add("en|0|700|call.line");
            state.CallProgress.Add("call.one", call);
            var game = new MinigameProgressState { AttemptsUsed = 2, ElapsedSeconds = 14.5, IsComplete = true };
            game.SequenceInput.AddRange(new[] { "low", "high" });
            state.MinigameProgress.Add("game.one", game);
            var store = new SaveStore(root);
            store.Save(state);
            var loaded = store.Load(state.EpisodeId, state.EpisodeVersion).State;
            Assert(loaded.CallProgress["call.one"].ElapsedMilliseconds == 1250 &&
                loaded.CallProgress["call.one"].FiredCueIds.Contains("cue.one") &&
                loaded.CallProgress["call.one"].FiredSubtitleIds.Contains("en|0|700|call.line"), "Call timeline did not round-trip.");
            Assert(loaded.MinigameProgress["game.one"].AttemptsUsed == 2 &&
                loaded.MinigameProgress["game.one"].IsComplete &&
                loaded.MinigameProgress["game.one"].SequenceInput.SequenceEqual(new[] { "low", "high" }),
                "Minigame progress did not round-trip.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }


    private static void SessionDispatchesMinigameCompletion()
    {
        string root = FindRepositoryRoot();
        string saves = Path.Combine(Path.GetTempPath(), "nightwire-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new RuntimeSessionCoordinator(new SaveStore(saves));
            var started = coordinator.StartNewSession(Path.Combine(root, "examples", "Episode_001"));
            Assert(started.Success, started.Error ?? "Session did not start.");
            coordinator.Progress!.PendingMinigameId = "minigame.signal_sequence";
            var begin = coordinator.StartMinigame("minigame.signal_sequence");
            Assert(begin.Success, begin.Error ?? "Minigame did not start.");
            var solved = coordinator.SubmitSequence("minigame.signal_sequence", new[] { "low", "high", "low" });
            Assert(solved.Success && solved.Minigame != null && solved.Minigame.Solved,
                solved.Error ?? "Minigame was not solved.");
            Assert(coordinator.Progress.PendingMinigameId == null, "Solved minigame remained pending.");
            Assert(coordinator.Progress.StoryFlags.TryGetValue("signal_sequence_solved", out var flag) &&
                flag.Kind == StoryFlagValueKind.Boolean && flag.BooleanValue,
                "Minigame completion event did not update story state.");
        }
        finally { if (Directory.Exists(saves)) Directory.Delete(saves, true); }
    }


    private static void AssetResolverEnforcesUnlockAndIntegrity()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-asset-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = CreateAssetTestPackage(root, "original episode image bytes");
            var state = CreateState();
            var resolver = new EpisodeAssetResolver(package, state);
            var locked = resolver.Open("asset.test");
            Assert(!locked.Success && locked.ErrorCode == "asset_locked", "Locked asset was opened.");
            Assert(!resolver.IsUnlocked("asset.test"), "Locked asset reported as unlocked.");

            state.UnlockedAssetIds.Add("asset.test");
            Assert(resolver.IsUnlocked("asset.test"), "Unlocked asset was not recognized.");
            var opened = resolver.Open("asset.test");
            Assert(opened.Success && opened.Asset != null, opened.Error ?? "Unlocked asset did not open.");
            using (opened.Asset!)
            {
                Assert(opened.Asset!.Type == "image" && opened.Asset.MimeType == "image/png", "Asset metadata was not preserved.");
                Assert(opened.Asset.Content.CanRead && !opened.Asset.Content.CanWrite, "Asset stream is not read-only.");
                using (var reader = new StreamReader(opened.Asset.Content, System.Text.Encoding.UTF8, false, 1024, true))
                    Assert(reader.ReadToEnd() == "original episode image bytes", "Asset content did not match the original bytes.");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void AssetResolverRejectsPostLoadTampering()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-asset-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var package = CreateAssetTestPackage(root, "original bytes");
            File.WriteAllText(Path.Combine(root, "Media", "image.bin"), "changed bytes");
            var state = CreateState();
            state.UnlockedAssetIds.Add("asset.test");
            var result = new EpisodeAssetResolver(package, state).Open("asset.test");
            Assert(!result.Success && result.ErrorCode == "asset_size_mismatch" || !result.Success && result.ErrorCode == "asset_hash_mismatch",
                "Asset modified after package load was not rejected.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static LoadedEpisodePackage CreateAssetTestPackage(string root, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, "Media"));
        string relativePath = "Media/image.bin";
        string fullPath = Path.Combine(root, "Media", "image.bin");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(fullPath, bytes);
        using var sha = SHA256.Create();
        string digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();

        var package = new LoadedEpisodePackage
        {
            RootDirectory = root,
            Descriptor = new EpisodePackageDescriptor { EpisodeId = "nightwire.ep001", EpisodeVersion = "1.0.0" }
        };
        package.Manifest.Files.Add(new EpisodeManifestFile
        {
            Path = relativePath, Kind = "media", SizeBytes = bytes.Length, Sha256 = digest
        });
        var asset = new EpisodeManifestAsset
        {
            AssetId = "asset.test", Type = "image", Path = relativePath, MimeType = "image/png"
        };
        package.Manifest.Assets.Add(asset);
        package.AssetsById.Add(asset.AssetId, asset);
        return package;
    }


    private static void EpisodeInstallerInstallsAndUpdates()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "episode.zip");
            CreateSampleArchive(archive);
            string installRoot = Path.Combine(root, "installed");
            var installer = new EpisodePackageInstaller();
            var first = installer.InstallArchive(archive, installRoot);
            Assert(first.Success && !first.UpdatedExisting, first.Error ?? "Initial install failed.");
            Assert(new EpisodePackageLoader().Load(first.InstallDirectory!).Descriptor.EpisodeId == "nightwire.ep001",
                "Installed package failed runtime loading.");
            var second = installer.InstallArchive(archive, installRoot);
            Assert(second.Success && second.UpdatedExisting, second.Error ?? "Update failed.");
            Assert(new EpisodePackageLoader().Load(second.InstallDirectory!).Descriptor.EpisodeVersion == "1.0.0",
                "Updated package failed runtime loading.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EpisodeInstallerRejectsArchiveLinks()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-install-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            int[] linkAttributes =
            {
                unchecked((int)0xA1FF0000), 0x400,
                unchecked((int)0xA1FF0000), 0x400
            };
            string[] entryNames =
            {
                "unix-link", "windows-link",
                "unix-directory-link/", "windows-directory-link/"
            };
            for (int index = 0; index < linkAttributes.Length; index++)
            {
                string archive = Path.Combine(root, "linked-" + index + ".zip");
                using (var file = new FileStream(archive, FileMode.CreateNew))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    var link = zip.CreateEntry(entryNames[index]);
                    link.ExternalAttributes = linkAttributes[index];
                    using (var writer = new StreamWriter(link.Open())) writer.Write("target");
                }

                var result = new EpisodePackageInstaller().InstallArchive(archive,
                    Path.Combine(root, "installed-" + index));
                Assert(!result.Success && result.ErrorCode == "archive_symlink_rejected",
                    "ZIP link entry was not rejected for attributes 0x" + linkAttributes[index].ToString("X8") + ".");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EpisodeInstallerRejectsPathTypeConflicts()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-install-path-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int scenario = 0; scenario < 4; scenario++)
            {
                string archive = Path.Combine(root, "conflict-" + scenario + ".zip");
                using (var file = new FileStream(archive, FileMode.CreateNew))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    if (scenario == 0)
                    {
                        using (var writer = new StreamWriter(zip.CreateEntry("node").Open())) writer.Write("file");
                        using (var writer = new StreamWriter(zip.CreateEntry("node/child").Open())) writer.Write("child");
                    }
                    else if (scenario == 1)
                    {
                        zip.CreateEntry("node/");
                        using (var writer = new StreamWriter(zip.CreateEntry("node").Open())) writer.Write("file");
                    }
                    else if (scenario == 2)
                    {
                        zip.CreateEntry("node/");
                        zip.CreateEntry("node/");
                    }
                    else
                    {
                        using (var writer = new StreamWriter(zip.CreateEntry("node").Open())) writer.Write("file");
                        zip.CreateEntry("node/child/");
                    }
                }

                var result = new EpisodePackageInstaller().InstallArchive(archive,
                    Path.Combine(root, "installed-" + scenario));
                Assert(!result.Success && (result.ErrorCode == "archive_path_type_conflict" ||
                    result.ErrorCode == "duplicate_archive_path"),
                    "ZIP file/directory path collision was not rejected for scenario " + scenario + ".");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EpisodeInstallerRejectsArchiveSpecialFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-install-special-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "special.zip");
            using (var file = new FileStream(archive, FileMode.CreateNew))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var fifo = zip.CreateEntry("named-pipe");
                fifo.ExternalAttributes = unchecked((int)0x11A40000);
                using (var writer = new StreamWriter(fifo.Open())) writer.Write("not a regular file");
            }

            var result = new EpisodePackageInstaller().InstallArchive(archive, Path.Combine(root, "installed"));
            Assert(!result.Success && result.ErrorCode == "archive_special_file_rejected",
                "A ZIP FIFO/special-file entry was not rejected.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EpisodeInstallerRejectsTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "bad.zip");
            using (var file = new FileStream(archive, FileMode.CreateNew))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open()))
                writer.Write("escape");
            var result = new EpisodePackageInstaller().InstallArchive(archive, Path.Combine(root, "installed"));
            Assert(!result.Success && result.ErrorCode == "unsafe_archive_path", "ZIP traversal was not rejected.");
            Assert(!File.Exists(Path.Combine(root, "outside.txt")), "ZIP traversal wrote outside staging.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EpisodeDownloaderRejectsLinkedDestinationAncestor()
    {
        string parent = Path.Combine(Path.GetTempPath(), "nightwire-download-parent-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-download-outside-" + Guid.NewGuid().ToString("N"));
        string linked = Path.Combine(parent, "linked");
        string destination = Path.Combine(linked, "downloads", "episode.zip");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(linked, outside);
            using var downloader = new EpisodePackageDownloader();
            var result = downloader.DownloadAsync(new Uri("https://example.invalid/episode.zip"), destination)
                .GetAwaiter().GetResult();
            Assert(!result.Success && result.ErrorCode == "download_io_failed",
                "Downloader accepted a destination beneath a linked ancestor.");
            Assert(!Directory.Exists(Path.Combine(outside, "downloads")),
                "Downloader created its destination through a linked ancestor.");
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }

    private static void TransportsRejectFollowedRedirects()
    {
        var redirectedUri = new Uri("https://redirected.example.test/payload");
        string root = Path.Combine(Path.GetTempPath(), "nightwire-redirect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var client = new HttpClient(new RedirectedResponseHandler(redirectedUri)))
            using (var downloader = new EpisodePackageDownloader(client: client))
            {
                var result = downloader.DownloadAsync(new Uri("https://cdn.example.test/episode.zip"),
                    Path.Combine(root, "episode.zip")).GetAwaiter().GetResult();
                Assert(!result.Success && result.ErrorCode == "redirect_not_allowed",
                    "Episode downloader accepted an automatically followed redirect.");
            }

            using (var client = new HttpClient(new RedirectedResponseHandler(redirectedUri)))
            using (var transport = new HttpReleaseCatalogueTransport(client))
            {
                bool rejected = false;
                try
                {
                    transport.FetchAsync(new Uri("https://catalogue.example.test/releases.json"),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException) { rejected = true; }
                Assert(rejected, "Catalogue transport accepted an automatically followed redirect.");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void EpisodeDownloaderChecksTrustedHash()
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("episode archive bytes");
        using var sha = SHA256.Create();
        string digest = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", string.Empty).ToLowerInvariant();
        string root = Path.Combine(Path.GetTempPath(), "nightwire-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var handler = new StubHttpHandler(payload);
            using var client = new HttpClient(handler);
            using var downloader = new EpisodePackageDownloader(client: client);
            string target = Path.Combine(root, "episode.zip");
            var result = downloader.DownloadAsync(new Uri("https://cdn.example.test/episode.zip"), target, digest)
                .GetAwaiter().GetResult();
            Assert(result.Success && result.Sha256 == digest && File.ReadAllBytes(target).SequenceEqual(payload),
                result.Error ?? "Verified download failed.");
            var cached = downloader.DownloadAsync(new Uri("https://cdn.example.test/episode.zip"), target,
                digest, expectedSizeBytes: payload.LongLength).GetAwaiter().GetResult();
            Assert(cached.Success && handler.RequestCount == 1,
                "A previously verified signed archive was downloaded again instead of reusing the cache.");
            var bad = downloader.DownloadAsync(new Uri("https://cdn.example.test/episode.zip"), target, new string('0', 64))
                .GetAwaiter().GetResult();
            Assert(!bad.Success && bad.ErrorCode == "download_hash_mismatch", "Incorrect trusted hash was accepted.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void CreateSampleArchive(string archivePath)
    {
        string source = Path.Combine(FindRepositoryRoot(), "examples", "Episode_001");
        using var file = new FileStream(archivePath, FileMode.CreateNew);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = path.Substring(source.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
            using var input = File.OpenRead(path);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private sealed class MemoryReleaseSequenceStore : IReleaseCatalogueSequenceStore
    {
        public long Sequence { get; private set; }
        public long LoadHighestAcceptedSequence() => Sequence;
        public void SaveHighestAcceptedSequence(long sequence) { Sequence = sequence; }
    }

    private sealed class FailingReleaseSequenceStore : IReleaseCatalogueSequenceStore
    {
        public long LoadHighestAcceptedSequence() => 0;
        public void SaveHighestAcceptedSequence(long sequence) =>
            throw new InvalidOperationException("StrongBox persistence is unavailable.");
    }

    private sealed class DeferredCatalogueTransport : IReleaseCatalogueTransport
    {
        private readonly TaskCompletionSource<byte[]> _completion =
            new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]> FetchAsync(Uri catalogueUri, CancellationToken cancellationToken) =>
            _completion.Task;

        public void Complete(byte[] envelope) => _completion.TrySetResult(envelope);
    }

    private sealed class StubCatalogueTransport : IReleaseCatalogueTransport
    {
        private readonly byte[] _envelope;
        public StubCatalogueTransport(byte[] envelope) { _envelope = envelope; }
        public Task<byte[]> FetchAsync(Uri catalogueUri, CancellationToken cancellationToken) =>
            Task.FromResult((byte[])_envelope.Clone());
    }

    private sealed class StubInstallLeaseProvider : IEpisodeInstallLeaseProvider
    {
        public bool TryAcquireInstallLease(string episodeId, out IDisposable? lease)
        {
            lease = new TestLease();
            return true;
        }
        private sealed class TestLease : IDisposable { public void Dispose() { } }
    }

    private sealed class TrackingInstallLeaseProvider : IEpisodeInstallLeaseProvider
    {
        public int AcquireCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool TryAcquireInstallLease(string episodeId, out IDisposable? lease)
        {
            AcquireCount++;
            lease = new TrackingLease(this);
            return true;
        }

        private sealed class TrackingLease : IDisposable
        {
            private TrackingInstallLeaseProvider? _owner;
            public TrackingLease(TrackingInstallLeaseProvider owner) { _owner = owner; }
            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null) owner.DisposeCount++;
            }
        }
    }

    private sealed class CancellationBlockingHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _started =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Cancellation-blocking handler unexpectedly resumed.");
        }
    }

    private sealed class RedirectedResponseHandler : HttpMessageHandler
    {
        private readonly Uri _redirectedUri;
        public RedirectedResponseHandler(Uri redirectedUri) { _redirectedUri = redirectedUri; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("redirected")),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, _redirectedUri)
            });
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        public StubHttpHandler(byte[] payload) { _payload = payload; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload),
                RequestMessage = request
            });
        }
    }


    private static void ReleaseCatalogueVerifiesSignatureAndSequence()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        string issued = DateTime.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture);
        string expires = DateTime.UtcNow.AddHours(2).ToString("o", CultureInfo.InvariantCulture);
        string payloadText = "{\"schema_version\":1,\"sequence\":7,\"issued_utc\":\"" + issued +
            "\",\"expires_utc\":\"" + expires + "\",\"releases\":[{\"episode_id\":\"nightwire.ep001\",\"episode_version\":\"1.0.0\",\"archive_url\":\"https://cdn.example.test/ep001.zip\",\"archive_size_bytes\":1234,\"archive_sha256\":\"" + new string('a', 64) +
            "\",\"minimum_runtime_version\":\"1.0.0\"}]}";
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
        byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string envelopeText = "{\"key_id\":\"release-2026\",\"algorithm\":\"ECDSA-P256-SHA256\",\"payload\":\"" +
            Convert.ToBase64String(payload) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";
        using var nextKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new ReleaseCatalogueVerifier(new[]
        {
            new TrustedReleaseKey { KeyId = "release-2026", SubjectPublicKeyInfo = publicKey },
            new TrustedReleaseKey { KeyId = "release-2027", SubjectPublicKeyInfo = nextKey.ExportSubjectPublicKeyInfo() }
        });
        var verified = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(envelopeText), DateTime.UtcNow, 7);
        Assert(verified.Success && verified.Catalogue!.Releases.Count == 1 &&
            verified.Catalogue.Releases[0].EpisodeId == "nightwire.ep001", verified.Error ?? "Valid signed catalogue rejected.");
        var sequenceStore = new MemoryReleaseSequenceStore();
        var acceptance = new ReleaseCatalogueAcceptanceService(verifier, sequenceStore);
        var accepted = acceptance.Accept(System.Text.Encoding.UTF8.GetBytes(envelopeText), DateTime.UtcNow);
        Assert(accepted.Success && sequenceStore.Sequence == 7, accepted.Error ?? "Accepted catalogue sequence was not persisted.");
        var secondAcceptanceService = new ReleaseCatalogueAcceptanceService(verifier, sequenceStore);
        var repeated = secondAcceptanceService.Accept(System.Text.Encoding.UTF8.GetBytes(envelopeText), DateTime.UtcNow);
        Assert(repeated.Success && sequenceStore.Sequence == 7,
            repeated.Error ?? "A second acceptance-service instance could not safely re-accept the same current catalogue.");

        string olderPayloadText = payloadText.Replace(@"""sequence"":7", @"""sequence"":6");
        byte[] olderPayload = System.Text.Encoding.UTF8.GetBytes(olderPayloadText);
        byte[] olderSignature = key.SignData(olderPayload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string olderEnvelope = JsonSerializer.Serialize(new
        {
            key_id = "release-2026",
            algorithm = "ECDSA-P256-SHA256",
            payload = Convert.ToBase64String(olderPayload),
            signature = Convert.ToBase64String(olderSignature)
        });
        var staleFromSecondService = secondAcceptanceService.Accept(
            System.Text.Encoding.UTF8.GetBytes(olderEnvelope), DateTime.UtcNow);
        Assert(!staleFromSecondService.Success && staleFromSecondService.ErrorCode == "catalogue_rollback" &&
            sequenceStore.Sequence == 7,
            "A second acceptance-service instance bypassed the persisted sequence floor.");

        byte[] rotatedSignature = nextKey.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string rotatedEnvelope = JsonSerializer.Serialize(new
        {
            key_id = "release-2027",
            algorithm = "ECDSA-P256-SHA256",
            payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(rotatedSignature)
        });
        var rotatedKeyResult = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(rotatedEnvelope), DateTime.UtcNow);
        Assert(rotatedKeyResult.Success && rotatedKeyResult.Catalogue!.SigningKeyId == "release-2027",
            rotatedKeyResult.Error ?? "Overlapping trusted-key rotation rejected the newly provisioned key.");

        var failingAcceptance = new ReleaseCatalogueAcceptanceService(verifier, new FailingReleaseSequenceStore());
        var storageFailure = failingAcceptance.Accept(System.Text.Encoding.UTF8.GetBytes(envelopeText), DateTime.UtcNow);
        Assert(!storageFailure.Success && storageFailure.ErrorCode == "sequence_store_failed",
            "A StrongBox persistence exception did not fail catalogue acceptance closed.");

        var rollback = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(envelopeText), DateTime.UtcNow, 8);
        Assert(!rollback.Success && rollback.ErrorCode == "catalogue_rollback", "Old catalogue sequence was accepted.");

        string duplicateEpisodePayloadText = payloadText.Substring(0, payloadText.Length - 2) +
            ",{\"episode_id\":\"nightwire.ep001\",\"episode_version\":\"1.1.0\",\"archive_url\":\"https://cdn.example.test/ep001-v2.zip\",\"archive_size_bytes\":1234,\"archive_sha256\":\"" +
            new string('b', 64) + "\",\"minimum_runtime_version\":\"1.0.0\"}]}";
        byte[] duplicateEpisodePayload = System.Text.Encoding.UTF8.GetBytes(duplicateEpisodePayloadText);
        byte[] duplicateEpisodeSignature = key.SignData(duplicateEpisodePayload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string duplicateEpisodeEnvelope = JsonSerializer.Serialize(new
        {
            key_id = "release-2026",
            algorithm = "ECDSA-P256-SHA256",
            payload = Convert.ToBase64String(duplicateEpisodePayload),
            signature = Convert.ToBase64String(duplicateEpisodeSignature)
        });
        var duplicateEpisodeResult = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(duplicateEpisodeEnvelope), DateTime.UtcNow);
        Assert(!duplicateEpisodeResult.Success && duplicateEpisodeResult.ErrorCode == "release_identity_invalid",
            "A catalogue containing multiple versions of the same episode was accepted.");

        string duplicateEnvelope = envelopeText.Replace(
            @"""key_id"":""release-2026""",
            @"""key_id"":""release-2026"",""key_id"":""release-2026""");
        var duplicateEnvelopeResult = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(duplicateEnvelope), DateTime.UtcNow);
        Assert(!duplicateEnvelopeResult.Success, "Duplicate envelope properties were accepted.");

        string duplicatePayloadText = payloadText.Replace(@"""sequence"":7", @"""sequence"":7,""sequence"":7");
        byte[] duplicatePayload = System.Text.Encoding.UTF8.GetBytes(duplicatePayloadText);
        byte[] duplicateSignature = key.SignData(duplicatePayload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string duplicatePayloadEnvelope = string.Concat(
            "{\"key_id\":\"release-2026\",\"algorithm\":\"ECDSA-P256-SHA256\",\"payload\":\"",
            Convert.ToBase64String(duplicatePayload), "\",\"signature\":\"",
            Convert.ToBase64String(duplicateSignature), "\"}");
        var duplicatePayloadResult = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(duplicatePayloadEnvelope), DateTime.UtcNow);
        Assert(!duplicatePayloadResult.Success, "Duplicate signed-payload properties were accepted.");

        string invalidRuntimePayloadText = payloadText.Replace(
            @"""minimum_runtime_version"":""1.0.0""",
            @"""minimum_runtime_version"":""1.0""");
        byte[] invalidRuntimePayload = System.Text.Encoding.UTF8.GetBytes(invalidRuntimePayloadText);
        byte[] invalidRuntimeSignature = key.SignData(invalidRuntimePayload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string invalidRuntimeEnvelope = string.Concat(
            "{\"key_id\":\"release-2026\",\"algorithm\":\"ECDSA-P256-SHA256\",\"payload\":\"",
            Convert.ToBase64String(invalidRuntimePayload), "\",\"signature\":\"",
            Convert.ToBase64String(invalidRuntimeSignature), "\"}");
        var invalidRuntimeResult = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(invalidRuntimeEnvelope), DateTime.UtcNow);
        Assert(!invalidRuntimeResult.Success && invalidRuntimeResult.ErrorCode == "runtime_version_invalid",
            "Malformed minimum runtime version was accepted.");

        string invalidTimestampPayloadText = payloadText.Replace(issued, issued.TrimEnd('Z'));
        byte[] invalidTimestampPayload = System.Text.Encoding.UTF8.GetBytes(invalidTimestampPayloadText);
        byte[] invalidTimestampSignature = key.SignData(invalidTimestampPayload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string invalidTimestampEnvelope = JsonSerializer.Serialize(new
        {
            key_id = "release-2026",
            algorithm = "ECDSA-P256-SHA256",
            payload = Convert.ToBase64String(invalidTimestampPayload),
            signature = Convert.ToBase64String(invalidTimestampSignature)
        });
        var invalidTimestampResult = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(invalidTimestampEnvelope), DateTime.UtcNow);
        Assert(!invalidTimestampResult.Success, "A catalogue timestamp without an explicit UTC offset was accepted.");

        envelopeText = envelopeText.Replace(Convert.ToBase64String(signature), Convert.ToBase64String(new byte[64]));
        var tampered = verifier.Verify(System.Text.Encoding.UTF8.GetBytes(envelopeText), DateTime.UtcNow);
        Assert(!tampered.Success && tampered.ErrorCode == "signature_invalid", "Invalid catalogue signature was accepted.");
    }

    private static void LibraryPresentationDisposalCancelsRefresh()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new ReleaseCatalogueVerifier(new[]
        {
            new TrustedReleaseKey { KeyId = "library-dispose", SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() }
        });
        var acceptance = new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore());
        var transport = new DeferredCatalogueTransport();
        string root = Path.Combine(Path.GetTempPath(), "nightwire-library-dispose-" + Guid.NewGuid().ToString("N"));
        var manager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
            Path.Combine(root, "installed"), Path.Combine(root, "downloads"),
            transport, acceptance, new StubInstallLeaseProvider(), "1.0.0");
        var presentation = new EpisodeLibraryPresentation(manager);
        int notificationsAfterDispose = 0;
        bool disposed = false;
        presentation.Changed += _ =>
        {
            if (disposed) Interlocked.Increment(ref notificationsAfterDispose);
        };

        Task<ReleaseCatalogueResult> refresh = presentation.RefreshAsync(DateTime.UtcNow);
        disposed = true;
        presentation.Dispose();
        transport.Complete(Array.Empty<byte>());
        var result = refresh.GetAwaiter().GetResult();

        Assert(!result.Success && result.ErrorCode == "catalogue_fetch_cancelled",
            "Disposing the library presentation did not cancel its active catalogue refresh.");
        Assert(notificationsAfterDispose == 0,
            "The disposed presentation published a Changed event after shutdown.");
        Assert(!presentation.Snapshot.IsRefreshing,
            "The disposed presentation retained a stale refreshing state.");
        bool rejected = false;
        try { presentation.RefreshAsync(DateTime.UtcNow).GetAwaiter().GetResult(); }
        catch (ObjectDisposedException) { rejected = true; }
        Assert(rejected, "A disposed library presentation accepted another refresh.");
        manager.Dispose();
    }

    private static void EpisodeAccessGateExcludesSessionAndInstall()
    {
        var access = new EpisodeAccessCoordinator();
        Assert(access.TryAcquireSessionLease("nightwire.ep001", out var sessionLease) && sessionLease != null,
            "Could not acquire an idle episode session lease.");
        Assert(!access.TryAcquireInstallLease("nightwire.ep001", out var blockedInstall) && blockedInstall == null,
            "Installer acquired an episode while its gameplay session was active.");
        Assert(access.TryAcquireInstallLease("nightwire.ep002", out var otherEpisodeInstall) && otherEpisodeInstall != null,
            "An unrelated episode was unnecessarily blocked.");
        otherEpisodeInstall!.Dispose();
        sessionLease!.Dispose();
        Assert(access.TryAcquireInstallLease("nightwire.ep001", out var installLease) && installLease != null,
            "Installer could not acquire the episode after the session closed.");
        Assert(!access.TryAcquireSessionLease("nightwire.ep001", out var blockedSession) && blockedSession == null,
            "A gameplay session opened while installation owned the episode.");
        installLease!.Dispose();
        Assert(access.TryAcquireSessionLease("nightwire.ep001", out var reopenedSession) && reopenedSession != null,
            "Gameplay session could not open after installation released its lease.");
        reopenedSession!.Dispose();
    }

    private static void EpisodeAccessGateCoordinatesInstances()
    {
        var first = new EpisodeAccessCoordinator();
        var second = new EpisodeAccessCoordinator();
        Assert(first.TryAcquireSessionLease("nightwire.ep001", out var session) && session != null,
            "First access coordinator could not acquire the session lease.");
        Assert(!second.TryAcquireInstallLease("NIGHTWIRE.EP001", out var blockedInstall) && blockedInstall == null,
            "A second coordinator bypassed the active session lease using a case variant.");
        session!.Dispose();
        Assert(second.TryAcquireInstallLease("nightwire.ep001", out var install) && install != null,
            "Install lease could not be acquired after the session released access.");
        Assert(!first.TryAcquireSessionLease("NIGHTWIRE.EP001", out var blockedSession) && blockedSession == null,
            "A first coordinator bypassed the active install lease using a case variant.");
        install!.Dispose();
        Assert(first.TryAcquireSessionLease("nightwire.ep001", out var reopened) && reopened != null,
            "Session access was not released across coordinator instances.");
        reopened!.Dispose();
    }

    private static void ReleaseManagerDisposalCancelsRefresh()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new ReleaseCatalogueVerifier(new[]
        {
            new TrustedReleaseKey { KeyId = "dispose-test", SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() }
        });
        var sequenceStore = new MemoryReleaseSequenceStore();
        var acceptance = new ReleaseCatalogueAcceptanceService(verifier, sequenceStore);
        var transport = new DeferredCatalogueTransport();
        var manager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
            Path.Combine(Path.GetTempPath(), "nightwire-dispose-install"),
            Path.Combine(Path.GetTempPath(), "nightwire-dispose-download"),
            transport, acceptance, new StubInstallLeaseProvider(), "1.0.0");
        Task<ReleaseCatalogueResult> refresh = manager.RefreshCatalogueAsync(DateTime.UtcNow);
        Task<ReleaseCatalogueResult> waitingRefresh = manager.RefreshCatalogueAsync(DateTime.UtcNow);
        manager.Dispose();
        transport.Complete(Array.Empty<byte>());
        var result = refresh.GetAwaiter().GetResult();
        var waitingResult = waitingRefresh.GetAwaiter().GetResult();
        Assert(!result.Success && result.ErrorCode == "catalogue_fetch_cancelled",
            "A transport that completed after shutdown was not cancelled before catalogue acceptance.");
        Assert(!waitingResult.Success && waitingResult.ErrorCode == "catalogue_fetch_cancelled",
            "A refresh waiting on the serialization gate did not cancel safely during shutdown.");
        Assert(sequenceStore.LoadHighestAcceptedSequence() == 0,
            "A catalogue sequence was persisted after the release manager had been disposed.");
    }

    private static void ReleaseManagerDisposalCancelsInstall()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-dispose-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string issued = DateTime.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture);
            string expires = DateTime.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture);
            string payloadText = "{\"schema_version\":1,\"sequence\":1,\"issued_utc\":\"" + issued +
                "\",\"expires_utc\":\"" + expires +
                "\",\"releases\":[{\"episode_id\":\"nightwire.ep001\",\"episode_version\":\"1.0.0\",\"archive_url\":\"https://cdn.example.test/episode.zip\",\"archive_size_bytes\":1,\"archive_sha256\":\"" +
                new string('a', 64) + "\",\"minimum_runtime_version\":\"1.0.0\"}]}";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
            byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            string envelope = "{\"key_id\":\"dispose-install\",\"algorithm\":\"ECDSA-P256-SHA256\",\"payload\":\"" +
                Convert.ToBase64String(payload) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";
            var verifier = new ReleaseCatalogueVerifier(new[]
            {
                new TrustedReleaseKey { KeyId = "dispose-install", SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() }
            });
            var acceptance = new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore());
            var leaseProvider = new TrackingInstallLeaseProvider();
            var handler = new CancellationBlockingHttpHandler();
            using var httpClient = new HttpClient(handler);
            using var downloader = new EpisodePackageDownloader(client: httpClient);
            var manager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                Path.Combine(root, "installed"), Path.Combine(root, "downloads"),
                new StubCatalogueTransport(System.Text.Encoding.UTF8.GetBytes(envelope)),
                acceptance, leaseProvider, "1.0.0", downloader);

            var refreshed = manager.RefreshCatalogueAsync(DateTime.UtcNow).GetAwaiter().GetResult();
            Assert(refreshed.Success, refreshed.Error ?? "Could not load the signed release fixture.");
            Task<EpisodeManagerOperationResult> install = manager.InstallOrUpdateAsync("nightwire.ep001");
            Assert(handler.Started.Wait(TimeSpan.FromSeconds(5)), "Episode download did not enter the HTTP transport.");
            Assert(leaseProvider.AcquireCount == 1 && leaseProvider.DisposeCount == 0,
                "Install lease was not held for the active download.");

            manager.Dispose();
            var result = install.GetAwaiter().GetResult();
            Assert(!result.Success && result.ErrorCode == "episode_install_cancelled",
                "Manager shutdown did not cancel the active episode installation.");
            Assert(leaseProvider.DisposeCount == 1,
                "Active install lease was not released exactly once after cancellation.");
            Assert(!Directory.Exists(Path.Combine(root, "installed", "nightwire.ep001")),
                "Episode installation continued after manager shutdown.");
            var afterShutdown = manager.InstallOrUpdateAsync("nightwire.ep001").GetAwaiter().GetResult();
            Assert(!afterShutdown.Success && afterShutdown.ErrorCode == "manager_disposed",
                "Disposed manager accepted a new episode operation.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ReleaseManagersSerializeSharedEpisodeOperation()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-manager-shared-operation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string issued = DateTime.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture);
            string expires = DateTime.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture);
            string payloadText = "{\\"schema_version\\":1,\\"sequence\\":1,\\"issued_utc\\":\\"" + issued +
                "\",\\"expires_utc\\":\\"" + expires +
                "\",\\"releases\\":[{\\"episode_id\\":\\"nightwire.ep001\\",\\"episode_version\\":\\"1.0.0\\",\\"archive_url\\":\\"https://cdn.example.test/episode.zip\\",\\"archive_size_bytes\\":1,\\"archive_sha256\\":\\"" +
                new string('a', 64) + "\",\\"minimum_runtime_version\\":\\"1.0.0\\"}]}";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
            byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            string envelope = "{\\"key_id\\":\\"shared-operation\\",\\"algorithm\\":\\"ECDSA-P256-SHA256\\",\\"payload\\":\\"" +
                Convert.ToBase64String(payload) + "\\",\\"signature\\":\\"" + Convert.ToBase64String(signature) + "\\"}";
            var verifier = new ReleaseCatalogueVerifier(new[]
            {
                new TrustedReleaseKey { KeyId = "shared-operation", SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() }
            });

            var firstHandler = new CancellationBlockingHttpHandler();
            var secondHandler = new CancellationBlockingHttpHandler();
            using var firstClient = new HttpClient(firstHandler);
            using var secondClient = new HttpClient(secondHandler);
            using var firstDownloader = new EpisodePackageDownloader(client: firstClient);
            using var secondDownloader = new EpisodePackageDownloader(client: secondClient);
            var firstLeaseProvider = new TrackingInstallLeaseProvider();
            var secondLeaseProvider = new TrackingInstallLeaseProvider();
            string installRoot = Path.Combine(root, "installed");
            string downloadRoot = Path.Combine(root, "downloads");
            var firstManager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                installRoot, downloadRoot, new StubCatalogueTransport(System.Text.Encoding.UTF8.GetBytes(envelope)),
                new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore()),
                firstLeaseProvider, "1.0.0", firstDownloader);
            var secondManager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                installRoot, downloadRoot, new StubCatalogueTransport(System.Text.Encoding.UTF8.GetBytes(envelope)),
                new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore()),
                secondLeaseProvider, "1.0.0", secondDownloader);

            Assert(firstManager.RefreshCatalogueAsync(DateTime.UtcNow).GetAwaiter().GetResult().Success,
                "First manager could not accept the signed release fixture.");
            Assert(secondManager.RefreshCatalogueAsync(DateTime.UtcNow).GetAwaiter().GetResult().Success,
                "Second manager could not accept the signed release fixture.");
            Task<EpisodeManagerOperationResult> firstInstall = firstManager.InstallOrUpdateAsync("nightwire.ep001");
            Assert(firstHandler.Started.Wait(TimeSpan.FromSeconds(5)), "First manager did not enter its download.");

            var secondResult = secondManager.InstallOrUpdateAsync("nightwire.ep001").GetAwaiter().GetResult();
            Assert(!secondResult.Success && secondResult.ErrorCode == "episode_operation_in_progress",
                "A second manager was allowed to start the same episode operation.");
            Assert(!secondHandler.Started.IsSet,
                "Second manager entered the network download despite an active same-episode operation.");
            Assert(secondLeaseProvider.AcquireCount == 0,
                "Second manager acquired an episode lease despite the shared operation reservation.");

            firstManager.Dispose();
            var firstResult = firstInstall.GetAwaiter().GetResult();
            Assert(!firstResult.Success && firstResult.ErrorCode == "episode_install_cancelled",
                "First manager did not unwind its blocked download after shutdown.");
            secondManager.Dispose();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ReleaseManagersIsolateSharedDownloadRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-manager-download-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        EpisodeReleaseManager? firstManager = null;
        EpisodeReleaseManager? secondManager = null;
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string issued = DateTime.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture);
            string expires = DateTime.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture);
            string payloadText = "{\"schema_version\":1,\"sequence\":1,\"issued_utc\":\"" + issued +
                "\",\"expires_utc\":\"" + expires +
                "\",\"releases\":[{\"episode_id\":\"nightwire.ep001\",\"episode_version\":\"1.0.0\",\"archive_url\":\"https://cdn.example.test/episode.zip\",\"archive_size_bytes\":1,\"archive_sha256\":\"" +
                new string('a', 64) + "\",\"minimum_runtime_version\":\"1.0.0\"}]}";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
            byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            string envelope = JsonSerializer.Serialize(new
            {
                key_id = "download-isolation",
                algorithm = "ECDSA-P256-SHA256",
                payload = Convert.ToBase64String(payload),
                signature = Convert.ToBase64String(signature)
            });
            var verifier = new ReleaseCatalogueVerifier(new[]
            {
                new TrustedReleaseKey { KeyId = "download-isolation", SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo() }
            });
            var firstHandler = new CancellationBlockingHttpHandler();
            var secondHandler = new CancellationBlockingHttpHandler();
            using var firstClient = new HttpClient(firstHandler);
            using var secondClient = new HttpClient(secondHandler);
            using var firstDownloader = new EpisodePackageDownloader(client: firstClient);
            using var secondDownloader = new EpisodePackageDownloader(client: secondClient);
            string sharedDownloadRoot = Path.Combine(root, "downloads");
            firstManager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                Path.Combine(root, "install-a"), sharedDownloadRoot,
                new StubCatalogueTransport(System.Text.Encoding.UTF8.GetBytes(envelope)),
                new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore()),
                new StubInstallLeaseProvider(), "1.0.0", firstDownloader);
            secondManager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                Path.Combine(root, "install-b"), sharedDownloadRoot,
                new StubCatalogueTransport(System.Text.Encoding.UTF8.GetBytes(envelope)),
                new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore()),
                new StubInstallLeaseProvider(), "1.0.0", secondDownloader);

            Assert(firstManager.RefreshCatalogueAsync(DateTime.UtcNow).GetAwaiter().GetResult().Success,
                "First manager could not accept its release fixture.");
            Assert(secondManager.RefreshCatalogueAsync(DateTime.UtcNow).GetAwaiter().GetResult().Success,
                "Second manager could not accept its release fixture.");
            Task<EpisodeManagerOperationResult> first = firstManager.InstallOrUpdateAsync("nightwire.ep001");
            Task<EpisodeManagerOperationResult> second = secondManager.InstallOrUpdateAsync("nightwire.ep001");
            Assert(Task.WaitAll(new[] { firstHandler.Started, secondHandler.Started }, TimeSpan.FromSeconds(5)),
                "Managers sharing a download root did not start independent downloads.");
            firstManager.Dispose();
            secondManager.Dispose();
            Assert(first.GetAwaiter().GetResult().ErrorCode == "episode_install_cancelled" &&
                second.GetAwaiter().GetResult().ErrorCode == "episode_install_cancelled",
                "Disposing managers did not cancel both isolated downloads.");
            string[] managerDirectories = Directory.GetDirectories(sharedDownloadRoot, ".library-*", SearchOption.TopDirectoryOnly);
            Assert(managerDirectories.Length == 2 && !string.Equals(managerDirectories[0], managerDirectories[1], StringComparison.Ordinal),
                "Managers did not receive distinct download cache directories.");
        }
        finally
        {
            firstManager?.Dispose();
            secondManager?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void InstallerRecoveryRestoresBackupOverInvalidDestination()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-installer-invalid-recovery-" + Guid.NewGuid().ToString("N"));
        string installRoot = Path.Combine(root, "installed");
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "episode.zip");
            CreateSampleArchive(archive);
            var installer = new EpisodePackageInstaller();
            var installed = installer.InstallArchive(archive, installRoot);
            Assert(installed.Success, installed.Error ?? "Could not create the valid installed package.");

            string backup = Path.Combine(installRoot,
                ".backup-nightwire.ep001-" + Guid.NewGuid().ToString("N"));
            Directory.Move(installed.InstallDirectory!, backup);
            string invalidBackup = Path.Combine(installRoot,
                ".backup-nightwire.ep001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(invalidBackup);
            File.WriteAllText(Path.Combine(invalidBackup, "not-a-package.marker"), "invalid backup");
            Directory.SetLastWriteTimeUtc(backup, DateTime.UtcNow.AddMinutes(-2));
            Directory.SetLastWriteTimeUtc(invalidBackup, DateTime.UtcNow);
            string destination = Path.Combine(installRoot, "nightwire.ep001");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "partial.marker"), "damaged replacement");

            var recovery = installer.RecoverInterruptedInstalls(installRoot);
            Assert(recovery.RestoredEpisodeIds.Contains("nightwire.ep001"),
                "Recovery did not restore the backup over the invalid destination.");
            var restored = new EpisodePackageLoader().Load(destination);
            Assert(restored.Descriptor.EpisodeId == "nightwire.ep001" &&
                restored.Descriptor.EpisodeVersion == "1.0.0",
                "Recovery did not restore a fully loadable previous episode.");
            Assert(!File.Exists(Path.Combine(destination, "partial.marker")),
                "The invalid destination remained active after recovery.");
            Assert(!Directory.Exists(backup), "Successfully restored backup was not consumed.");
            Assert(!Directory.Exists(invalidBackup), "Obsolete invalid backup was not cleaned after successful recovery.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void InstallerRecoveryCleansValidatedQuarantine()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-quarantine-recovery-" + Guid.NewGuid().ToString("N"));
        string installRoot = Path.Combine(root, "installed");
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "episode.zip");
            CreateSampleArchive(archive);
            var installer = new EpisodePackageInstaller();
            var installed = installer.InstallArchive(archive, installRoot);
            Assert(installed.Success, installed.Error ?? "Could not create the active episode fixture.");

            string quarantine = Path.Combine(installRoot,
                ".invalid-nightwire.ep001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(quarantine);
            File.WriteAllText(Path.Combine(quarantine, "invalid.marker"), "orphaned quarantine");

            var recovery = installer.RecoverInterruptedInstalls(installRoot);
            Assert(!Directory.Exists(quarantine),
                "Recovery did not clean a quarantine after validating its active episode.");
            Assert(new EpisodePackageLoader().Load(installed.InstallDirectory!).Descriptor.EpisodeId == "nightwire.ep001",
                "Quarantine cleanup damaged the active installed episode.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ReleaseManagerRecoversBeforeLibraryAccess()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-manager-recovery-" + Guid.NewGuid().ToString("N"));
        string installRoot = Path.Combine(root, "installed");
        Directory.CreateDirectory(installRoot);
        try
        {
            string backup = Path.Combine(installRoot,
                ".backup-nightwire.ep001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "previous.marker"), "preserve");
            string staging = Path.Combine(installRoot, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "partial.marker"), "discard");

            var verifier = new ReleaseCatalogueVerifier(Array.Empty<TrustedReleaseKey>());
            var acceptance = new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore());
            var manager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                installRoot, Path.Combine(root, "downloads"),
                new StubCatalogueTransport(Array.Empty<byte>()), acceptance,
                new StubInstallLeaseProvider(), "1.0.0");

            // Library discovery must recover even when the host omitted the explicit startup call.
            var library = manager.GetLibrary();
            string restored = Path.Combine(installRoot, "nightwire.ep001");
            Assert(File.Exists(Path.Combine(restored, "previous.marker")),
                "Library discovery ran before the interrupted backup was restored.");
            Assert(!Directory.Exists(staging),
                "Library discovery left an abandoned staging directory behind.");
            Assert(library.Count == 1 && library[0].Status == EpisodeLibraryStatus.InstalledPackageInvalid,
                "Recovered but invalid content was not represented as an invalid installed episode.");

            var first = manager.RecoverAtStartup();
            var second = manager.RecoverAtStartup();
            Assert(first.RestoredEpisodeIds.Contains("nightwire.ep001") &&
                second.RestoredEpisodeIds.Contains("nightwire.ep001"),
                "Startup recovery was not cached as a single completed operation.");
            Assert(File.Exists(Path.Combine(restored, "previous.marker")),
                "Repeated startup recovery changed the restored installation.");
            manager.Dispose();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ReleaseManagerInstallsOfflineEpisode()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-release-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archivePath = Path.Combine(root, "episode.zip");
            CreateSampleArchive(archivePath);
            byte[] archiveBytes = File.ReadAllBytes(archivePath);
            using var sha = SHA256.Create();
            string archiveHash = BitConverter.ToString(sha.ComputeHash(archiveBytes)).Replace("-", string.Empty).ToLowerInvariant();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] publicKey = key.ExportSubjectPublicKeyInfo();
            string issued = DateTime.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture);
            string expires = DateTime.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture);
            string payloadText = "{\"schema_version\":1,\"sequence\":1,\"issued_utc\":\"" + issued +
                "\",\"expires_utc\":\"" + expires + "\",\"releases\":[{\"episode_id\":\"nightwire.ep001\",\"episode_version\":\"1.0.0\",\"archive_url\":\"https://cdn.example.test/episode.zip\",\"archive_size_bytes\":" +
                archiveBytes.Length + ",\"archive_sha256\":\"" + archiveHash + "\",\"minimum_runtime_version\":\"0.1.0\"}]}";
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
            byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            string envelope = "{\"key_id\":\"test-key\",\"algorithm\":\"ECDSA-P256-SHA256\",\"payload\":\"" +
                Convert.ToBase64String(payload) + "\",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";
            var verifier = new ReleaseCatalogueVerifier(new[]
            {
                new TrustedReleaseKey { KeyId = "test-key", SubjectPublicKeyInfo = publicKey }
            });
            var acceptance = new ReleaseCatalogueAcceptanceService(verifier, new MemoryReleaseSequenceStore());
            using var httpClient = new HttpClient(new StubHttpHandler(archiveBytes));
            using var downloader = new EpisodePackageDownloader(client: httpClient);
            var manager = new EpisodeReleaseManager(new Uri("https://catalogue.example.test/releases.json"),
                Path.Combine(root, "installed"), Path.Combine(root, "downloads"),
                new StubCatalogueTransport(System.Text.Encoding.UTF8.GetBytes(envelope)),
                acceptance, new StubInstallLeaseProvider(), "1.0.0", downloader);
            var refreshed = manager.RefreshCatalogueAsync(DateTime.UtcNow).GetAwaiter().GetResult();
            Assert(refreshed.Success, refreshed.Error ?? "Signed catalogue refresh failed.");
            var result = manager.InstallOrUpdateAsync("nightwire.ep001").GetAwaiter().GetResult();
            Assert(result.Success, result.Error ?? "Episode release manager failed to install the episode.");
            var library = manager.GetLibrary();
            Assert(library.Count == 1 && library[0].Status == EpisodeLibraryStatus.InstalledOffline &&
                library[0].InstalledVersion == "1.0.0", "Installed episode was not recognized as available offline.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void InstallerRejectsReleaseIdentityMismatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-release-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "episode.zip");
            CreateSampleArchive(archive);
            string installRoot = Path.Combine(root, "installed");
            var installer = new EpisodePackageInstaller();
            var first = installer.InstallArchive(archive, installRoot);
            Assert(first.Success, first.Error ?? "Initial package install failed.");
            string marker = Path.Combine(first.InstallDirectory!, "preserve.marker");
            File.WriteAllText(marker, "keep-old-install");

            var mismatch = installer.InstallArchive(archive, installRoot, "nightwire.ep001", "9.9.9");
            Assert(!mismatch.Success && mismatch.ErrorCode == "release_episode_version_mismatch",
                "Installer accepted a package version that did not match the signed release.");
            Assert(File.Exists(marker) && File.ReadAllText(marker) == "keep-old-install",
                "Rejected release replaced the previously installed episode.");

            string invalidArchive = Path.Combine(root, "invalid-update.zip");
            File.WriteAllText(invalidArchive, "not a ZIP archive");
            var invalid = installer.InstallArchive(invalidArchive, installRoot, "nightwire.ep001");
            Assert(!invalid.Success, "Installer accepted a corrupt replacement archive.");
            Assert(File.Exists(marker) && File.ReadAllText(marker) == "keep-old-install",
                "A corrupt replacement archive damaged the previously installed episode.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void InstallerRejectsSymlinkedRootAncestor()
    {
        string parent = Path.Combine(Path.GetTempPath(), "nightwire-install-parent-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "nightwire-install-outside-" + Guid.NewGuid().ToString("N"));
        string linked = Path.Combine(parent, "linked");
        string installRoot = Path.Combine(linked, "episodes");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(linked, outside);
            bool rejected = false;
            try { new EpisodePackageInstaller().RecoverInterruptedInstalls(installRoot); }
            catch (EpisodeInstallException exception) { rejected = exception.ErrorCode == "unsafe_install_root"; }
            Assert(rejected, "Installer accepted a linked ancestor directory.");
            Assert(!Directory.Exists(Path.Combine(outside, "episodes")),
                "Installer created its root through a linked ancestor.");
        }
        finally
        {
            if (Directory.Exists(linked)) Directory.Delete(linked);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
            if (Directory.Exists(parent)) Directory.Delete(parent, true);
        }
    }

    private static void InstallerRecoveryRestoresPreviousInstall()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string backup = Path.Combine(root, ".backup-nightwire.ep001-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "previous-install.marker"), "preserve");
            string staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "partial.marker"), "discard");
            var result = new EpisodePackageInstaller().RecoverInterruptedInstalls(root);
            string restored = Path.Combine(root, "nightwire.ep001");
            Assert(result.RestoredEpisodeIds.Contains("nightwire.ep001"), "Interrupted episode backup was not restored.");
            Assert(File.Exists(Path.Combine(restored, "previous-install.marker")), "Restored installation content is missing.");
            Assert(!Directory.Exists(staging) && result.RemovedStagingDirectories == 1,
                "Incomplete staging directory was not cleaned.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FailingEpisodeMigration : IEpisodeProgressMigration
    {
        public string EpisodeId => "nightwire.ep001";
        public string FromEpisodeVersion => "1.0.0";
        public string ToEpisodeVersion => "1.1.0";
        public void Migrate(EpisodeProgressState state)
        {
            state.StoryFlags["migration_partial_change"] = StoryFlagValue.FromBoolean(true);
            throw new InvalidOperationException("Simulated migration failure.");
        }
    }

    private sealed class TestEpisodeMigration : IEpisodeProgressMigration
    {
        public string EpisodeId => "nightwire.ep001";
        public string FromEpisodeVersion => "1.0.0";
        public string ToEpisodeVersion => "1.1.0";
        public void Migrate(EpisodeProgressState state)
        {
            state.StoryFlags["migration_applied"] = StoryFlagValue.FromBoolean(true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "examples", "Episode_001")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate NIGHTWIRE repository root.");
    }

    private static string CopySamplePackage()
    {
        string source = Path.Combine(FindRepositoryRoot(), "examples", "Episode_001");
        string target = Path.Combine(Path.GetTempPath(), "nightwire-test-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(source, target);
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static EpisodeRuntimeDefinition CreateEpisode()
    {
        var episode = new EpisodeRuntimeDefinition
        {
            EpisodeId = "nightwire.ep001",
            EpisodeVersion = "1.0.0",
            EntryEventId = "event.start"
        };
        episode.ConversationIds.Add("conversation.opening");
        episode.CallIds.Add("call.anisha");
        episode.AssetIds.Add("asset.photo");

        var start = new StoryEventDefinition
        {
            EventId = "event.start",
            TriggerType = StoryTriggerType.EpisodeStarted
        };
        start.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.OpenConversation,
            TargetId = "conversation.opening"
        });
        start.Actions.Add(new StoryActionDefinition
        {
            Type = StoryActionType.SetStoryFlag,
            Flag = "episode_started",
            FlagValue = StoryFlagValue.FromBoolean(true)
        });
        episode.Events.Add(start);
        return episode;
    }

    private static EpisodeProgressState CreateState()
    {
        return new EpisodeProgressState
        {
            EpisodeId = "nightwire.ep001",
            EpisodeVersion = "1.0.0"
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

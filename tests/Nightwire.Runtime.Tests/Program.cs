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
        Run("episode loader rejects an older runtime", EpisodeLoaderRejectsOldRuntime, ref failures);
        Run("conversation runtime advances by authored delay and persists read state", ConversationRuntimePersistsProgress, ref failures);
        Run("conversation choices select the authored branch", ConversationRuntimeBranchesOnChoice, ref failures);
        Run("choice effects update story flags and relationship scores", ConversationChoiceEffectsPersist, ref failures);
        Run("save store round-trips player progress", SaveStoreRoundTripsProgress, ref failures);
        Run("save store recovers from a corrupt primary using backup", SaveStoreRecoversBackup, ref failures);
        Run("save store rejects a different episode identity", SaveStoreRejectsEpisodeMismatch, ref failures);
        Run("save store detects episode-version metadata tampering", SaveStoreRejectsMetadataTampering, ref failures);
        Run("save store applies an explicit episode migration", SaveStoreMigratesEpisodeVersion, ref failures);
        Run("session coordinator starts an episode and persists its entry event", SessionStartsAndPersists, ref failures);
        Run("session coordinator resumes conversation progress", SessionResumesConversation, ref failures);
        Run("session coordinator prevents accidental new-game overwrite", SessionPreventsOverwrite, ref failures);
        Run("prerecorded call timeline emits cues and completes", CallRuntimeAdvancesTimeline, ref failures);
        Run("sequence minigame validates and solves authored input", MinigameRuntimeSolvesSequence, ref failures);
        Run("save store round-trips call and minigame progress", SaveStoreRoundTripsActivities, ref failures);
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
        Assert(first.Subtitles.Count == 0, "Subtitle fired too early.");
        var second = runtime.Advance(package, state, "call.test", 200, "en");
        Assert(second.Subtitles.Count == 1, "Subtitle was not emitted.");
        var final = runtime.Advance(package, state, "call.test", 1200, "en");
        Assert(final.Completed && final.ElapsedMilliseconds == 2000, "Call did not complete.");
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

    private static void SaveStoreRoundTripsActivities()
    {
        string root = Path.Combine(Path.GetTempPath(), "nightwire-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = CreateState();
            var call = new CallProgressState { ElapsedMilliseconds = 1250 };
            call.FiredCueIds.Add("cue.one");
            state.CallProgress.Add("call.one", call);
            var game = new MinigameProgressState { AttemptsUsed = 2, ElapsedSeconds = 14.5, IsComplete = true };
            game.SequenceInput.AddRange(new[] { "low", "high" });
            state.MinigameProgress.Add("game.one", game);
            var store = new SaveStore(root);
            store.Save(state);
            var loaded = store.Load(state.EpisodeId, state.EpisodeVersion).State;
            Assert(loaded.CallProgress["call.one"].ElapsedMilliseconds == 1250 &&
                loaded.CallProgress["call.one"].FiredCueIds.Contains("cue.one"), "Call timeline did not round-trip.");
            Assert(loaded.MinigameProgress["game.one"].AttemptsUsed == 2 &&
                loaded.MinigameProgress["game.one"].IsComplete &&
                loaded.MinigameProgress["game.one"].SequenceInput.SequenceEqual(new[] { "low", "high" }),
                "Minigame progress did not round-trip.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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

using System;
using System.IO;
using System.Linq;
using Nightwire.Runtime.Loading;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Progression;
using Nightwire.Runtime.Conversations;

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

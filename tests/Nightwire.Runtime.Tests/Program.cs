using System;
using Nightwire.Runtime.Domain;
using Nightwire.Runtime.Progression;

internal static class Program
{
    private static int Main()
    {
        var failures = 0;
        Run("episode-start event opens conversation and sets flag", EpisodeStartExecutes, ref failures);
        Run("one-shot event cannot execute twice", OneShotEventIsIdempotent, ref failures);
        Run("invalid action batch does not partially mutate state", InvalidBatchIsAtomic, ref failures);
        Run("asset unlock and call start produce typed commands", AssetAndCallActionsExecute, ref failures);
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
        Assert(state.StoryFlags.TryGetValue("episode_started", out var value) && value, "Story flag was not set.");
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
            Value = true
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
            Value = true
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

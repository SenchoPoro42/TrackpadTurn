using TrackpadTurn.Core;

namespace TrackpadTurn.Core.Tests;

// Ported one-to-one from Tests/TrackpadTurnCoreTests/PressTrackerTests.swift.
public class PressTrackerTests
{
    private readonly TurnSettings settings = new();
    private const double Hold = PressTracker.HoldBeforeRunning;

    [Fact]
    public void HeldPressRunsUntilReleased()
    {
        var tracker = new PressTracker();
        var deadline = tracker.PressBegan(10, settings);
        Assert.Equal(10 + Hold, deadline);
        Assert.Equal([PressAction.RunBegan], tracker.CheckHold(10 + Hold));
        Assert.True(tracker.IsRunning);
        Assert.Equal([PressAction.RunEnded], tracker.PressEnded());
        Assert.False(tracker.IsTracking);
    }

    [Fact]
    public void QuickClickStaysAPlainRightClick()
    {
        var tracker = new PressTracker();
        tracker.PressBegan(10, settings);
        Assert.Empty(tracker.PressEnded());
        // The hold timer fires after the release: nothing may start.
        Assert.Empty(tracker.CheckHold(10 + Hold));
    }

    [Fact]
    public void EarlyCheckDoesNotRun()
    {
        var tracker = new PressTracker();
        tracker.PressBegan(10, settings);
        Assert.Empty(tracker.CheckHold(10 + Hold / 2));
        Assert.False(tracker.IsRunning);
    }

    [Fact]
    public void RunStartsOnlyOnce()
    {
        var tracker = new PressTracker();
        tracker.PressBegan(10, settings);
        Assert.Equal([PressAction.RunBegan], tracker.CheckHold(10 + Hold));
        Assert.Empty(tracker.CheckHold(10 + Hold * 2));
    }

    [Fact]
    public void StaleTimerFromEarlierPressCannotStartLaterOne()
    {
        var tracker = new PressTracker();
        tracker.PressBegan(10, settings);
        tracker.PressEnded();
        tracker.PressBegan(10 + Hold * 0.9, settings);
        // The first press's timer fires; the second press has not been held long enough.
        Assert.Empty(tracker.CheckHold(10 + Hold));
    }

    [Fact]
    public void SettingOffNeverRuns()
    {
        var tracker = new PressTracker();
        var off = settings with { PressToRunEnabled = false };
        Assert.Null(tracker.PressBegan(10, off));
        Assert.Empty(tracker.CheckHold(20));
    }

    [Fact]
    public void AppDisabledNeverRuns()
    {
        var tracker = new PressTracker();
        var off = settings with { IsEnabled = false };
        Assert.Null(tracker.PressBegan(10, off));
    }
}

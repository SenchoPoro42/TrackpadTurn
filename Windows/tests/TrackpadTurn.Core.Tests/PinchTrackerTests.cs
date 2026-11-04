using TrackpadTurn.Core;
using static TrackpadTurn.Core.GesturePhase;

namespace TrackpadTurn.Core.Tests;

// Ported one-to-one from Tests/TrackpadTurnCoreTests/PinchTrackerTests.swift.
public class PinchTrackerTests
{
    private readonly TurnSettings settings = new();
    private const double Step = PinchTracker.MagnificationPerStep;

    [Fact]
    public void FingersApartZoomsIn()
    {
        var tracker = new PinchTracker();
        Assert.Equal(2, tracker.Consume(new PinchSample(Began, Step * 2), settings));
    }

    [Fact]
    public void FingersTogetherZoomsOut()
    {
        var tracker = new PinchTracker();
        Assert.Equal(-3, tracker.Consume(new PinchSample(Began, -Step * 3), settings));
    }

    [Fact]
    public void SmallChangesAccumulate()
    {
        var tracker = new PinchTracker();
        var steps = tracker.Consume(new PinchSample(Began, Step * 0.6), settings);
        steps += tracker.Consume(new PinchSample(Changed, Step * 0.6), settings);
        Assert.Equal(1, steps);
    }

    [Fact]
    public void LeftoverDoesNotCarryIntoNextPinch()
    {
        var tracker = new PinchTracker();
        tracker.Consume(new PinchSample(Began, Step * 0.9), settings);
        tracker.Consume(new PinchSample(Ended, 0), settings);
        Assert.Equal(0, tracker.Consume(new PinchSample(Began, Step * 0.2), settings));
    }

    [Fact]
    public void ZoomSpeedScalesSteps()
    {
        var tracker = new PinchTracker();
        var fast = settings with { ZoomSpeed = 2 };
        Assert.Equal(4, tracker.Consume(new PinchSample(Began, Step * 2), fast));
    }

    [Fact]
    public void PinchOffDoesNothing()
    {
        var tracker = new PinchTracker();
        var off = settings with { PinchZoomEnabled = false };
        Assert.Equal(0, tracker.Consume(new PinchSample(Began, 1), off));
    }
}

public class TurnSettingsTests
{
    [Fact]
    public void OlderSavedSettingsKeepDefaultsForNewFields()
    {
        var settings = TurnSettings.FromJson("""{"turnSpeed": 2.0}""");
        Assert.Equal(2.0, settings.TurnSpeed);
        Assert.True(settings.IsEnabled);
        Assert.True(settings.PinchZoomEnabled);
    }

    [Fact]
    public void OutOfRangeSpeedsAreClamped()
    {
        var settings = TurnSettings.FromJson("""{"turnSpeed": 99, "zoomSpeed": 0}""");
        Assert.Equal(TurnSettings.TurnSpeedRange.Max, settings.TurnSpeed);
        Assert.Equal(TurnSettings.ZoomSpeedRange.Min, settings.ZoomSpeed);
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var settings = new TurnSettings { TurnSpeed = 1.5, ReverseVertical = true, PressToRunEnabled = false };
        Assert.Equal(settings, TurnSettings.FromJson(settings.ToJson()));
    }
}

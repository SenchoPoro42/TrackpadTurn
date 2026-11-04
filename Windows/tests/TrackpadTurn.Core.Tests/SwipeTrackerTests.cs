using TrackpadTurn.Core;
using static TrackpadTurn.Core.GesturePhase;

namespace TrackpadTurn.Core.Tests;

// Ported one-to-one from Tests/TrackpadTurnCoreTests/SwipeTrackerTests.swift.
public class SwipeTrackerTests
{
    private readonly TurnSettings settings = new();

    private static readonly SwipeAction SwipeBegan = SwipeAction.SwipeBegan;
    private static readonly SwipeAction SwipeEnded = SwipeAction.SwipeEnded;
    private static SwipeAction.Look Look(int dx, int dy) => new SwipeAction.Look(dx, dy);
    private static SwipeAction[] Actions(params SwipeAction[] actions) => actions;

    // Lifecycle

    [Fact]
    public void FullSwipeSendsBeginLookEnd()
    {
        var tracker = new SwipeTracker();
        Assert.Equal(new SwipeResponse(), tracker.Consume(new ScrollSample(MayBegin), settings));
        Assert.Equal(
            new SwipeResponse(Actions(SwipeBegan, Look(10, 0)), ScrollDisposition.ForwardAfterActions),
            tracker.Consume(new ScrollSample(Began, DeltaX: 10), settings));
        Assert.Equal(
            new SwipeResponse(Actions(Look(4, 0)), ScrollDisposition.Forward),
            tracker.Consume(new ScrollSample(Changed, DeltaX: 4), settings));
        Assert.Equal(
            new SwipeResponse(Actions(SwipeEnded), ScrollDisposition.Forward),
            tracker.Consume(new ScrollSample(Ended), settings));
        Assert.False(tracker.IsTracking);
    }

    [Fact]
    public void ChangedWithoutBeganStillBeginsOnce()
    {
        var tracker = new SwipeTracker();
        Assert.Equal(Actions(SwipeBegan, Look(3, 0)), tracker.Consume(new ScrollSample(Changed, DeltaX: 3), settings).Actions);
        Assert.Equal(Actions(Look(3, 0)), tracker.Consume(new ScrollSample(Changed, DeltaX: 3), settings).Actions);
    }

    [Fact]
    public void CancelledEndsSwipe()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 5), settings);
        Assert.Equal(Actions(SwipeEnded), tracker.Consume(new ScrollSample(Cancelled), settings).Actions);
    }

    [Fact]
    public void BeganWhileSwipingClosesTheLostSwipeFirst()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 5), settings);
        // The `Ended` went to another app's window; the next swipe must re-signal the addon.
        Assert.Equal(
            Actions(SwipeEnded, SwipeBegan, Look(5, 0)),
            tracker.Consume(new ScrollSample(Began, DeltaX: 5), settings).Actions);
    }

    [Fact]
    public void FinishingTwiceSendsOneRelease()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 5), settings);
        Assert.Equal(Actions(SwipeEnded), tracker.Finish());
        Assert.Empty(tracker.Consume(new ScrollSample(Ended), settings).Actions);
    }

    // Start threshold

    [Fact]
    public void TinyMovementNeverStartsSwipe()
    {
        var tracker = new SwipeTracker();
        Assert.Equal(new SwipeResponse([], ScrollDisposition.Drop), tracker.Consume(new ScrollSample(Began, DeltaX: 1), settings));
        Assert.Equal(new SwipeResponse([], ScrollDisposition.Drop), tracker.Consume(new ScrollSample(Changed, DeltaY: 1), settings));
        Assert.True(tracker.IsTracking);
        Assert.Equal(new SwipeResponse(), tracker.Consume(new ScrollSample(Ended), settings));
        Assert.False(tracker.IsTracking);
    }

    [Fact]
    public void TravelBeforeStartIsNotLost()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 2), settings);
        Assert.Equal(
            new SwipeResponse(Actions(SwipeBegan, Look(4, 0)), ScrollDisposition.ForwardAfterActions),
            tracker.Consume(new ScrollSample(Changed, DeltaX: 2), settings));
    }

    [Fact]
    public void StartThresholdIsDistanceNotPerAxis()
    {
        var tracker = new SwipeTracker();
        // 2.5 points on each axis is ~3.5 points of travel.
        Assert.Equal(SwipeBegan, tracker.Consume(new ScrollSample(Began, DeltaX: 2.5, DeltaY: -2.5), settings).Actions[0]);
    }

    // Glide, devices, settings

    [Fact]
    public void MomentumAfterLiftIsDroppedWithoutTurning()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 5), settings);
        tracker.Consume(new ScrollSample(Ended), settings);
        var glide = new ScrollSample(None, MomentumPhase.Changed, DeltaX: 50);
        Assert.Equal(new SwipeResponse([], ScrollDisposition.Drop), tracker.Consume(glide, settings));
    }

    [Fact]
    public void WheelMouseIsIgnored()
    {
        var tracker = new SwipeTracker();
        var wheel = new ScrollSample(None, IsContinuous: false, DeltaY: 3);
        Assert.Equal(new SwipeResponse(), tracker.Consume(wheel, settings));
    }

    [Fact]
    public void DisabledLeavesEverythingAlone()
    {
        var tracker = new SwipeTracker();
        var off = settings with { IsEnabled = false };
        Assert.Equal(new SwipeResponse(), tracker.Consume(new ScrollSample(Began, DeltaX: 5), off));
        var glide = new ScrollSample(None, MomentumPhase.Changed, DeltaY: 9);
        Assert.Equal(new SwipeResponse(), tracker.Consume(glide, off));
    }

    // Direction

    [Fact]
    public void NaturalScrollingFingersRightLooksRight()
    {
        var tracker = new SwipeTracker();
        // Natural scrolling: content follows the fingers, deltas equal finger travel.
        var sample = new ScrollSample(Began, DeltaX: 8, IsDirectionInvertedFromDevice: true);
        Assert.Equal(Actions(SwipeBegan, Look(8, 0)), tracker.Consume(sample, settings).Actions);
    }

    [Fact]
    public void TraditionalScrollingFingersRightLooksRight()
    {
        var tracker = new SwipeTracker();
        // Traditional scrolling: macOS reports the opposite sign for the same finger motion.
        var sample = new ScrollSample(Began, DeltaX: -8, IsDirectionInvertedFromDevice: false);
        Assert.Equal(Actions(SwipeBegan, Look(8, 0)), tracker.Consume(sample, settings).Actions);
    }

    [Fact]
    public void FingersUpLooksUpWithDampedPitch()
    {
        var tracker = new SwipeTracker();
        // Screen y grows downward, so fingers moving up are negative, like a mouse moving up.
        var sample = new ScrollSample(Began, DeltaY: -100);
        Assert.Equal(Actions(SwipeBegan, Look(0, -77)), tracker.Consume(sample, settings).Actions);
    }

    [Fact]
    public void ReverseTogglesFlipEachAxis()
    {
        var tracker = new SwipeTracker();
        var reversed = settings with { ReverseHorizontal = true, ReverseVertical = true };
        var sample = new ScrollSample(Began, DeltaX: 10, DeltaY: -100);
        Assert.Equal(Actions(SwipeBegan, Look(-10, 77)), tracker.Consume(sample, reversed).Actions);
    }

    [Fact]
    public void TiltOffKeepsCameraLevel()
    {
        var tracker = new SwipeTracker();
        var flat = settings with { TiltEnabled = false };
        var sample = new ScrollSample(Began, DeltaX: 5, DeltaY: 40);
        Assert.Equal(Actions(SwipeBegan, Look(5, 0)), tracker.Consume(sample, flat).Actions);
    }

    // Precision

    [Fact]
    public void SlowSwipesAccumulateFractions()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 3), settings);
        var actions = new List<SwipeAction>();
        for (var i = 0; i < 3; i++)
        {
            actions.AddRange(tracker.Consume(new ScrollSample(Changed, DeltaX: 0.4), settings).Actions);
        }
        Assert.Equal(Actions(Look(1, 0)), actions);
    }

    [Fact]
    public void TurnSpeedScalesLook()
    {
        var tracker = new SwipeTracker();
        var fast = settings with { TurnSpeed = 2.5 };
        Assert.Equal(Actions(SwipeBegan, Look(10, 0)), tracker.Consume(new ScrollSample(Began, DeltaX: 4), fast).Actions);
    }

    [Fact]
    public void FractionsResetBetweenSwipes()
    {
        var tracker = new SwipeTracker();
        tracker.Consume(new ScrollSample(Began, DeltaX: 3.6), settings); // leaves 0.6
        tracker.Consume(new ScrollSample(Ended), settings);
        // 3.5 alone is 3 whole pixels; with a carried 0.6 it would be 4.
        Assert.Equal(Actions(SwipeBegan, Look(3, 0)), tracker.Consume(new ScrollSample(Began, DeltaX: 3.5), settings).Actions);
    }
}

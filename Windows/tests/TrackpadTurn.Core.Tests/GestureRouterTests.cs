using TrackpadTurn.Core;

namespace TrackpadTurn.Core.Tests;

public class GestureRouterTests
{
    // WoW in front with the cursor over it. Mouselook hides the cursor, so "hidden" means the
    // addon started turning.
    private static readonly TargetState Mouselooking = new(WowIsForeground: true, CursorOverWow: true, CursorHidden: true);
    private static readonly TargetState OverFrame = new(WowIsForeground: true, CursorOverWow: true, CursorHidden: false);
    private static readonly TargetState OtherApp = new(WowIsForeground: false, CursorOverWow: false, CursorHidden: false);

    private const double Ppm = TouchpadGestureRecognizer.PointsPerMillimetre;

    private static OutputCommand Down(SignalKey key) => new OutputCommand.KeyDown(key);
    private static OutputCommand Up(SignalKey key) => new OutputCommand.KeyUp(key);
    private static OutputCommand Tap(SignalKey key) => new OutputCommand.KeyTap(key);
    private static OutputCommand Look(int dx, int dy) => new OutputCommand.Look(dx, dy);
    private static OutputCommand[] Commands(params OutputCommand[] commands) => commands;

    private static TouchpadFrame Frame(double time, params (int Id, double X, double Y)[] contacts) =>
        new(time, [.. contacts.Select(contact => new TouchpadContact(contact.Id, contact.X, contact.Y))], IsButtonDown: false);

    /// <summary>Two fingers land at x = 10 and 30 mm, then move right by `travelMm`.</summary>
    private static RouterResult StartSwipe(GestureRouter router, TargetState target, double travelMm = 1, double time = 0)
    {
        router.OnTouchpadFrame(Frame(time, (1, 10, 10), (2, 30, 10)), target);
        return router.OnTouchpadFrame(Frame(time + 0.01, (1, 10 + travelMm, 10), (2, 30 + travelMm, 10)), target);
    }

    // Swipe

    [Fact]
    public void SwipeOverTheWorldHoldsTheSwipeKeyAndLooks()
    {
        var router = new GestureRouter(new TurnSettings());
        Assert.Equal(Commands(Down(SignalKey.Swipe), Look((int)Ppm, 0)), StartSwipe(router, Mouselooking).Commands);
        Assert.Equal(Commands(Look((int)(Ppm / 2), 0)), router.OnTouchpadFrame(Frame(0.02, (1, 11.5, 10), (2, 31.5, 10)), Mouselooking).Commands);
        Assert.Equal(Commands(Up(SignalKey.Swipe)), router.OnTouchpadFrame(Frame(0.03), Mouselooking).Commands);
    }

    [Fact]
    public void SwipeWhileWowIsNotInFrontDoesNothing()
    {
        var router = new GestureRouter(new TurnSettings());
        Assert.Empty(StartSwipe(router, OtherApp).Commands);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.015, OtherApp).Disposition);
        Assert.Empty(router.OnTouchpadFrame(Frame(0.03), OtherApp).Commands);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.04, OtherApp).Disposition);
    }

    [Fact]
    public void SwipeStartedElsewhereIsFollowedNowhere()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, OtherApp);
        // WoW comes to the front mid-swipe: this gesture still belongs to the other app.
        Assert.Empty(router.OnTouchpadFrame(Frame(0.02, (1, 15, 10), (2, 35, 10)), Mouselooking).Commands);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.025, Mouselooking).Disposition);
    }

    [Fact]
    public void DisabledLeavesEverythingAlone()
    {
        var router = new GestureRouter(new TurnSettings { IsEnabled = false });
        Assert.Empty(StartSwipe(router, Mouselooking).Commands);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.02, Mouselooking).Disposition);
    }

    // Wheel events: what WoW still sees

    [Fact]
    public void WheelIsHeldBackUntilTheSwipeStarts()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), Mouselooking);
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.005, Mouselooking).Disposition);
    }

    [Fact]
    public void OneFingerOnTheTouchpadLeavesTheMouseWheelAlone()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10)), Mouselooking);
        router.OnTouchpadFrame(Frame(0.01, (1, 30, 10)), Mouselooking);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.015, Mouselooking).Disposition);
    }

    [Fact]
    public void GlideIsDroppedWhenFingersLiftOneByOne()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        router.OnTouchpadFrame(Frame(0.02, (1, 11, 10)), Mouselooking);
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.025, Mouselooking).Disposition);
        router.OnTouchpadFrame(Frame(0.03), Mouselooking);
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.05, Mouselooking).Disposition);
    }

    [Fact]
    public void WheelReachesWowDuringTheSwipe()
    {
        // Frames under the cursor keep scrolling; the addon stops the wheel from zooming.
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.015, Mouselooking).Disposition);
    }

    [Fact]
    public void WheelMadeBeforeTheSwipeKeyIsDropped()
    {
        // The hook can run for a wheel event that was queued before the swipe key was sent; WoW
        // would get it ahead of the key and zoom. Times are when each event was made.
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking); // the key goes down at 0.01
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.008, Mouselooking).Disposition);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.012, Mouselooking).Disposition);
    }

    [Fact]
    public void GlideAfterLiftIsDroppedUntilItStops()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        router.OnTouchpadFrame(Frame(0.03), Mouselooking);
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.05, Mouselooking).Disposition);
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.10, Mouselooking).Disposition);
        // A quiet gap ends the glide; a real wheel after it works as normal.
        var afterGap = 0.10 + GestureRouter.GlideQuietSeconds + 0.01;
        Assert.Equal(HookDisposition.Pass, router.OnWheel(afterGap, Mouselooking).Disposition);
    }

    [Fact]
    public void GlideHasAnUpperBound()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        router.OnTouchpadFrame(Frame(0.03), Mouselooking);
        var time = 0.05;
        while (time < 0.03 + GestureRouter.GlideMaxSeconds)
        {
            Assert.Equal(HookDisposition.Drop, router.OnWheel(time, Mouselooking).Disposition);
            time += 0.05;
        }
        Assert.Equal(HookDisposition.Pass, router.OnWheel(time + 0.01, Mouselooking).Disposition);
    }

    [Fact]
    public void ListScrollsOverAFrameStillSkipTheGlide()
    {
        // The app can't tell a world swipe from a list swipe, so both lose the glide (as on the Mac).
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, OverFrame);
        router.OnTouchpadFrame(Frame(0.03), OverFrame);
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.05, OverFrame).Disposition);
    }

    // Look movement: only while WoW mouselooks

    [Fact]
    public void LookWaitsUntilMouselookHidesTheCursor()
    {
        var router = new GestureRouter(new TurnSettings());
        // WoW hasn't handled the swipe key yet, so the cursor is still visible.
        Assert.Equal(Commands(Down(SignalKey.Swipe)), StartSwipe(router, OverFrame).Commands);
        Assert.Equal(
            Commands(Look((int)(Ppm * 2), 0)),
            router.OnTouchpadFrame(Frame(0.02, (1, 12, 10), (2, 32, 10)), Mouselooking).Commands);
    }

    [Fact]
    public void LookOverAFrameNeverMovesTheCursor()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, OverFrame);
        var late = GestureRouter.MouselookStartSeconds + 0.05;
        Assert.Empty(router.OnTouchpadFrame(Frame(late, (1, 20, 10), (2, 40, 10)), OverFrame).Commands);
        // Turning after the wait would replay the travel held back for a frame.
        Assert.Equal(
            Commands(Look((int)Ppm, 0)),
            router.OnTouchpadFrame(Frame(late + 0.01, (1, 21, 10), (2, 41, 10)), Mouselooking).Commands);
        Assert.Equal(Commands(Up(SignalKey.Swipe)), router.OnTouchpadFrame(Frame(late + 0.02), OverFrame).Commands);
    }

    // Pinch

    [Fact]
    public void PinchTapsZoomKeysAndSwallowsItsWheel()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), Mouselooking);
        // 20 → 22 mm apart is a 0.1 magnification: four steps of 0.025.
        var apart = router.OnTouchpadFrame(Frame(0.01, (1, 9, 10), (2, 31, 10)), Mouselooking);
        Assert.Equal(Enumerable.Repeat(Tap(SignalKey.ZoomIn), 4), apart.Commands);
        // Windows sends a pinch to WoW as Ctrl+wheel; the zoom keys replace it.
        Assert.Equal(HookDisposition.Drop, router.OnWheel(0.015, Mouselooking).Disposition);
        var together = router.OnTouchpadFrame(Frame(0.02, (1, 10, 10), (2, 30, 10)), Mouselooking);
        Assert.All(together.Commands, command => Assert.Equal(Tap(SignalKey.ZoomOut), command));
        Assert.Empty(router.OnTouchpadFrame(Frame(0.03), Mouselooking).Commands);
    }

    [Fact]
    public void PinchOverAnotherAppDoesNothing()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), OtherApp);
        Assert.Empty(router.OnTouchpadFrame(Frame(0.01, (1, 5, 10), (2, 35, 10)), OtherApp).Commands);
        Assert.Equal(HookDisposition.Pass, router.OnWheel(0.015, OtherApp).Disposition);
    }

    // Two-finger press to run

    [Fact]
    public void HeldTouchpadPressRunsUntilReleased()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)) with { IsButtonDown = true }, Mouselooking);
        var press = router.OnRightButton(isDown: true, 0.01, Mouselooking);
        Assert.Equal((HookDisposition.Pass, 0), (press.Disposition, press.Commands.Count));
        Assert.True(router.NeedsTicks);
        Assert.Empty(router.OnTick(0.01 + PressTracker.HoldBeforeRunning / 2, Mouselooking).Commands);
        Assert.Equal(Commands(Down(SignalKey.Forward)), router.OnTick(0.01 + PressTracker.HoldBeforeRunning, Mouselooking).Commands);
        // Stop running before WoW sees the release, as on the Mac: the up is re-sent after the key.
        var release = router.OnRightButton(isDown: false, 0.5, Mouselooking);
        Assert.Equal(HookDisposition.Drop, release.Disposition);
        Assert.Equal(Commands(Up(SignalKey.Forward), new OutputCommand.ReleaseRightButton()), release.Commands);
    }

    [Fact]
    public void QuickTouchpadClickStaysAPlainRightClick()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), Mouselooking);
        router.OnRightButton(isDown: true, 0.01, Mouselooking);
        var release = router.OnRightButton(isDown: false, 0.08, Mouselooking);
        Assert.Equal((HookDisposition.Pass, 0), (release.Disposition, release.Commands.Count));
        Assert.Empty(router.OnTick(0.5, Mouselooking).Commands);
    }

    [Fact]
    public void MouseRightButtonIsLeftAlone()
    {
        // No fingers on the touchpad: the click came from a mouse, which keeps its right-drag.
        var router = new GestureRouter(new TurnSettings());
        router.OnRightButton(isDown: true, 0, Mouselooking);
        Assert.Empty(router.OnTick(1, Mouselooking).Commands);
        Assert.Equal(HookDisposition.Pass, router.OnRightButton(isDown: false, 1.1, Mouselooking).Disposition);
    }

    [Fact]
    public void PressOverAnotherAppDoesNothing()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), OtherApp);
        router.OnRightButton(isDown: true, 0.01, OtherApp);
        Assert.Empty(router.OnTick(1, OtherApp).Commands);
    }

    [Fact]
    public void HoldToRunOffNeverRuns()
    {
        var router = new GestureRouter(new TurnSettings { PressToRunEnabled = false });
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), Mouselooking);
        router.OnRightButton(isDown: true, 0.01, Mouselooking);
        Assert.Empty(router.OnTick(1, Mouselooking).Commands);
    }

    // Lost events and focus changes

    [Fact]
    public void LosingWowMidSwipeReleasesTheKeyOnce()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        Assert.Equal(Commands(Up(SignalKey.Swipe)), router.Abandon().Commands);
        Assert.Empty(router.OnTouchpadFrame(Frame(0.02, (1, 15, 10), (2, 35, 10)), Mouselooking).Commands);
        Assert.Empty(router.OnTouchpadFrame(Frame(0.03), Mouselooking).Commands);
    }

    [Fact]
    public void LosingWowWhileRunningStopsRunning()
    {
        var router = new GestureRouter(new TurnSettings());
        router.OnTouchpadFrame(Frame(0, (1, 10, 10), (2, 30, 10)), Mouselooking);
        router.OnRightButton(isDown: true, 0.01, Mouselooking);
        router.OnTick(1, Mouselooking);
        Assert.Equal(Commands(Up(SignalKey.Forward)), router.Abandon().Commands);
        Assert.Equal(HookDisposition.Pass, router.OnRightButton(isDown: false, 1.1, Mouselooking).Disposition);
    }

    [Fact]
    public void MissingLiftReportEndsTheSwipeAfterATimeout()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        Assert.True(router.NeedsTicks);
        Assert.Empty(router.OnTick(0.01 + GestureRouter.LostContactSeconds / 2, Mouselooking).Commands);
        Assert.Equal(Commands(Up(SignalKey.Swipe)), router.OnTick(0.02 + GestureRouter.LostContactSeconds, Mouselooking).Commands);
        Assert.False(router.NeedsTicks);
    }

    [Fact]
    public void SwitchingOffReleasesHeldKeys()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        Assert.Equal(Commands(Up(SignalKey.Swipe)), router.UpdateSettings(new TurnSettings { IsEnabled = false }).Commands);
    }

    [Fact]
    public void TuningChangesKeepTheSwipeGoing()
    {
        var router = new GestureRouter(new TurnSettings());
        StartSwipe(router, Mouselooking);
        Assert.Empty(router.UpdateSettings(new TurnSettings { TurnSpeed = 2 }).Commands);
        Assert.Equal(
            Commands(Look((int)(Ppm * 2), 0)),
            router.OnTouchpadFrame(Frame(0.02, (1, 12, 10), (2, 32, 10)), Mouselooking).Commands);
    }
}

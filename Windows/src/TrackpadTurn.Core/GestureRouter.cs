namespace TrackpadTurn.Core;

/// <summary>Where input would go right now, as the app observes it.</summary>
/// <param name="WowIsForeground">WoW's window is the foreground window, so injected keys reach it.</param>
/// <param name="CursorOverWow">The cursor is over WoW, not over another window in front of it.</param>
/// <param name="CursorHidden">The system cursor is hidden, which WoW does while it mouselooks.</param>
public readonly record struct TargetState(bool WowIsForeground, bool CursorOverWow, bool CursorHidden)
{
    public bool IsWow => WowIsForeground && CursorOverWow;
}

/// <summary>Input the app must inject, in order.</summary>
public abstract record OutputCommand
{
    private OutputCommand() { }

    public sealed record KeyDown(SignalKey Key) : OutputCommand;
    public sealed record KeyUp(SignalKey Key) : OutputCommand;
    /// <summary>Down then up.</summary>
    public sealed record KeyTap(SignalKey Key) : OutputCommand;
    /// <summary>Relative mouse movement in pixels.</summary>
    public sealed record Look(int Dx, int Dy) : OutputCommand;
    /// <summary>Re-send the right-button release that was held back.</summary>
    public sealed record ReleaseRightButton : OutputCommand;
}

/// <summary>What the low-level mouse hook does with the event it is holding.</summary>
public enum HookDisposition
{
    Pass,
    Drop,
}

public sealed record RouterResult(IReadOnlyList<OutputCommand> Commands, HookDisposition Disposition = HookDisposition.Pass)
{
    public static readonly RouterResult None = new([]);

    public override string ToString() => $"[{string.Join(", ", Commands)}] {Disposition}";
}

/// <summary>
/// Decides what WoW receives for each touchpad frame, wheel message and right-button event.
/// The Windows counterpart of the Mac app's GestureBridge, kept free of Win32 so it is testable.
/// </summary>
/// <remarks>
/// BREADCRUMB: the app feeds this from three places on its input thread
/// (Windows/src/TrackpadTurn.Windows/Input/InputThread.cs): Raw Input touchpad reports, the
/// WH_MOUSE_LL hook (wheel and right button) and a timer. It runs every returned command
/// through InputInjector, and the hook drops events this returns Drop for.
/// </remarks>
public sealed class GestureRouter(TurnSettings settings)
{
    /// <summary>A gap between wheel messages this long ends the glide after a lift.</summary>
    public const double GlideQuietSeconds = 0.15;
    /// <summary>The glide is never held back longer than this, so a mouse wheel can't stay blocked.</summary>
    public const double GlideMaxSeconds = 2.0;
    /// <summary>
    /// WoW needs a frame or two to start mouselook after the swipe key. Look movement waits this
    /// long for the cursor to hide; after that the swipe is over a frame and the cursor must stay put.
    /// </summary>
    public const double MouselookStartSeconds = 0.3;
    /// <summary>
    /// Fingers stay down this long without a report only if the lift report was lost. Generous,
    /// because some touchpads stop reporting fingers that rest without moving.
    /// </summary>
    public const double LostContactSeconds = 1.5;

    private enum Owner
    {
        /// <summary>No two-finger gesture, or one that hasn't been classified yet.</summary>
        None,
        /// <summary>The gesture started over WoW; follow it to its end.</summary>
        Wow,
        /// <summary>The gesture started elsewhere, or was abandoned; leave it alone.</summary>
        Elsewhere,
    }

    private readonly TouchpadGestureRecognizer recognizer = new();
    private readonly SwipeTracker swipeTracker = new();
    private readonly PinchTracker pinchTracker = new();
    private readonly PressTracker pressTracker = new();

    private Owner gestureOwner = Owner.None;
    private bool isPinching;
    private double lastFrameTime;

    // Look movement held back until mouselook hides the cursor.
    private double swipeBeganAt;
    private int heldLookX;
    private int heldLookY;

    // The glide after a lift: wheel messages to drop.
    private double? glideStartedAt;
    private double lastGlideWheelAt;

    private double? pressDeadline;

    public TurnSettings Settings { get; private set; } = settings;

    /// <summary>Call <see cref="OnTick"/> regularly: a press is waiting, or fingers are down.</summary>
    public bool NeedsTicks => pressDeadline is not null || pressTracker.IsRunning || recognizer.HasContacts;

    /// <summary>Two-finger swipes that turned or scrolled, for the status line.</summary>
    public int SwipeCount { get; private set; }

    /// <summary>At least one finger touches the touchpad.</summary>
    public bool HasContacts => recognizer.HasContacts;

    /// <summary>A signal key is held down in WoW (swiping or running).</summary>
    public bool IsHoldingKeys => swipeTracker.IsSwiping || pressTracker.IsRunning;

    // Touchpad frames

    public RouterResult OnTouchpadFrame(TouchpadFrame frame, TargetState target)
    {
        lastFrameTime = frame.Time;
        return Route(recognizer.Consume(frame), frame.Time, target);
    }

    private RouterResult Route(IReadOnlyList<RecognizedGesture> gestures, double time, TargetState target)
    {
        var commands = new List<OutputCommand>();
        foreach (var gesture in gestures)
        {
            switch (gesture)
            {
                case RecognizedGesture.Scroll(var sample):
                    RouteScroll(sample, time, target, commands);
                    break;
                case RecognizedGesture.Pinch(var sample):
                    RoutePinch(sample, time, target, commands);
                    break;
            }
        }
        return new RouterResult(commands);
    }

    private void RouteScroll(ScrollSample sample, double time, TargetState target, List<OutputCommand> commands)
    {
        if (sample.Phase == GesturePhase.Began)
        {
            gestureOwner = Settings.IsEnabled && target.IsWow ? Owner.Wow : Owner.Elsewhere;
            isPinching = false;
        }
        if (gestureOwner != Owner.Wow)
        {
            EndGestureIfOver(sample.Phase);
            return;
        }

        foreach (var action in swipeTracker.Consume(sample, Settings).Actions)
        {
            switch (action)
            {
                case SwipeAction.Began:
                    commands.Add(new OutputCommand.KeyDown(SignalKey.Swipe));
                    swipeBeganAt = time;
                    heldLookX = 0;
                    heldLookY = 0;
                    SwipeCount++;
                    break;
                case SwipeAction.Look(var dx, var dy):
                    Look(dx, dy, time, target, commands);
                    break;
                case SwipeAction.Ended:
                    commands.Add(new OutputCommand.KeyUp(SignalKey.Swipe));
                    break;
            }
        }
        if (IsEnd(sample.Phase))
        {
            StartGlide(time);
        }
        EndGestureIfOver(sample.Phase);
    }

    private void RoutePinch(PinchSample sample, double time, TargetState target, List<OutputCommand> commands)
    {
        if (sample.Phase == GesturePhase.Began)
        {
            gestureOwner = Settings.IsEnabled && target.IsWow ? Owner.Wow : Owner.Elsewhere;
            isPinching = true;
        }
        if (gestureOwner != Owner.Wow)
        {
            EndGestureIfOver(sample.Phase);
            return;
        }

        var steps = pinchTracker.Consume(sample, Settings);
        var key = steps > 0 ? SignalKey.ZoomIn : SignalKey.ZoomOut;
        for (var step = 0; step < Math.Abs(steps); step++)
        {
            commands.Add(new OutputCommand.KeyTap(key));
        }
        if (IsEnd(sample.Phase))
        {
            // Windows may keep zooming (Ctrl+wheel) after the lift, like the scroll glide.
            StartGlide(time);
        }
        EndGestureIfOver(sample.Phase);
    }

    /// <summary>Mouse movement turns WoW only while it mouselooks; otherwise it would drag the cursor.</summary>
    private void Look(int dx, int dy, double time, TargetState target, List<OutputCommand> commands)
    {
        if (target.CursorHidden)
        {
            commands.Add(new OutputCommand.Look(heldLookX + dx, heldLookY + dy));
            heldLookX = 0;
            heldLookY = 0;
        }
        else if (time - swipeBeganAt < MouselookStartSeconds)
        {
            heldLookX += dx;
            heldLookY += dy;
        }
        else
        {
            heldLookX = 0;
            heldLookY = 0;
        }
    }

    private static bool IsEnd(GesturePhase phase) => phase is GesturePhase.Ended or GesturePhase.Cancelled;

    private void EndGestureIfOver(GesturePhase phase)
    {
        if (IsEnd(phase))
        {
            gestureOwner = Owner.None;
            isPinching = false;
        }
    }

    // Wheel messages (WH_MOUSE_LL)

    /// <summary>
    /// A wheel message is about to reach the app under the cursor. The hook can't tell a touchpad
    /// from a mouse wheel, so this decides from what the touchpad's fingers are doing.
    /// </summary>
    /// <param name="time">When the wheel event was made (not when the hook sees it), on this router's clock.</param>
    public RouterResult OnWheel(double time, TargetState target)
    {
        if (!Settings.IsEnabled)
        {
            return RouterResult.None;
        }

        if (recognizer.IsUndecided)
        {
            // Two fingers down but not classified yet: hold it back, as the Mac drops the first few points.
            return target.IsWow ? new RouterResult([], HookDisposition.Drop) : RouterResult.None;
        }
        if (gestureOwner == Owner.Wow)
        {
            // Frames under the cursor keep scrolling while swiping; a pinch's Ctrl+wheel never reaches WoW.
            // A wheel event made before the swipe key went down would reach WoW ahead of the key
            // and zoom (the Mac re-posts that scroll behind the key instead).
            var madeBeforeSwipeKey = time <= swipeBeganAt;
            var drop = isPinching || !swipeTracker.IsSwiping || madeBeforeSwipeKey;
            return drop ? new RouterResult([], HookDisposition.Drop) : RouterResult.None;
        }

        if (glideStartedAt is { } startedAt)
        {
            var isQuietGap = time - lastGlideWheelAt > GlideQuietSeconds;
            if (!isQuietGap && time - startedAt <= GlideMaxSeconds)
            {
                lastGlideWheelAt = time;
                return new RouterResult([], HookDisposition.Drop);
            }
            glideStartedAt = null;
        }
        return RouterResult.None;
    }

    private void StartGlide(double time)
    {
        glideStartedAt = time;
        lastGlideWheelAt = time;
    }

    // Right button (WH_MOUSE_LL)

    public RouterResult OnRightButton(bool isDown, double time, TargetState target)
    {
        if (isDown)
        {
            // Only a click made with fingers on the touchpad counts; a mouse keeps its right-drag.
            if (target.IsWow && recognizer.HasContacts)
            {
                pressDeadline = pressTracker.PressBegan(time, Settings);
            }
            return RouterResult.None;
        }

        if (!pressTracker.IsTracking)
        {
            return RouterResult.None;
        }
        pressDeadline = null;
        if (!pressTracker.PressEnded().Contains(PressAction.RunEnded))
        {
            return RouterResult.None;
        }
        // Stop running before WoW sees the button release, then hand it the release.
        return new RouterResult(
            [new OutputCommand.KeyUp(SignalKey.Forward), new OutputCommand.ReleaseRightButton()],
            HookDisposition.Drop);
    }

    // Timer

    public RouterResult OnTick(double time, TargetState target)
    {
        var commands = new List<OutputCommand>();
        if (pressDeadline is { } deadline && time >= deadline)
        {
            pressDeadline = null;
            if (pressTracker.CheckHold(deadline).Contains(PressAction.RunBegan))
            {
                commands.Add(new OutputCommand.KeyDown(SignalKey.Forward));
            }
        }
        if (recognizer.HasContacts && time - lastFrameTime > LostContactSeconds)
        {
            commands.AddRange(Route(recognizer.Reset(), time, target).Commands);
        }
        return new RouterResult(commands);
    }

    // Ending gestures early

    /// <summary>
    /// Ends gestures whose end will never be seen (WoW lost focus, events were missed, quitting),
    /// so WoW is never left mouselooking or running.
    /// </summary>
    public RouterResult Abandon()
    {
        var commands = new List<OutputCommand>();
        if (swipeTracker.Finish().Count > 0)
        {
            commands.Add(new OutputCommand.KeyUp(SignalKey.Swipe));
        }
        pinchTracker.Consume(new PinchSample(GesturePhase.Cancelled, 0), Settings);
        pressDeadline = null;
        if (pressTracker.PressEnded().Contains(PressAction.RunEnded))
        {
            commands.Add(new OutputCommand.KeyUp(SignalKey.Forward));
        }
        if (gestureOwner == Owner.Wow)
        {
            gestureOwner = Owner.Elsewhere;
        }
        glideStartedAt = null;
        return new RouterResult(commands);
    }

    /// <summary>New settings apply at once; switching the app off ends what is held.</summary>
    public RouterResult UpdateSettings(TurnSettings newSettings)
    {
        Settings = newSettings;
        return newSettings.IsEnabled ? RouterResult.None : Abandon();
    }
}

namespace TrackpadTurn.Core;

/// <summary>What the app must send to WoW in response to a scroll sample.</summary>
public abstract record SwipeAction
{
    private SwipeAction() { }

    /// <summary>Press the swipe signal key: the addon starts mouselook if the cursor is over the world.</summary>
    public static readonly SwipeAction SwipeBegan = new Began();
    /// <summary>Release the swipe signal key: the addon stops mouselook.</summary>
    public static readonly SwipeAction SwipeEnded = new Ended();

    public sealed record Began : SwipeAction;
    public sealed record Ended : SwipeAction;
    /// <summary>Post a mouse movement of this many pixels; WoW turns with it while mouselooking.</summary>
    public sealed record Look(int Dx, int Dy) : SwipeAction;
}

/// <summary>What happens to the original scroll event.</summary>
public enum ScrollDisposition
{
    /// <summary>Let WoW have it now (frames under the cursor still scroll).</summary>
    Forward,
    /// <summary>Hold it back and re-post it after the actions, so WoW sees the signal key first.</summary>
    ForwardAfterActions,
    /// <summary>Do not let WoW see it.</summary>
    Drop,
}

public sealed record SwipeResponse(IReadOnlyList<SwipeAction> Actions, ScrollDisposition Disposition)
{
    public SwipeResponse() : this([], ScrollDisposition.Forward) { }

    public SwipeResponse(IReadOnlyList<SwipeAction> actions) : this(actions, ScrollDisposition.Forward) { }

    // Value equality over the action list, like the Swift struct's synthesized `==`.
    public bool Equals(SwipeResponse? other) =>
        other is not null && Disposition == other.Disposition && Actions.SequenceEqual(other.Actions);

    public override int GetHashCode() => HashCode.Combine(Disposition, Actions.Count);

    public override string ToString() => $"[{string.Join(", ", Actions)}] {Disposition}";
}

/// <summary>
/// Turns the stream of touchpad scroll samples into one swipe with look deltas.
/// Ported one-to-one from Sources/TrackpadTurnCore/SwipeTracker.swift.
/// </summary>
/// <remarks>
/// Feel follows Roblox's trackpad camera: both axes rotate (pitch damped), nothing happens until
/// the fingers really move, no glide after they lift, and wheel mice are left alone.
/// </remarks>
public sealed class SwipeTracker
{
    /// <summary>
    /// Finger travel (points) before a scroll counts as a swipe. Starting mouselook snaps the
    /// character to face the camera, so a resting or twitching pair of fingers must not do it.
    /// </summary>
    public const double StartTravel = 3.0;

    private enum State
    {
        Idle,
        /// <summary>Fingers are scrolling but have not travelled <see cref="StartTravel"/> yet.</summary>
        Pending,
        Swiping,
    }

    private State state = State.Idle;
    private double pendingTravelX;
    private double pendingTravelY;
    // Mouse deltas are whole pixels; keep the fractions so slow swipes still turn.
    private double remainderX;
    private double remainderY;

    /// <summary>The signal key is held down.</summary>
    public bool IsSwiping => state == State.Swiping;

    /// <summary>
    /// A scroll gesture is in progress (pending or swiping), so its end must be seen even if
    /// the events stop targeting WoW.
    /// </summary>
    public bool IsTracking => state != State.Idle;

    public SwipeResponse Consume(ScrollSample sample, TurnSettings settings)
    {
        if (!settings.IsEnabled || !sample.IsContinuous)
        {
            return new SwipeResponse();
        }

        // Glide after the fingers lift: drop it. A camera that keeps spinning overshoots, and the
        // addon only blocks wheel zoom while fingers are down.
        if (sample.MomentumPhase != MomentumPhase.None)
        {
            return new SwipeResponse([], ScrollDisposition.Drop);
        }

        switch (sample.Phase)
        {
            case GesturePhase.None:
            case GesturePhase.MayBegin:
                return new SwipeResponse();
            case GesturePhase.Began:
                // A new gesture while one is open means its end event never reached us.
                var closing = Finish();
                var response = Advance(sample, settings);
                return response with { Actions = [.. closing, .. response.Actions] };
            case GesturePhase.Changed:
                return Advance(sample, settings);
            case GesturePhase.Ended:
            case GesturePhase.Cancelled:
            default:
                return new SwipeResponse(Finish());
        }
    }

    /// <summary>Ends the gesture without its natural end event (WoW lost focus, events were lost).</summary>
    public IReadOnlyList<SwipeAction> Finish()
    {
        var wasSwiping = IsSwiping;
        state = State.Idle;
        return wasSwiping ? [SwipeAction.SwipeEnded] : [];
    }

    private SwipeResponse Advance(ScrollSample sample, TurnSettings settings)
    {
        var travel = sample.FingerTravel;
        if (state == State.Swiping)
        {
            return new SwipeResponse(LookActions(travel.X, travel.Y, settings));
        }

        var pendingX = travel.X;
        var pendingY = travel.Y;
        if (state == State.Pending)
        {
            pendingX += pendingTravelX;
            pendingY += pendingTravelY;
        }
        if (Math.Sqrt(pendingX * pendingX + pendingY * pendingY) < StartTravel)
        {
            // Held back: a few points of scroll are invisible, but a stray zoom tick is not.
            state = State.Pending;
            pendingTravelX = pendingX;
            pendingTravelY = pendingY;
            return new SwipeResponse([], ScrollDisposition.Drop);
        }

        state = State.Swiping;
        remainderX = 0;
        remainderY = 0;
        return new SwipeResponse(
            [SwipeAction.SwipeBegan, .. LookActions(pendingX, pendingY, settings)],
            ScrollDisposition.ForwardAfterActions);
    }

    private SwipeAction[] LookActions(double travelX, double travelY, TurnSettings settings)
    {
        // "Fingers right = look right" and "fingers up = look up", the same as moving a mouse.
        var horizontalSign = settings.ReverseHorizontal ? -1.0 : 1.0;
        var verticalSign = settings.ReverseVertical ? -1.0 : 1.0;
        var lookX = travelX * settings.TurnSpeed * horizontalSign;
        var lookY = settings.TiltEnabled
            ? travelY * settings.TurnSpeed * TurnSettings.TiltDamping * verticalSign
            : 0;

        var totalX = remainderX + lookX;
        var totalY = remainderY + lookY;
        var wholeX = Math.Truncate(totalX);
        var wholeY = Math.Truncate(totalY);
        remainderX = totalX - wholeX;
        remainderY = totalY - wholeY;

        if (wholeX == 0 && wholeY == 0)
        {
            return [];
        }
        return [new SwipeAction.Look((int)wholeX, (int)wholeY)];
    }
}

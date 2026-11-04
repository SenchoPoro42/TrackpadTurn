namespace TrackpadTurn.Core;

/// <summary>A two-finger gesture recognised from raw contacts, in the trackers' terms.</summary>
public abstract record RecognizedGesture
{
    private RecognizedGesture() { }

    public sealed record Scroll(ScrollSample Sample) : RecognizedGesture;
    public sealed record Pinch(PinchSample Sample) : RecognizedGesture;
}

/// <summary>
/// Turns touchpad contact frames into the scroll and pinch samples macOS hands the Mac helper.
/// </summary>
/// <remarks>
/// Windows gives legacy apps only wheel messages, with no begin or end and with glide added
/// after the fingers lift. The raw contacts say exactly when two fingers land, move and lift.
/// A pair of fingers becomes either a scroll (they move together) or a pinch (their distance
/// changes), decided once per gesture, as on the Mac.
/// </remarks>
public sealed class TouchpadGestureRecognizer
{
    /// <summary>
    /// Scroll points per millimetre of finger travel. A Mac trackpad scrolls roughly this much,
    /// so turn speed 1 feels alike on both. Tune with the app's turn speed.
    /// </summary>
    public const double PointsPerMillimetre = 8.0;

    /// <summary>
    /// Movement (centroid travel + change in finger distance) before the gesture is classified.
    /// Below this the fingers are resting or twitching.
    /// </summary>
    public const double ClassifyMillimetres = 0.75;

    private enum State
    {
        Idle,
        /// <summary>Two fingers down, not yet moved enough to say what they do.</summary>
        Undecided,
        Scrolling,
        Pinching,
        /// <summary>More than two fingers were down; wait until all but one lift.</summary>
        Blocked,
    }

    private State state = State.Idle;
    private (int First, int Second) pairIds;
    private (double X, double Y) anchorCentroid;
    private double anchorDistance;
    private (double X, double Y) lastCentroid;
    private double lastDistance;

    /// <summary>At least one finger touches the touchpad.</summary>
    public bool HasContacts { get; private set; }

    /// <summary>Two fingers are down but haven't moved enough to be a scroll or a pinch.</summary>
    public bool IsUndecided => state == State.Undecided;

    public IReadOnlyList<RecognizedGesture> Consume(TouchpadFrame frame)
    {
        HasContacts = frame.Contacts.Count > 0;
        var gestures = new List<RecognizedGesture>();

        if (frame.Contacts.Count > 2)
        {
            AddEnd(gestures, cancelled: true);
            state = State.Blocked;
            return gestures;
        }
        if (state == State.Blocked)
        {
            if (frame.Contacts.Count <= 1)
            {
                state = State.Idle;
            }
            return gestures;
        }
        if (frame.Contacts.Count < 2)
        {
            AddEnd(gestures, cancelled: false);
            state = State.Idle;
            return gestures;
        }

        var (first, second) = Pair(frame);
        if (state == State.Idle || (first.Id, second.Id) != pairIds)
        {
            AddEnd(gestures, cancelled: false);
            Anchor(first, second);
            return gestures;
        }

        var centroid = Centroid(first, second);
        var distance = Distance(first, second);
        switch (state)
        {
            case State.Undecided:
                var travel = Length(centroid.X - anchorCentroid.X, centroid.Y - anchorCentroid.Y);
                var spread = Math.Abs(distance - anchorDistance);
                if (travel + spread < ClassifyMillimetres)
                {
                    break;
                }
                if (spread > travel && anchorDistance > 0)
                {
                    state = State.Pinching;
                    gestures.Add(new RecognizedGesture.Pinch(new PinchSample(GesturePhase.Began, distance / anchorDistance - 1)));
                }
                else
                {
                    state = State.Scrolling;
                    gestures.Add(ScrollBy(GesturePhase.Began, centroid.X - anchorCentroid.X, centroid.Y - anchorCentroid.Y));
                }
                lastCentroid = centroid;
                lastDistance = distance;
                break;

            case State.Scrolling:
                if (centroid != lastCentroid)
                {
                    gestures.Add(ScrollBy(GesturePhase.Changed, centroid.X - lastCentroid.X, centroid.Y - lastCentroid.Y));
                    lastCentroid = centroid;
                }
                break;

            case State.Pinching:
                if (distance != lastDistance && lastDistance > 0)
                {
                    gestures.Add(new RecognizedGesture.Pinch(new PinchSample(GesturePhase.Changed, distance / lastDistance - 1)));
                    lastDistance = distance;
                }
                break;
        }
        return gestures;
    }

    /// <summary>Ends the open gesture without a lift report (reports stopped arriving).</summary>
    public IReadOnlyList<RecognizedGesture> Reset()
    {
        var gestures = new List<RecognizedGesture>();
        AddEnd(gestures, cancelled: false);
        state = State.Idle;
        HasContacts = false;
        return gestures;
    }

    private void Anchor(TouchpadContact first, TouchpadContact second)
    {
        state = State.Undecided;
        pairIds = (first.Id, second.Id);
        anchorCentroid = Centroid(first, second);
        anchorDistance = Distance(first, second);
    }

    private void AddEnd(List<RecognizedGesture> gestures, bool cancelled)
    {
        var phase = cancelled ? GesturePhase.Cancelled : GesturePhase.Ended;
        switch (state)
        {
            case State.Scrolling:
                gestures.Add(new RecognizedGesture.Scroll(new ScrollSample(phase)));
                break;
            case State.Pinching:
                gestures.Add(new RecognizedGesture.Pinch(new PinchSample(phase, 0)));
                break;
        }
        state = State.Idle;
    }

    private static RecognizedGesture.Scroll ScrollBy(GesturePhase phase, double dxMillimetres, double dyMillimetres) =>
        // Contacts are finger positions, so the deltas are finger travel ("natural" direction).
        new RecognizedGesture.Scroll(new ScrollSample(
            phase,
            DeltaX: dxMillimetres * PointsPerMillimetre,
            DeltaY: dyMillimetres * PointsPerMillimetre,
            IsDirectionInvertedFromDevice: true));

    // Order the pair by contact id so the same two fingers always compare equal.
    private static (TouchpadContact First, TouchpadContact Second) Pair(TouchpadFrame frame) =>
        frame.Contacts[0].Id <= frame.Contacts[1].Id
            ? (frame.Contacts[0], frame.Contacts[1])
            : (frame.Contacts[1], frame.Contacts[0]);

    private static (double X, double Y) Centroid(TouchpadContact first, TouchpadContact second) =>
        ((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static double Distance(TouchpadContact first, TouchpadContact second) =>
        Length(second.X - first.X, second.Y - first.Y);

    private static double Length(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
}

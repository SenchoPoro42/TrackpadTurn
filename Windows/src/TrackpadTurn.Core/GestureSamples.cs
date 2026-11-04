namespace TrackpadTurn.Core;

// Platform-neutral snapshots of the gesture events the trackers consume.
// Mirrors Sources/TrackpadTurnCore/GestureSamples.swift. On Windows they are built from the
// touchpad's raw contacts by TouchpadGestureRecognizer instead of from macOS scroll events.

public enum GesturePhase
{
    None,
    /// <summary>Fingers rest on the touchpad but have not moved yet.</summary>
    MayBegin,
    Began,
    Changed,
    Ended,
    Cancelled,
}

public enum MomentumPhase
{
    None,
    Began,
    Changed,
    Ended,
}

/// <param name="IsContinuous">Pixel-precise scrolling (touchpad). False for notched wheel mice.</param>
/// <param name="DeltaX">Scroll delta in points; equals finger travel when <paramref name="IsDirectionInvertedFromDevice"/> is true.</param>
/// <param name="IsDirectionInvertedFromDevice">True when the deltas already follow the fingers ("natural" scrolling).</param>
public readonly record struct ScrollSample(
    GesturePhase Phase,
    MomentumPhase MomentumPhase = MomentumPhase.None,
    bool IsContinuous = true,
    double DeltaX = 0,
    double DeltaY = 0,
    bool IsDirectionInvertedFromDevice = true)
{
    /// <summary>
    /// Finger travel in screen coordinates (x right, y down), whatever the scroll-direction
    /// preference is.
    /// </summary>
    public (double X, double Y) FingerTravel =>
        IsDirectionInvertedFromDevice ? (DeltaX, DeltaY) : (-DeltaX, -DeltaY);
}

/// <param name="Magnification">The change since the previous sample; positive = fingers apart.</param>
public readonly record struct PinchSample(GesturePhase Phase, double Magnification);

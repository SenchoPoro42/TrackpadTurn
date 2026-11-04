namespace TrackpadTurn.Core;

/// <summary>
/// Turns pinch magnification into whole camera-zoom steps.
/// Ported one-to-one from Sources/TrackpadTurnCore/PinchTracker.swift.
/// </summary>
public sealed class PinchTracker
{
    /// <summary>Magnification needed for one zoom step at zoom speed 1. A full pinch is roughly 0.6.</summary>
    public const double MagnificationPerStep = 0.025;

    private double accumulated;

    /// <summary>Returns the zoom steps to send: positive = zoom in (fingers apart), negative = zoom out.</summary>
    public int Consume(PinchSample sample, TurnSettings settings)
    {
        if (!settings.IsEnabled || !settings.PinchZoomEnabled)
        {
            return 0;
        }

        switch (sample.Phase)
        {
            case GesturePhase.Began:
                accumulated = 0;
                break;
            case GesturePhase.Ended:
            case GesturePhase.Cancelled:
                accumulated = 0;
                return 0;
        }

        accumulated += sample.Magnification * settings.ZoomSpeed;
        var steps = Math.Truncate(accumulated / MagnificationPerStep);
        accumulated -= steps * MagnificationPerStep;
        return (int)steps;
    }
}

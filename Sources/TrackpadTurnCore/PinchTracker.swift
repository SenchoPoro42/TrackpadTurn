/// Turns pinch magnification into whole camera-zoom steps.
public struct PinchTracker: Sendable {
    /// Magnification needed for one zoom step at zoom speed 1. A full pinch is roughly 0.6.
    public static let magnificationPerStep = 0.025

    private var accumulated = 0.0

    public init() {}

    /// Returns the zoom steps to send: positive = zoom in (fingers apart), negative = zoom out.
    public mutating func consume(_ sample: PinchSample, settings: TurnSettings) -> Int {
        guard settings.isEnabled, settings.pinchZoomEnabled else { return 0 }

        switch sample.phase {
        case .began:
            accumulated = 0
        case .ended, .cancelled:
            accumulated = 0
            return 0
        case .none, .mayBegin, .changed:
            break
        }

        accumulated += sample.magnification * settings.zoomSpeed
        let steps = (accumulated / Self.magnificationPerStep).rounded(.towardZero)
        accumulated -= steps * Self.magnificationPerStep
        return Int(steps)
    }
}

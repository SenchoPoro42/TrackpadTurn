/// Platform-neutral snapshots of the macOS events the trackers consume.
/// The app converts `CGEvent`/`NSEvent` into these so the logic stays testable.

public enum GesturePhase: Sendable, Equatable {
    case none
    /// Fingers rest on the trackpad but have not moved yet.
    case mayBegin
    case began
    case changed
    case ended
    case cancelled
}

public enum MomentumPhase: Sendable, Equatable {
    case none
    case began
    case changed
    case ended
}

public struct ScrollSample: Sendable, Equatable {
    public var phase: GesturePhase
    public var momentumPhase: MomentumPhase
    /// Pixel-precise scrolling (trackpad, Magic Mouse). False for notched wheel mice.
    public var isContinuous: Bool
    /// `NSEvent.scrollingDeltaX/Y`, already flipped by macOS when natural scrolling is on.
    public var deltaX: Double
    public var deltaY: Double
    /// `NSEvent.isDirectionInvertedFromDevice` — true when natural scrolling is on.
    public var isDirectionInvertedFromDevice: Bool

    public init(
        phase: GesturePhase,
        momentumPhase: MomentumPhase = .none,
        isContinuous: Bool = true,
        deltaX: Double = 0,
        deltaY: Double = 0,
        isDirectionInvertedFromDevice: Bool = true
    ) {
        self.phase = phase
        self.momentumPhase = momentumPhase
        self.isContinuous = isContinuous
        self.deltaX = deltaX
        self.deltaY = deltaY
        self.isDirectionInvertedFromDevice = isDirectionInvertedFromDevice
    }

    /// Finger travel in screen coordinates (x right, y down), whatever the scroll-direction
    /// preference is. With natural scrolling the content follows the fingers, so the deltas
    /// already equal finger travel; otherwise macOS has flipped them.
    public var fingerTravel: (x: Double, y: Double) {
        isDirectionInvertedFromDevice ? (deltaX, deltaY) : (-deltaX, -deltaY)
    }
}

public struct PinchSample: Sendable, Equatable {
    public var phase: GesturePhase
    /// `NSEvent.magnification` — the change since the previous event; positive = fingers apart.
    public var magnification: Double

    public init(phase: GesturePhase, magnification: Double) {
        self.phase = phase
        self.magnification = magnification
    }
}

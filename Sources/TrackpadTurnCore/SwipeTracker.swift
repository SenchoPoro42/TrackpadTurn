/// What the app must send to WoW in response to a scroll sample.
public enum SwipeAction: Equatable, Sendable {
    /// Press the swipe signal key: the addon starts mouselook if the cursor is over the world.
    case swipeBegan
    /// Post a mouse movement of this many pixels; WoW turns with it while mouselooking.
    case look(dx: Int, dy: Int)
    /// Release the swipe signal key: the addon stops mouselook.
    case swipeEnded
}

/// What happens to the original scroll event.
public enum ScrollDisposition: Equatable, Sendable {
    /// Let WoW have it now (frames under the cursor still scroll).
    case forward
    /// Hold it back and re-post it after the actions, so WoW sees the signal key first.
    case forwardAfterActions
    /// Do not let WoW see it.
    case drop
}

public struct SwipeResponse: Equatable, Sendable {
    public var actions: [SwipeAction]
    public var disposition: ScrollDisposition

    public init(_ actions: [SwipeAction] = [], _ disposition: ScrollDisposition = .forward) {
        self.actions = actions
        self.disposition = disposition
    }
}

/// Turns the stream of trackpad scroll events into one swipe with look deltas.
///
/// Feel follows Roblox's trackpad camera: both axes rotate (pitch damped), nothing happens until
/// the fingers really move, no glide after they lift, and wheel mice are left alone.
public struct SwipeTracker: Sendable {
    /// Finger travel (points) before a scroll counts as a swipe. Starting mouselook snaps the
    /// character to face the camera, so a resting or twitching pair of fingers must not do it.
    public static let startTravel = 3.0

    private enum State: Sendable {
        case idle
        /// Fingers are scrolling but have not travelled `startTravel` yet.
        case pending(travelX: Double, travelY: Double)
        case swiping
    }

    private var state = State.idle
    // Mouse deltas are whole pixels; keep the fractions so slow swipes still turn.
    private var remainderX = 0.0
    private var remainderY = 0.0

    public init() {}

    /// The signal key is held down.
    public var isSwiping: Bool {
        if case .swiping = state { return true }
        return false
    }

    /// A scroll gesture is in progress (pending or swiping), so its end must be seen even if
    /// the events stop targeting WoW.
    public var isTracking: Bool {
        if case .idle = state { return false }
        return true
    }

    public mutating func consume(_ sample: ScrollSample, settings: TurnSettings) -> SwipeResponse {
        guard settings.isEnabled, sample.isContinuous else { return SwipeResponse() }

        // macOS keeps sending scroll events after the fingers lift (inertia). Drop them: a camera
        // that keeps spinning overshoots, and the addon only blocks wheel zoom while fingers are down.
        guard sample.momentumPhase == .none else { return SwipeResponse([], .drop) }

        switch sample.phase {
        case .none, .mayBegin:
            return SwipeResponse()
        case .began:
            // A new gesture while one is open means its end event never reached us.
            let closing = finish()
            var response = advance(with: sample, settings: settings)
            response.actions = closing + response.actions
            return response
        case .changed:
            return advance(with: sample, settings: settings)
        case .ended, .cancelled:
            return SwipeResponse(finish())
        }
    }

    /// Ends the gesture without its natural end event (WoW lost focus, events were lost).
    public mutating func finish() -> [SwipeAction] {
        let wasSwiping = isSwiping
        state = .idle
        return wasSwiping ? [.swipeEnded] : []
    }

    private mutating func advance(with sample: ScrollSample, settings: TurnSettings) -> SwipeResponse {
        let travel = sample.fingerTravel
        switch state {
        case .swiping:
            return SwipeResponse([look(travelX: travel.x, travelY: travel.y, settings: settings)].compactMap { $0 })

        case .idle, .pending:
            var pendingX = travel.x
            var pendingY = travel.y
            if case let .pending(travelX, travelY) = state {
                pendingX += travelX
                pendingY += travelY
            }
            guard (pendingX * pendingX + pendingY * pendingY).squareRoot() >= Self.startTravel else {
                // Held back: a few points of scroll are invisible, but a stray zoom tick is not.
                state = .pending(travelX: pendingX, travelY: pendingY)
                return SwipeResponse([], .drop)
            }
            state = .swiping
            remainderX = 0
            remainderY = 0
            let actions = [.swipeBegan] + [look(travelX: pendingX, travelY: pendingY, settings: settings)].compactMap { $0 }
            return SwipeResponse(actions, .forwardAfterActions)
        }
    }

    private mutating func look(travelX: Double, travelY: Double, settings: TurnSettings) -> SwipeAction? {
        // "Fingers right = look right" and "fingers up = look up", the same as moving a mouse.
        let horizontalSign = settings.reverseHorizontal ? -1.0 : 1.0
        let verticalSign = settings.reverseVertical ? -1.0 : 1.0
        let lookX = travelX * settings.turnSpeed * horizontalSign
        let lookY = settings.tiltEnabled
            ? travelY * settings.turnSpeed * TurnSettings.tiltDamping * verticalSign
            : 0

        let totalX = remainderX + lookX
        let totalY = remainderY + lookY
        let wholeX = totalX.rounded(.towardZero)
        let wholeY = totalY.rounded(.towardZero)
        remainderX = totalX - wholeX
        remainderY = totalY - wholeY

        guard wholeX != 0 || wholeY != 0 else { return nil }
        return .look(dx: Int(wholeX), dy: Int(wholeY))
    }
}

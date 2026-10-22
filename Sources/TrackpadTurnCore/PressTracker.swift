/// What the app must send to WoW for a held two-finger trackpad press.
public enum PressAction: Equatable, Sendable {
    /// Press the forward signal key: WoW runs forward while it is mouselooking.
    case runBegan
    /// Release the forward signal key.
    case runEnded
}

/// Turns a held two-finger trackpad press into "run forward", like holding both mouse buttons.
///
/// A two-finger press reaches WoW as a right-click, which on its own starts mouselook. Holding it
/// a moment longer adds forward movement (or toggles autorun, as chosen in the addon's settings).
/// A quick two-finger click stays a plain right-click (interact, loot) because running only
/// starts after `holdBeforeRunning`.
///
/// macOS sends no click at all for a press made in the middle of a two-finger scroll, so the
/// press has to start the gesture.
public struct PressTracker: Sendable {
    /// Seconds a press must be held before running starts; a deliberate click is shorter.
    public static let holdBeforeRunning = 0.2

    /// When the current press, if still held, starts running.
    private var runsAt: Double?
    public private(set) var isRunning = false

    public init() {}

    /// A press is down (waiting or running), so its release must be seen.
    public var isTracking: Bool { runsAt != nil || isRunning }

    /// Returns the time at which to call `checkHold(at:)`, or nil when nothing should happen.
    public mutating func pressBegan(at time: Double, settings: TurnSettings) -> Double? {
        guard settings.isEnabled, settings.pressToRunEnabled else { return nil }
        let deadline = time + Self.holdBeforeRunning
        runsAt = deadline
        return deadline
    }

    public mutating func checkHold(at time: Double) -> [PressAction] {
        // Compare against the stored deadline, not a recomputed hold time: the timer fires at
        // exactly the deadline, and subtracting it back can land a hair short in floating point.
        guard let runsAt, !isRunning, time >= runsAt else { return [] }
        isRunning = true
        return [.runBegan]
    }

    /// The press was released, or must be treated as released (WoW lost focus, events missed).
    public mutating func pressEnded() -> [PressAction] {
        runsAt = nil
        guard isRunning else { return [] }
        isRunning = false
        return [.runEnded]
    }
}

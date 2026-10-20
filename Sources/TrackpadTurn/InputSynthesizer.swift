import CoreGraphics
import TrackpadTurnCore

/// Posts the synthetic input WoW receives: signal keys for the addon and mouse movement for
/// mouselook. Every event carries a marker so our own event tap lets it straight through.
@MainActor
final class InputSynthesizer {
    private static let marker: Int64 = 0x5450_5455_524E // "TPTURN"

    private let source: CGEventSource?

    init() {
        source = CGEventSource(stateID: .privateState)
        source?.userData = Self.marker
        // Synthetic events must never mute the real trackpad, not even briefly.
        source?.localEventsSuppressionInterval = 0
        let permitAll: CGEventFilterMask = [.permitLocalMouseEvents, .permitLocalKeyboardEvents, .permitSystemDefinedEvents]
        source?.setLocalEventsFilterDuringSuppressionState(permitAll, state: .eventSuppressionStateSuppressionInterval)
        source?.setLocalEventsFilterDuringSuppressionState(permitAll, state: .eventSuppressionStateRemoteMouseDrag)
    }

    static func isSynthetic(_ event: CGEvent) -> Bool {
        event.getIntegerValueField(.eventSourceUserData) == marker
    }

    /// Press or release a signal key. Pass `pid` to deliver to WoW after it lost focus.
    func setKey(_ key: WowSignalProtocol.SignalKey, down: Bool, deliverTo pid: pid_t? = nil) {
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: key.virtualKeyCode, keyDown: down) else { return }
        // Report the modifiers the user is really holding: WoW then keeps a correct Shift/Ctrl
        // state, and the addon binds every modifier combination of the signal keys.
        let held = CGEventSource.flagsState(.combinedSessionState)
            .intersection([.maskShift, .maskControl, .maskAlternate, .maskCommand])
        event.flags = held.union(.maskSecondaryFn) // real F-keys carry the fn flag
        post(event, to: pid)
    }

    func tapKey(_ key: WowSignalProtocol.SignalKey) {
        setKey(key, down: true)
        setKey(key, down: false)
    }

    /// Mouse movement that leaves the cursor where it is. While WoW mouselooks it turns the
    /// camera by the delta; otherwise WoW sees a zero-distance move and nothing happens.
    func postLook(dx: Int, dy: Int) {
        let cursor = CGEvent(source: nil)?.location ?? .zero
        guard let event = CGEvent(
            mouseEventSource: source,
            mouseType: .mouseMoved,
            mouseCursorPosition: cursor,
            mouseButton: .left
        ) else { return }
        event.setIntegerValueField(.mouseEventDeltaX, value: Int64(dx))
        event.setIntegerValueField(.mouseEventDeltaY, value: Int64(dy))
        event.setDoubleValueField(.mouseEventDeltaX, value: Double(dx))
        event.setDoubleValueField(.mouseEventDeltaY, value: Double(dy))
        post(event)
    }

    /// Re-inject a captured event behind the events posted before it.
    func repost(_ event: CGEvent) {
        guard let copy = event.copy() else { return }
        copy.setIntegerValueField(.eventSourceUserData, value: Self.marker)
        post(copy)
    }

    private func post(_ event: CGEvent, to pid: pid_t? = nil) {
        event.setIntegerValueField(.eventSourceUserData, value: Self.marker)
        if let pid {
            event.postToPid(pid)
        } else {
            // The HID point is upstream of every tap, so events keep the order they were posted in.
            event.post(tap: .cghidEventTap)
        }
    }
}

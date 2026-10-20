/// The contract between this helper and the TrackpadTurn WoW addon.
///
/// An addon cannot read trackpad gestures, and the helper cannot see WoW's UI. So the helper
/// reports gestures as key presses on keys nobody uses, and the addon (which knows whether the
/// cursor is over the 3D world or over a frame) decides what they mean.
///
/// BREADCRUMB: the addon binds these exact keys in `Addon/TrackpadTurn/TrackpadTurn.lua`
/// (`SIGNAL_KEYS`). Change both sides together and bump `version`.
public enum WowSignalProtocol {
    public static let version = 1

    public enum SignalKey: CaseIterable, Sendable {
        /// Held down for the whole two-finger swipe. Down = swipe began, up = fingers lifted.
        case swipe
        /// Tapped once per pinch-out step.
        case zoomIn
        /// Tapped once per pinch-in step.
        case zoomOut

        /// macOS virtual key code (`kVK_F19`, `kVK_F17`, `kVK_F18`).
        public var virtualKeyCode: UInt16 {
            switch self {
            case .swipe: 80
            case .zoomIn: 64
            case .zoomOut: 79
            }
        }

        /// The key name WoW uses in bindings.
        public var wowKeyName: String {
            switch self {
            case .swipe: "F19"
            case .zoomIn: "F17"
            case .zoomOut: "F18"
            }
        }
    }
}

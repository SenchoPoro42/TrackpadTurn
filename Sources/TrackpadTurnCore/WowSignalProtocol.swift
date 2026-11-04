/// The contract between this helper and the TrackpadTurn WoW addon.
///
/// An addon cannot read trackpad gestures, and the helper cannot see WoW's UI. So the helper
/// reports gestures as key presses on keys nobody uses, and the addon (which knows whether the
/// cursor is over the 3D world or over a frame) decides what they mean.
///
/// BREADCRUMB: the addon binds these exact keys in `Addon/TrackpadTurn/TrackpadTurn.lua`
/// (`SIGNAL_KEY_SETS.mac`). The Windows helper uses other keys, because WoW on Windows ignores
/// F13–F24 (`Windows/src/TrackpadTurn.Core/WowSignalProtocol.cs`). Change all sides together
/// and bump `version`.
public enum WowSignalProtocol {
    public static let version = 3

    public enum SignalKey: CaseIterable, Sendable {
        /// Held down for the whole two-finger swipe. Down = swipe began, up = fingers lifted.
        case swipe
        /// Tapped once per pinch-out step.
        case zoomIn
        /// Tapped once per pinch-in step.
        case zoomOut
        /// Held while a two-finger press is held. The addon binds it to run-while-held or to
        /// toggle autorun (WoW: Settings → AddOns → TrackpadTurn).
        case forward

        /// macOS virtual key code (`kVK_F19`, `kVK_F17`, `kVK_F18`, `kVK_F16`).
        public var virtualKeyCode: UInt16 {
            switch self {
            case .swipe: 80
            case .zoomIn: 64
            case .zoomOut: 79
            case .forward: 106
            }
        }

        /// The key name WoW uses in bindings.
        public var wowKeyName: String {
            switch self {
            case .swipe: "F19"
            case .zoomIn: "F17"
            case .zoomOut: "F18"
            case .forward: "F16"
            }
        }
    }
}

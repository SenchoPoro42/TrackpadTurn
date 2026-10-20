/// User tuning for how gestures map to camera motion. Persisted by the app as JSON.
public struct TurnSettings: Codable, Equatable, Sendable {
    public var isEnabled = true
    /// Mouse-look pixels per point of finger travel.
    public var turnSpeed = 1.0
    /// Up/down swipes tilt the camera. When off, only left/right turns.
    public var tiltEnabled = true
    public var reverseHorizontal = false
    public var reverseVertical = false
    public var pinchZoomEnabled = true
    /// Multiplies how many zoom steps one pinch produces.
    public var zoomSpeed = 1.0

    /// Roblox damps pitch against yaw by this ratio; it keeps a mostly-sideways swipe level.
    public static let tiltDamping = 0.77
    public static let turnSpeedRange = 0.25...4.0
    public static let zoomSpeedRange = 0.25...4.0

    public init() {}

    // Decode field-by-field with defaults so settings saved by older builds keep loading
    // after new fields are added.
    public init(from decoder: Decoder) throws {
        let defaults = TurnSettings()
        let container = try decoder.container(keyedBy: CodingKeys.self)
        isEnabled = try container.decodeIfPresent(Bool.self, forKey: .isEnabled) ?? defaults.isEnabled
        turnSpeed = try container.decodeIfPresent(Double.self, forKey: .turnSpeed) ?? defaults.turnSpeed
        tiltEnabled = try container.decodeIfPresent(Bool.self, forKey: .tiltEnabled) ?? defaults.tiltEnabled
        reverseHorizontal = try container.decodeIfPresent(Bool.self, forKey: .reverseHorizontal) ?? defaults.reverseHorizontal
        reverseVertical = try container.decodeIfPresent(Bool.self, forKey: .reverseVertical) ?? defaults.reverseVertical
        pinchZoomEnabled = try container.decodeIfPresent(Bool.self, forKey: .pinchZoomEnabled) ?? defaults.pinchZoomEnabled
        zoomSpeed = try container.decodeIfPresent(Double.self, forKey: .zoomSpeed) ?? defaults.zoomSpeed
    }
}

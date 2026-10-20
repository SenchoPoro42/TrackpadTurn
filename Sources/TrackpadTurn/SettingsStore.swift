import Foundation
import Observation
import TrackpadTurnCore

@MainActor
@Observable
final class SettingsStore {
    private static let settingsKey = "turnSettings"
    private static let wowFolderKey = "customWowFolderPath"

    var settings: TurnSettings {
        didSet {
            guard settings != oldValue else { return }
            if let data = try? JSONEncoder().encode(settings) {
                UserDefaults.standard.set(data, forKey: Self.settingsKey)
            }
            onChange?(settings)
        }
    }

    /// A WoW install outside the standard Applications folders, picked by the user.
    var customWowFolder: URL? {
        didSet { UserDefaults.standard.set(customWowFolder?.path, forKey: Self.wowFolderKey) }
    }

    @ObservationIgnored var onChange: ((TurnSettings) -> Void)?

    init() {
        let data = UserDefaults.standard.data(forKey: Self.settingsKey)
        settings = data.flatMap { try? JSONDecoder().decode(TurnSettings.self, from: $0) } ?? TurnSettings()
        customWowFolder = UserDefaults.standard.string(forKey: Self.wowFolderKey).map { URL(filePath: $0) }
    }
}

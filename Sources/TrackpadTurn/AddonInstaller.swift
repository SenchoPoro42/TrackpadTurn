import Foundation
import Observation

/// Finds WoW installs and keeps the bundled TrackpadTurn addon installed in each game flavor.
///
/// The addon ships inside the app (Resources/Addon/TrackpadTurn) so helper and addon always
/// speak the same `WowSignalProtocol` version; installs that already exist are updated on launch.
@MainActor
@Observable
final class AddonInstaller {
    struct GameFlavor: Identifiable, Equatable {
        /// e.g. `/Applications/World of Warcraft/_classic_era_`
        let folder: URL
        var installedVersion: String?

        var id: String { folder.path }
        var addOnsFolder: URL { folder.appending(path: "Interface/AddOns", directoryHint: .isDirectory) }
        var addonFolder: URL { addOnsFolder.appending(path: AddonInstaller.addonName, directoryHint: .isDirectory) }

        var displayName: String {
            let knownNames = [
                "_retail_": "Retail",
                "_classic_": "Classic",
                "_classic_era_": "Classic Era",
                "_anniversary_": "Anniversary",
                "_ptr_": "Retail PTR",
                "_xptr_": "Retail Beta",
                "_beta_": "Beta",
                "_classic_ptr_": "Classic PTR",
                "_classic_era_ptr_": "Classic Era PTR",
            ]
            let name = folder.lastPathComponent
            return knownNames[name] ?? name
                .trimmingCharacters(in: CharacterSet(charactersIn: "_"))
                .replacingOccurrences(of: "_", with: " ")
                .capitalized
        }
    }

    nonisolated static let addonName = "TrackpadTurn"

    private(set) var flavors: [GameFlavor] = []
    private(set) var lastError: String?
    let bundledVersion: String?

    @ObservationIgnored private let bundledAddon: URL?

    init() {
        let addon = Bundle.main.resourceURL?.appending(path: "Addon/\(Self.addonName)", directoryHint: .isDirectory)
        bundledAddon = addon.flatMap { FileManager.default.fileExists(atPath: $0.path) ? $0 : nil }
        bundledVersion = bundledAddon.flatMap(Self.addonVersion(in:))
    }

    static var standardWowRoots: [URL] {
        let applications = [
            URL(filePath: "/Applications", directoryHint: .isDirectory),
            FileManager.default.homeDirectoryForCurrentUser.appending(path: "Applications", directoryHint: .isDirectory),
        ]
        return applications.map { $0.appending(path: "World of Warcraft", directoryHint: .isDirectory) }
    }

    func refresh(customRoot: URL?) {
        let roots = Self.standardWowRoots + [customRoot].compactMap { $0 }
        var found: [GameFlavor] = []
        for root in roots {
            for folder in Self.flavorFolders(in: root) where !found.contains(where: { $0.folder == folder }) {
                let installed = Self.addonVersion(in: folder.appending(path: "Interface/AddOns/\(Self.addonName)"))
                found.append(GameFlavor(folder: folder, installedVersion: installed))
            }
        }
        flavors = found
    }

    func needsInstall(_ flavor: GameFlavor) -> Bool {
        flavor.installedVersion == nil || flavor.installedVersion != bundledVersion
    }

    func install(_ flavor: GameFlavor) {
        guard let bundledAddon else {
            lastError = "This build has no bundled addon. Build the app with scripts/build-app.sh."
            return
        }
        let fileManager = FileManager.default
        let staging = flavor.addOnsFolder.appending(path: ".\(Self.addonName)-installing", directoryHint: .isDirectory)
        do {
            try fileManager.createDirectory(at: flavor.addOnsFolder, withIntermediateDirectories: true)
            try? fileManager.removeItem(at: staging)
            try fileManager.copyItem(at: bundledAddon, to: staging)
            // Swap in the new copy in one step so WoW never loads a half-copied addon.
            if fileManager.fileExists(atPath: flavor.addonFolder.path) {
                _ = try fileManager.replaceItemAt(flavor.addonFolder, withItemAt: staging)
            } else {
                try fileManager.moveItem(at: staging, to: flavor.addonFolder)
            }
            lastError = nil
        } catch {
            try? fileManager.removeItem(at: staging)
            lastError = "Could not install into \(flavor.displayName): \(error.localizedDescription)"
        }
        if let index = flavors.firstIndex(of: flavor) {
            flavors[index].installedVersion = Self.addonVersion(in: flavor.addonFolder)
        }
    }

    /// Keeps existing installs in step with this helper. Never installs where the user has not.
    func updateExistingInstalls() {
        for flavor in flavors where flavor.installedVersion != nil && needsInstall(flavor) {
            install(flavor)
        }
    }

    // MARK: Discovery

    /// Game flavors are `_name_` folders, e.g. `_retail_`, `_classic_era_`.
    private static func flavorFolders(in root: URL) -> [URL] {
        let contents = (try? FileManager.default.contentsOfDirectory(
            at: root,
            includingPropertiesForKeys: [.isDirectoryKey],
            options: [.skipsHiddenFiles]
        )) ?? []
        return contents
            .filter { url in
                let name = url.lastPathComponent
                let isDirectory = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
                return isDirectory && name.count > 2 && name.hasPrefix("_") && name.hasSuffix("_")
            }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
    }

    private static func addonVersion(in addonFolder: URL) -> String? {
        let toc = addonFolder.appending(path: "\(addonName).toc")
        guard let text = try? String(contentsOf: toc, encoding: .utf8) else { return nil }
        for line in text.split(whereSeparator: \.isNewline) where line.hasPrefix("## Version:") {
            return line.dropFirst("## Version:".count).trimmingCharacters(in: .whitespaces)
        }
        return "unknown"
    }
}

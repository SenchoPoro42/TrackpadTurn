import AppKit
import Observation

/// Tracks whether World of Warcraft (any flavor) is the frontmost app.
@MainActor
@Observable
final class WowAppMonitor {
    private(set) var frontmostWowPID: pid_t?
    /// Called with the old pid when WoW stops being frontmost.
    @ObservationIgnored var onWowResignedFront: ((pid_t) -> Void)?

    @ObservationIgnored private var observer: NSObjectProtocol?

    init() {
        update(front: NSWorkspace.shared.frontmostApplication)
        observer = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil,
            queue: .main
        ) { [weak self] note in
            let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            MainActor.assumeIsolated { self?.update(front: app) }
        }
    }

    static func isWow(_ app: NSRunningApplication?) -> Bool {
        // Every flavor (Retail, Classic, Classic Era, PTR) ships with this bundle id prefix.
        app?.bundleIdentifier?.hasPrefix("com.blizzard.worldofwarcraft") == true
    }

    private func update(front: NSRunningApplication?) {
        let newPID = Self.isWow(front) ? front?.processIdentifier : nil
        if let oldPID = frontmostWowPID, oldPID != newPID {
            onWowResignedFront?(oldPID)
        }
        frontmostWowPID = newPID
    }
}

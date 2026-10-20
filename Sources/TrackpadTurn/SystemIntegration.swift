import AppKit
import ApplicationServices
import ServiceManagement

/// Accessibility permission lets the app read trackpad events and post input to WoW.
enum AccessibilityPermission {
    static var isGranted: Bool { AXIsProcessTrusted() }

    /// Shows the system "allow this app" prompt (once per launch at most).
    static func requestPrompt() {
        // The key is `kAXTrustedCheckOptionPrompt`; the literal avoids a non-Sendable global.
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    static func openSystemSettings() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }
}

enum LoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func setEnabled(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }
}

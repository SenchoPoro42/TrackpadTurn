import AppKit
import Observation
import SwiftUI

@main
struct TrackpadTurnApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        MenuBarExtra {
            MenuPanel(model: appDelegate.model)
        } label: {
            MenuBarIcon(bridge: appDelegate.model.bridge)
        }
        .menuBarExtraStyle(.window)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    let model = AppModel()

    func applicationDidFinishLaunching(_ notification: Notification) {
        model.start()
    }

    func applicationWillTerminate(_ notification: Notification) {
        // Release a held swipe key so WoW is never left mouselooking.
        model.shutdown()
    }
}

@MainActor
@Observable
final class AppModel {
    let settingsStore = SettingsStore()
    let wowMonitor = WowAppMonitor()
    let installer = AddonInstaller()
    let bridge: GestureBridge

    init() {
        bridge = GestureBridge(settingsStore: settingsStore, wowMonitor: wowMonitor)
    }

    func start() {
        refreshAddons()
        installer.updateExistingInstalls()
        bridge.start()
    }

    func shutdown() {
        bridge.shutdown()
    }

    func refreshAddons() {
        installer.refresh(customRoot: settingsStore.customWowFolder)
    }

    func chooseWowFolder() {
        let panel = NSOpenPanel()
        panel.message = "Choose your “World of Warcraft” folder (the one that holds _retail_ or _classic_era_)."
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        NSApp.activate()
        guard panel.runModal() == .OK, let folder = panel.url else { return }
        settingsStore.customWowFolder = folder
        refreshAddons()
    }
}

private struct MenuBarIcon: View {
    let bridge: GestureBridge

    var body: some View {
        // Filled while WoW is frontmost and swipes are live.
        Image(systemName: bridge.status == .ready
            ? "rectangle.and.hand.point.up.left.filled"
            : "rectangle.and.hand.point.up.left")
    }
}

import SwiftUI
import TrackpadTurnCore

/// The panel shown from the menu bar icon.
struct MenuPanel: View {
    let model: AppModel

    @State private var opensAtLogin = LoginItem.isEnabled
    @State private var loginItemError: String?

    private var settings: Binding<TurnSettings> {
        Binding(get: { model.settingsStore.settings }, set: { model.settingsStore.settings = $0 })
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            StatusCard(status: model.bridge.status, activity: model.bridge.activity)
            Divider()
            feelSection
            Divider()
            AddonSection(model: model)
            Divider()
            footer
        }
        .padding(16)
        .frame(width: 320)
        .onAppear {
            opensAtLogin = LoginItem.isEnabled
            model.refreshAddons()
        }
    }

    private var header: some View {
        HStack(spacing: 10) {
            Image(systemName: "rectangle.and.hand.point.up.left.filled")
                .font(.title2)
                .foregroundStyle(.tint)
            VStack(alignment: .leading, spacing: 1) {
                Text("TrackpadTurn").font(.headline)
                Text("Two-finger turning for World of Warcraft")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Toggle("Enabled", isOn: settings.isEnabled)
                .toggleStyle(.switch)
                .labelsHidden()
                .controlSize(.small)
        }
    }

    private var feelSection: some View {
        VStack(alignment: .leading, spacing: 10) {
            SpeedSlider(title: "Turn speed", value: settings.turnSpeed, range: TurnSettings.turnSpeedRange)
            SpeedSlider(title: "Zoom speed", value: settings.zoomSpeed, range: TurnSettings.zoomSpeedRange)
                .disabled(!model.settingsStore.settings.pinchZoomEnabled)

            VStack(alignment: .leading, spacing: 6) {
                Toggle("Tilt camera with up/down swipes", isOn: settings.tiltEnabled)
                Toggle("Pinch to zoom", isOn: settings.pinchZoomEnabled)
                Toggle("Reverse left/right", isOn: settings.reverseHorizontal)
                Toggle("Reverse up/down", isOn: settings.reverseVertical)
                    .disabled(!model.settingsStore.settings.tiltEnabled)
            }
            .toggleStyle(.checkbox)
        }
        .disabled(!model.settingsStore.settings.isEnabled)
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Toggle("Open at login", isOn: $opensAtLogin)
                    .toggleStyle(.checkbox)
                    .onChange(of: opensAtLogin) { _, enabled in
                        do {
                            try LoginItem.setEnabled(enabled)
                            loginItemError = nil
                        } catch {
                            loginItemError = error.localizedDescription
                            opensAtLogin = LoginItem.isEnabled
                        }
                    }
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
                    .keyboardShortcut("q")
            }
            if let loginItemError {
                Text(loginItemError).font(.caption).foregroundStyle(.red)
            }
        }
    }
}

private struct StatusCard: View {
    let status: GestureBridge.Status
    let activity: GestureBridge.Activity

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Circle()
                    .fill(color)
                    .frame(width: 8, height: 8)
                    .alignmentGuide(.firstTextBaseline) { $0[.bottom] - 1 }
                Text(message)
                    .font(.callout)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if status == .needsPermission {
                Button("Open Accessibility Settings") { AccessibilityPermission.openSystemSettings() }
                    .controlSize(.small)
            }
            if let lastSwipeAt = activity.lastSwipeAt {
                // Lets you confirm swipes reach WoW when something feels off in game.
                HStack(spacing: 4) {
                    Text(activity.isSwiping ? "Swiping now" : "Last swipe \(lastSwipeAt, style: .relative) ago")
                    Text("· \(activity.lookEventsInLastSwipe) moves")
                }
                .font(.caption)
                .foregroundStyle(.secondary)
                .monospacedDigit()
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 8))
    }

    private var color: Color {
        switch status {
        case .ready: .green
        case .waitingForWow: .yellow
        case .disabled: .secondary
        case .needsPermission: .orange
        }
    }

    private var message: String {
        switch status {
        case .ready: "Swipe two fingers over the world to turn. Pinch to zoom."
        case .waitingForWow: "Waiting for World of Warcraft to be in front."
        case .disabled: "Turned off. Your trackpad works as normal in WoW."
        case .needsPermission: "Allow TrackpadTurn under Accessibility so it can read the trackpad and turn your camera."
        }
    }
}

private struct SpeedSlider: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(title)
                Spacer()
                Text(value, format: .number.precision(.fractionLength(2)))
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                + Text("×").foregroundStyle(.secondary)
            }
            .font(.callout)
            Slider(value: $value, in: range)
                .controlSize(.small)
        }
    }
}

private struct AddonSection: View {
    let model: AppModel

    private var installer: AddonInstaller { model.installer }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("WoW addon").font(.callout.weight(.semibold))
                Spacer()
                if let version = installer.bundledVersion {
                    Text("v\(version)").font(.caption).foregroundStyle(.secondary)
                }
            }

            if installer.flavors.isEmpty {
                Text("No World of Warcraft install found in Applications.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            ForEach(installer.flavors) { flavor in
                HStack {
                    Text(flavor.displayName)
                    Spacer()
                    if installer.needsInstall(flavor) {
                        Button(flavor.installedVersion == nil ? "Install" : "Update") {
                            installer.install(flavor)
                        }
                        .controlSize(.small)
                    } else {
                        Label("Installed", systemImage: "checkmark.circle.fill")
                            .font(.caption)
                            .foregroundStyle(.green)
                    }
                }
                .font(.callout)
            }

            HStack {
                Text("After installing, restart WoW once.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Choose WoW Folder…") { model.chooseWowFolder() }
                    .controlSize(.small)
                    .buttonStyle(.link)
                    .font(.caption)
            }

            if let error = installer.lastError {
                Text(error).font(.caption).foregroundStyle(.red)
            }
        }
    }
}

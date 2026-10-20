import Foundation
import Observation

/// Keeps the event tap running for as long as Accessibility permission allows it.
///
/// macOS has no callback for the permission being granted or revoked, so this checks on a
/// slow timer: it starts the tap once allowed and stops it (and reports lost events) if revoked.
@MainActor
@Observable
final class InputAccess {
    private(set) var isActive = false

    /// Events may have been missed (tap timed out, permission revoked): end any open swipe.
    @ObservationIgnored var onEventsMissed: (() -> Void)?

    /// Sees every scroll and pinch event before apps do.
    var eventHandler: EventTap.Handler? {
        get { tap.handler }
        set { tap.handler = newValue }
    }

    @ObservationIgnored private let tap = EventTap()
    @ObservationIgnored private var checkTimer: Timer?

    init() {
        tap.onEventsMissed = { [weak self] in
            Log.access.notice("event tap was disabled by macOS and switched back on")
            self?.onEventsMissed?()
        }
    }

    func start() {
        if !startTapIfAllowed() {
            AccessibilityPermission.requestPrompt()
        }
        checkTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.recheck() }
        }
    }

    func stop() {
        checkTimer?.invalidate()
        checkTimer = nil
        tap.stop()
        isActive = false
    }

    private func recheck() {
        if isActive {
            guard !AccessibilityPermission.isGranted else { return }
            tap.stop()
            isActive = false
            Log.access.notice("Accessibility permission revoked; event tap stopped")
            onEventsMissed?()
        } else {
            startTapIfAllowed()
        }
    }

    @discardableResult
    private func startTapIfAllowed() -> Bool {
        guard AccessibilityPermission.isGranted, tap.start() else { return false }
        isActive = true
        Log.access.notice("event tap started")
        return true
    }
}

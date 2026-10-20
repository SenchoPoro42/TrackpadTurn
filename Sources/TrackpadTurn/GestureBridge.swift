import AppKit
import Observation
import TrackpadTurnCore

/// Routes trackpad gestures bound for WoW into signal keys and mouselook movement.
///
/// Scroll events mostly still reach WoW, so frames under the cursor scroll as usual; the addon
/// decides what a swipe means and stops it zooming. Only the glide after lift and the first few
/// points of travel are held back (see `SwipeTracker`).
@MainActor
@Observable
final class GestureBridge {
    enum Status: Equatable {
        case needsPermission
        case disabled
        case waitingForWow
        case ready
    }

    /// Shown in the panel. Updated only when a swipe starts and ends, never per event, so an
    /// open panel cannot slow down event handling.
    struct Activity: Equatable {
        var isSwiping = false
        var lastSwipeAt: Date?
        var lookEventsInLastSwipe = 0
    }

    private(set) var activity = Activity()

    var status: Status {
        if !access.isActive { return .needsPermission }
        if !settingsStore.settings.isEnabled { return .disabled }
        if wowMonitor.frontmostWowPID == nil { return .waitingForWow }
        return .ready
    }

    @ObservationIgnored private let settingsStore: SettingsStore
    @ObservationIgnored private let wowMonitor: WowAppMonitor
    @ObservationIgnored private let access = InputAccess()
    @ObservationIgnored private let synthesizer = InputSynthesizer()
    @ObservationIgnored private var swipeTracker = SwipeTracker()
    @ObservationIgnored private var pinchTracker = PinchTracker()
    /// The WoW process holding the swipe key down, so it can be released even after focus moves.
    @ObservationIgnored private var swipeTargetPID: pid_t?
    @ObservationIgnored private var lookEventsThisSwipe = 0

    init(settingsStore: SettingsStore, wowMonitor: WowAppMonitor) {
        self.settingsStore = settingsStore
        self.wowMonitor = wowMonitor

        access.eventHandler = { [weak self] type, event in
            self?.handle(type: type, event: event) ?? Unmanaged.passUnretained(event)
        }
        access.onEventsMissed = { [weak self] in self?.abandonSwipe() }
        wowMonitor.onWowResignedFront = { [weak self] _ in self?.abandonSwipe() }
        settingsStore.onChange = { [weak self] settings in
            if !settings.isEnabled { self?.abandonSwipe() }
        }
    }

    func start() {
        access.start()
    }

    func shutdown() {
        abandonSwipe()
        access.stop()
    }

    // MARK: Event routing

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        let passThrough = Unmanaged.passUnretained(event)
        guard !InputSynthesizer.isSynthetic(event) else { return passThrough }

        let isBoundForWow = isBoundForWow(event)
        // Once a swipe is open, follow it to its end even if a window appears under the cursor.
        let followsOpenSwipe = type == .scrollWheel && swipeTracker.isTracking
        guard isBoundForWow || followsOpenSwipe, let nsEvent = NSEvent(cgEvent: event) else {
            if type == .scrollWheel, let wowPID = wowMonitor.frontmostWowPID {
                let targetPID = event.getIntegerValueField(.eventTargetUnixProcessID)
                Log.input.debug("scroll passed untouched: targets pid \(targetPID), WoW is \(wowPID)")
            }
            return passThrough
        }

        switch nsEvent.type {
        case .scrollWheel:
            let sample = ScrollSample(nsEvent)
            if !isBoundForWow, sample.phase == .began {
                // A new gesture somewhere else: close ours, leave this one to its app.
                abandonSwipe()
                return passThrough
            }
            return handleScroll(event, sample: sample)
        case .magnify:
            handlePinch(nsEvent)
            return passThrough
        default:
            return passThrough
        }
    }

    /// WoW must be frontmost (signal keys go to the key window) and the cursor must be over WoW,
    /// not over another app's window in front of it.
    private func isBoundForWow(_ event: CGEvent) -> Bool {
        guard let wowPID = wowMonitor.frontmostWowPID else { return false }
        let targetPID = pid_t(truncatingIfNeeded: event.getIntegerValueField(.eventTargetUnixProcessID))
        return targetPID == 0 || targetPID == wowPID
    }

    private func handleScroll(_ event: CGEvent, sample: ScrollSample) -> Unmanaged<CGEvent>? {
        let response = swipeTracker.consume(sample, settings: settingsStore.settings)
        Log.input.debug("""
            scroll phase=\(String(describing: sample.phase), privacy: .public) \
            momentum=\(String(describing: sample.momentumPhase), privacy: .public) \
            dx=\(sample.deltaX, format: .fixed(precision: 1)) dy=\(sample.deltaY, format: .fixed(precision: 1)) \
            → \(String(describing: response.disposition), privacy: .public) \
            \(String(describing: response.actions), privacy: .public)
            """)

        for action in response.actions {
            switch action {
            case .swipeBegan:
                synthesizer.setKey(.swipe, down: true)
                swipeTargetPID = wowMonitor.frontmostWowPID
                lookEventsThisSwipe = 0
                activity.isSwiping = true
                activity.lastSwipeAt = .now
            case let .look(dx, dy):
                synthesizer.postLook(dx: dx, dy: dy)
                lookEventsThisSwipe += 1
            case .swipeEnded:
                releaseSwipeKey()
            }
        }

        switch response.disposition {
        case .forward:
            return Unmanaged.passUnretained(event)
        case .forwardAfterActions:
            // WoW must see the key before this scroll, or the swipe's first wheel tick would zoom.
            synthesizer.repost(event)
            return nil
        case .drop:
            return nil
        }
    }

    private func handlePinch(_ nsEvent: NSEvent) {
        let sample = PinchSample(phase: GesturePhase(nsEvent.phase), magnification: Double(nsEvent.magnification))
        let steps = pinchTracker.consume(sample, settings: settingsStore.settings)
        guard steps != 0 else { return }
        let key: WowSignalProtocol.SignalKey = steps > 0 ? .zoomIn : .zoomOut
        for _ in 0..<abs(steps) {
            synthesizer.tapKey(key)
        }
    }

    /// Ends a swipe whose end event will never reach us, so WoW is never left mouselooking.
    private func abandonSwipe() {
        guard swipeTracker.finish().contains(.swipeEnded) else { return }
        releaseSwipeKey()
    }

    private func releaseSwipeKey() {
        // Post straight to the WoW process when it is no longer the app that receives keys.
        let deliverTo = swipeTargetPID == wowMonitor.frontmostWowPID ? nil : swipeTargetPID
        synthesizer.setKey(.swipe, down: false, deliverTo: deliverTo)
        swipeTargetPID = nil
        activity.isSwiping = false
        activity.lookEventsInLastSwipe = lookEventsThisSwipe
        Log.input.notice("swipe released after \(self.lookEventsThisSwipe) look moves")
    }
}

// MARK: - NSEvent → core samples

extension ScrollSample {
    init(_ event: NSEvent) {
        self.init(
            phase: GesturePhase(event.phase),
            momentumPhase: MomentumPhase(event.momentumPhase),
            isContinuous: event.hasPreciseScrollingDeltas,
            deltaX: Double(event.scrollingDeltaX),
            deltaY: Double(event.scrollingDeltaY),
            isDirectionInvertedFromDevice: event.isDirectionInvertedFromDevice
        )
    }
}

extension GesturePhase {
    init(_ phase: NSEvent.Phase) {
        if phase.contains(.began) {
            self = .began
        } else if phase.contains(.changed) || phase.contains(.stationary) {
            self = .changed
        } else if phase.contains(.ended) {
            self = .ended
        } else if phase.contains(.cancelled) {
            self = .cancelled
        } else if phase.contains(.mayBegin) {
            self = .mayBegin
        } else {
            self = .none
        }
    }
}

extension MomentumPhase {
    init(_ phase: NSEvent.Phase) {
        if phase.contains(.began) {
            self = .began
        } else if phase.contains(.changed) || phase.contains(.stationary) {
            self = .changed
        } else if phase.contains(.ended) || phase.contains(.cancelled) {
            self = .ended
        } else {
            self = .none
        }
    }
}

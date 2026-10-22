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
    @ObservationIgnored private var pressTracker = PressTracker()
    /// Fires when a held two-finger press has lasted long enough to run.
    @ObservationIgnored private var pressHoldTask: Task<Void, Never>?
    /// The WoW process holding the forward key down.
    @ObservationIgnored private var runTargetPID: pid_t?

    init(settingsStore: SettingsStore, wowMonitor: WowAppMonitor) {
        self.settingsStore = settingsStore
        self.wowMonitor = wowMonitor

        access.eventHandler = { [weak self] type, event in
            self?.handle(type: type, event: event) ?? Unmanaged.passUnretained(event)
        }
        access.onEventsMissed = { [weak self] in self?.abandonGestures() }
        wowMonitor.onWowResignedFront = { [weak self] _ in self?.abandonGestures() }
        settingsStore.onChange = { [weak self] settings in
            if !settings.isEnabled { self?.abandonGestures() }
        }
    }

    func start() {
        access.start()
    }

    func shutdown() {
        abandonGestures()
        access.stop()
    }

    // MARK: Event routing

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        let passThrough = Unmanaged.passUnretained(event)
        guard !InputSynthesizer.isSynthetic(event) else { return passThrough }

        let isBoundForWow = isBoundForWow(event)
        // Once a swipe or press is open, follow it to its end even if a window appears under the cursor.
        let followsOpenSwipe = type == .scrollWheel && swipeTracker.isTracking
        let followsOpenPress = type == .rightMouseUp && pressTracker.isTracking
        guard isBoundForWow || followsOpenSwipe || followsOpenPress, let nsEvent = NSEvent(cgEvent: event) else {
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
        case .rightMouseDown:
            handlePressDown(event)
            return passThrough
        case .rightMouseUp:
            return handlePressUp(event)
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

    // MARK: Press to run

    /// macOS marks mouse events that a trackpad made with `NSEventSubtypeTouch` (3); a mouse's
    /// right button never sends it, so a real mouse keeps its plain right-drag.
    private static let trackpadEventSubtype: Int64 = 3

    private func handlePressDown(_ event: CGEvent) {
        guard event.getIntegerValueField(.mouseEventSubtype) == Self.trackpadEventSubtype else { return }
        let now = ProcessInfo.processInfo.systemUptime
        guard let runsAt = pressTracker.pressBegan(at: now, settings: settingsStore.settings) else { return }
        pressHoldTask?.cancel()
        pressHoldTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(runsAt - now))
            guard !Task.isCancelled, let self else { return }
            // Check at the deadline this task was made for; a newer press has a later one.
            guard pressTracker.checkHold(at: runsAt).contains(.runBegan) else { return }
            synthesizer.setKey(.forward, down: true)
            runTargetPID = wowMonitor.frontmostWowPID
            Log.input.debug("press held: running forward")
        }
    }

    private func handlePressUp(_ event: CGEvent) -> Unmanaged<CGEvent>? {
        pressHoldTask?.cancel()
        guard pressTracker.pressEnded().contains(.runEnded) else { return Unmanaged.passUnretained(event) }
        // Stop running before WoW sees the button release: the release ends mouselook, and the
        // forward binding only applies while mouselooking.
        releaseForwardKey()
        synthesizer.repost(event)
        return nil
    }

    private func releaseForwardKey() {
        synthesizer.setKey(.forward, down: false, deliverTo: deliveryPID(for: runTargetPID))
        runTargetPID = nil
        Log.input.debug("press released: stopped running")
    }

    // MARK: Ending gestures

    /// Ends gestures whose end events will never reach us, so WoW is never left mouselooking
    /// or running.
    private func abandonGestures() {
        abandonSwipe()
        pressHoldTask?.cancel()
        if pressTracker.pressEnded().contains(.runEnded) {
            releaseForwardKey()
        }
    }

    private func abandonSwipe() {
        if swipeTracker.finish().contains(.swipeEnded) {
            releaseSwipeKey()
        }
    }

    /// Post straight to the WoW process when it is no longer the app that receives keys.
    private func deliveryPID(for targetPID: pid_t?) -> pid_t? {
        targetPID == wowMonitor.frontmostWowPID ? nil : targetPID
    }

    private func releaseSwipeKey() {
        synthesizer.setKey(.swipe, down: false, deliverTo: deliveryPID(for: swipeTargetPID))
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

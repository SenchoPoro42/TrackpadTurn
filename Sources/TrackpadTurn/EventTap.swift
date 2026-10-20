import CoreGraphics
import Foundation

/// Owns the CGEventTap that sees scroll and pinch events before apps do.
///
/// The tap sits at the annotated session point, so each event already names the process it is
/// going to (`eventTargetUnixProcessID`). It is an active tap: the handler may drop an event and
/// re-post it later to control ordering.
@MainActor
final class EventTap {
    /// Return the event (possibly the same one) to pass it on, or nil to drop it.
    typealias Handler = @MainActor (CGEventType, CGEvent) -> Unmanaged<CGEvent>?

    var handler: Handler?
    private var machPort: CFMachPort?
    private var runLoopSource: CFRunLoopSource?

    /// Called after macOS disabled the tap and it was switched back on: events (maybe the end of
    /// a swipe) were missed in between.
    var onEventsMissed: (() -> Void)?

    var isRunning: Bool { machPort != nil }

    /// Fails while the app lacks Accessibility permission.
    @discardableResult
    func start() -> Bool {
        guard machPort == nil else { return true }

        // NSEvent types 29 (gesture) and 30 (magnify) have no CGEventType case; pinches arrive as either.
        let gestureType: UInt64 = 29
        let magnifyType: UInt64 = 30
        let mask: CGEventMask = (1 << CGEventType.scrollWheel.rawValue) | (1 << gestureType) | (1 << magnifyType)

        guard let port = CGEvent.tapCreate(
            tap: .cgAnnotatedSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: mask,
            callback: eventTapCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else { return false }

        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, port, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: port, enable: true)
        machPort = port
        runLoopSource = source
        return true
    }

    func stop() {
        if let runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        }
        if let machPort {
            CGEvent.tapEnable(tap: machPort, enable: false)
            CFMachPortInvalidate(machPort)
        }
        machPort = nil
        runLoopSource = nil
    }

    fileprivate func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            // macOS switches a tap off when a callback is slow; switch it straight back on.
            if let machPort { CGEvent.tapEnable(tap: machPort, enable: true) }
            onEventsMissed?()
            return Unmanaged.passUnretained(event)
        default:
            return handler?(type, event) ?? Unmanaged.passUnretained(event)
        }
    }
}

private func eventTapCallback(
    proxy: CGEventTapProxy,
    type: CGEventType,
    event: CGEvent,
    userInfo: UnsafeMutableRawPointer?
) -> Unmanaged<CGEvent>? {
    guard let userInfo else { return Unmanaged.passUnretained(event) }
    let tap = Unmanaged<EventTap>.fromOpaque(userInfo).takeUnretainedValue()
    // The run loop source is on the main run loop, so callbacks arrive on the main thread.
    let incoming = MainThreadBox(event)
    return MainActor.assumeIsolated {
        MainThreadBox(tap.handle(type: type, event: incoming.value))
    }.value
}

/// CGEvent is not Sendable, but tap callbacks run on the main thread, so it never crosses threads.
private struct MainThreadBox<Value>: @unchecked Sendable {
    let value: Value
    init(_ value: Value) { self.value = value }
}

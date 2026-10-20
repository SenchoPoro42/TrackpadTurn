import Foundation
import Testing
@testable import TrackpadTurnCore

@Suite struct PinchTrackerTests {
    let settings = TurnSettings()
    let step = PinchTracker.magnificationPerStep

    @Test func fingersApartZoomsIn() {
        var tracker = PinchTracker()
        #expect(tracker.consume(PinchSample(phase: .began, magnification: step * 2), settings: settings) == 2)
    }

    @Test func fingersTogetherZoomsOut() {
        var tracker = PinchTracker()
        #expect(tracker.consume(PinchSample(phase: .began, magnification: -step * 3), settings: settings) == -3)
    }

    @Test func smallChangesAccumulate() {
        var tracker = PinchTracker()
        var steps = tracker.consume(PinchSample(phase: .began, magnification: step * 0.6), settings: settings)
        steps += tracker.consume(PinchSample(phase: .changed, magnification: step * 0.6), settings: settings)
        #expect(steps == 1)
    }

    @Test func leftoverDoesNotCarryIntoNextPinch() {
        var tracker = PinchTracker()
        _ = tracker.consume(PinchSample(phase: .began, magnification: step * 0.9), settings: settings)
        _ = tracker.consume(PinchSample(phase: .ended, magnification: 0), settings: settings)
        #expect(tracker.consume(PinchSample(phase: .began, magnification: step * 0.2), settings: settings) == 0)
    }

    @Test func zoomSpeedScalesSteps() {
        var tracker = PinchTracker()
        var fast = settings
        fast.zoomSpeed = 2
        #expect(tracker.consume(PinchSample(phase: .began, magnification: step * 2), settings: fast) == 4)
    }

    @Test func pinchOffDoesNothing() {
        var tracker = PinchTracker()
        var off = settings
        off.pinchZoomEnabled = false
        #expect(tracker.consume(PinchSample(phase: .began, magnification: 1), settings: off) == 0)
    }
}

@Suite struct TurnSettingsTests {
    @Test func olderSavedSettingsKeepDefaultsForNewFields() throws {
        let saved = Data(#"{"turnSpeed": 2.0}"#.utf8)
        let settings = try JSONDecoder().decode(TurnSettings.self, from: saved)
        #expect(settings.turnSpeed == 2.0)
        #expect(settings.isEnabled)
        #expect(settings.pinchZoomEnabled)
    }
}

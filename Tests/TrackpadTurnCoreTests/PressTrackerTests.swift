import Testing
@testable import TrackpadTurnCore

@Suite struct PressTrackerTests {
    let settings = TurnSettings()
    let hold = PressTracker.holdBeforeRunning

    @Test func heldPressRunsUntilReleased() {
        var tracker = PressTracker()
        let deadline = tracker.pressBegan(at: 10, settings: settings)
        #expect(deadline == 10 + hold)
        #expect(tracker.checkHold(at: 10 + hold) == [.runBegan])
        #expect(tracker.isRunning)
        #expect(tracker.pressEnded() == [.runEnded])
        #expect(!tracker.isTracking)
    }

    @Test func quickClickStaysAPlainRightClick() {
        var tracker = PressTracker()
        _ = tracker.pressBegan(at: 10, settings: settings)
        #expect(tracker.pressEnded() == [])
        // The hold timer fires after the release: nothing may start.
        #expect(tracker.checkHold(at: 10 + hold) == [])
    }

    @Test func earlyCheckDoesNotRun() {
        var tracker = PressTracker()
        _ = tracker.pressBegan(at: 10, settings: settings)
        #expect(tracker.checkHold(at: 10 + hold / 2) == [])
        #expect(!tracker.isRunning)
    }

    @Test func runStartsOnlyOnce() {
        var tracker = PressTracker()
        _ = tracker.pressBegan(at: 10, settings: settings)
        #expect(tracker.checkHold(at: 10 + hold) == [.runBegan])
        #expect(tracker.checkHold(at: 10 + hold * 2) == [])
    }

    @Test func staleTimerFromEarlierPressCannotStartLaterOne() {
        var tracker = PressTracker()
        _ = tracker.pressBegan(at: 10, settings: settings)
        _ = tracker.pressEnded()
        _ = tracker.pressBegan(at: 10 + hold * 0.9, settings: settings)
        // The first press's timer fires; the second press has not been held long enough.
        #expect(tracker.checkHold(at: 10 + hold) == [])
    }

    @Test func settingOffNeverRuns() {
        var tracker = PressTracker()
        var off = settings
        off.pressToRunEnabled = false
        #expect(tracker.pressBegan(at: 10, settings: off) == nil)
        #expect(tracker.checkHold(at: 20) == [])
    }

    @Test func appDisabledNeverRuns() {
        var tracker = PressTracker()
        var off = settings
        off.isEnabled = false
        #expect(tracker.pressBegan(at: 10, settings: off) == nil)
    }
}

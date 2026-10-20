import Testing
@testable import TrackpadTurnCore

@Suite struct SwipeTrackerTests {
    let settings = TurnSettings()

    // MARK: Lifecycle

    @Test func fullSwipeSendsBeginLookEnd() {
        var tracker = SwipeTracker()
        #expect(tracker.consume(ScrollSample(phase: .mayBegin), settings: settings) == SwipeResponse())
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 10), settings: settings)
            == SwipeResponse([.swipeBegan, .look(dx: 10, dy: 0)], .forwardAfterActions))
        #expect(tracker.consume(ScrollSample(phase: .changed, deltaX: 4), settings: settings)
            == SwipeResponse([.look(dx: 4, dy: 0)], .forward))
        #expect(tracker.consume(ScrollSample(phase: .ended), settings: settings)
            == SwipeResponse([.swipeEnded], .forward))
        #expect(!tracker.isTracking)
    }

    @Test func changedWithoutBeganStillBeginsOnce() {
        var tracker = SwipeTracker()
        #expect(tracker.consume(ScrollSample(phase: .changed, deltaX: 3), settings: settings).actions
            == [.swipeBegan, .look(dx: 3, dy: 0)])
        #expect(tracker.consume(ScrollSample(phase: .changed, deltaX: 3), settings: settings).actions
            == [.look(dx: 3, dy: 0)])
    }

    @Test func cancelledEndsSwipe() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 5), settings: settings)
        #expect(tracker.consume(ScrollSample(phase: .cancelled), settings: settings).actions == [.swipeEnded])
    }

    @Test func beganWhileSwipingClosesTheLostSwipeFirst() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 5), settings: settings)
        // The `.ended` went to another app's window; the next swipe must re-signal the addon.
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 5), settings: settings).actions
            == [.swipeEnded, .swipeBegan, .look(dx: 5, dy: 0)])
    }

    @Test func finishingTwiceSendsOneRelease() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 5), settings: settings)
        #expect(tracker.finish() == [.swipeEnded])
        #expect(tracker.consume(ScrollSample(phase: .ended), settings: settings).actions == [])
    }

    // MARK: Start threshold

    @Test func tinyMovementNeverStartsSwipe() {
        var tracker = SwipeTracker()
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 1), settings: settings) == SwipeResponse([], .drop))
        #expect(tracker.consume(ScrollSample(phase: .changed, deltaY: 1), settings: settings) == SwipeResponse([], .drop))
        #expect(tracker.isTracking)
        #expect(tracker.consume(ScrollSample(phase: .ended), settings: settings) == SwipeResponse())
        #expect(!tracker.isTracking)
    }

    @Test func travelBeforeStartIsNotLost() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 2), settings: settings)
        #expect(tracker.consume(ScrollSample(phase: .changed, deltaX: 2), settings: settings)
            == SwipeResponse([.swipeBegan, .look(dx: 4, dy: 0)], .forwardAfterActions))
    }

    @Test func startThresholdIsDistanceNotPerAxis() {
        var tracker = SwipeTracker()
        // 2.5 points on each axis is ~3.5 points of travel.
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 2.5, deltaY: -2.5), settings: settings).actions.first
            == .swipeBegan)
    }

    // MARK: Glide, devices, settings

    @Test func momentumAfterLiftIsDroppedWithoutTurning() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 5), settings: settings)
        _ = tracker.consume(ScrollSample(phase: .ended), settings: settings)
        let glide = ScrollSample(phase: .none, momentumPhase: .changed, deltaX: 50)
        #expect(tracker.consume(glide, settings: settings) == SwipeResponse([], .drop))
    }

    @Test func wheelMouseIsIgnored() {
        var tracker = SwipeTracker()
        let wheel = ScrollSample(phase: .none, isContinuous: false, deltaY: 3)
        #expect(tracker.consume(wheel, settings: settings) == SwipeResponse())
    }

    @Test func disabledLeavesEverythingAlone() {
        var tracker = SwipeTracker()
        var off = settings
        off.isEnabled = false
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 5), settings: off) == SwipeResponse())
        let glide = ScrollSample(phase: .none, momentumPhase: .changed, deltaY: 9)
        #expect(tracker.consume(glide, settings: off) == SwipeResponse())
    }

    // MARK: Direction

    @Test func naturalScrollingFingersRightLooksRight() {
        var tracker = SwipeTracker()
        // Natural scrolling: content follows the fingers, deltas equal finger travel.
        let sample = ScrollSample(phase: .began, deltaX: 8, isDirectionInvertedFromDevice: true)
        #expect(tracker.consume(sample, settings: settings).actions == [.swipeBegan, .look(dx: 8, dy: 0)])
    }

    @Test func traditionalScrollingFingersRightLooksRight() {
        var tracker = SwipeTracker()
        // Traditional scrolling: macOS reports the opposite sign for the same finger motion.
        let sample = ScrollSample(phase: .began, deltaX: -8, isDirectionInvertedFromDevice: false)
        #expect(tracker.consume(sample, settings: settings).actions == [.swipeBegan, .look(dx: 8, dy: 0)])
    }

    @Test func fingersUpLooksUpWithDampedPitch() {
        var tracker = SwipeTracker()
        // Screen y grows downward, so fingers moving up are negative, like a mouse moving up.
        let sample = ScrollSample(phase: .began, deltaY: -100)
        #expect(tracker.consume(sample, settings: settings).actions == [.swipeBegan, .look(dx: 0, dy: -77)])
    }

    @Test func reverseTogglesFlipEachAxis() {
        var tracker = SwipeTracker()
        var reversed = settings
        reversed.reverseHorizontal = true
        reversed.reverseVertical = true
        let sample = ScrollSample(phase: .began, deltaX: 10, deltaY: -100)
        #expect(tracker.consume(sample, settings: reversed).actions == [.swipeBegan, .look(dx: -10, dy: 77)])
    }

    @Test func tiltOffKeepsCameraLevel() {
        var tracker = SwipeTracker()
        var flat = settings
        flat.tiltEnabled = false
        let sample = ScrollSample(phase: .began, deltaX: 5, deltaY: 40)
        #expect(tracker.consume(sample, settings: flat).actions == [.swipeBegan, .look(dx: 5, dy: 0)])
    }

    // MARK: Precision

    @Test func slowSwipesAccumulateFractions() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 3), settings: settings)
        var actions: [SwipeAction] = []
        for _ in 0..<3 {
            actions += tracker.consume(ScrollSample(phase: .changed, deltaX: 0.4), settings: settings).actions
        }
        #expect(actions == [.look(dx: 1, dy: 0)])
    }

    @Test func turnSpeedScalesLook() {
        var tracker = SwipeTracker()
        var fast = settings
        fast.turnSpeed = 2.5
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 4), settings: fast).actions
            == [.swipeBegan, .look(dx: 10, dy: 0)])
    }

    @Test func fractionsResetBetweenSwipes() {
        var tracker = SwipeTracker()
        _ = tracker.consume(ScrollSample(phase: .began, deltaX: 3.6), settings: settings) // leaves 0.6
        _ = tracker.consume(ScrollSample(phase: .ended), settings: settings)
        // 3.5 alone is 3 whole pixels; with a carried 0.6 it would be 4.
        #expect(tracker.consume(ScrollSample(phase: .began, deltaX: 3.5), settings: settings).actions
            == [.swipeBegan, .look(dx: 3, dy: 0)])
    }
}

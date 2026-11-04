using TrackpadTurn.Core;
using static TrackpadTurn.Core.GesturePhase;

namespace TrackpadTurn.Core.Tests;

public class TouchpadGestureRecognizerTests
{
    private const double Ppm = TouchpadGestureRecognizer.PointsPerMillimetre;

    private static TouchpadFrame Frame(double time, params (int Id, double X, double Y)[] contacts) =>
        new(time, [.. contacts.Select(contact => new TouchpadContact(contact.Id, contact.X, contact.Y))], IsButtonDown: false);

    private static RecognizedGesture Scroll(GesturePhase phase, double dxMm = 0, double dyMm = 0) =>
        new RecognizedGesture.Scroll(new ScrollSample(phase, DeltaX: dxMm * Ppm, DeltaY: dyMm * Ppm));

    private static RecognizedGesture Pinch(GesturePhase phase, double magnification) =>
        new RecognizedGesture.Pinch(new PinchSample(phase, magnification));

    private static RecognizedGesture[] Gestures(params RecognizedGesture[] gestures) => gestures;

    [Fact]
    public void TwoFingersMovingTogetherScroll()
    {
        var recognizer = new TouchpadGestureRecognizer();
        Assert.Empty(recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10))));
        Assert.Equal(Gestures(Scroll(Began, dxMm: 1)), recognizer.Consume(Frame(0.01, (1, 11, 10), (2, 31, 10))));
        Assert.Equal(Gestures(Scroll(Changed, dxMm: 0.5)), recognizer.Consume(Frame(0.02, (1, 11.5, 10), (2, 31.5, 10))));
        Assert.Equal(Gestures(Scroll(Ended)), recognizer.Consume(Frame(0.03)));
        Assert.False(recognizer.HasContacts);
    }

    [Fact]
    public void FingersUpScrollWithNegativeY()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 20), (2, 30, 20)));
        // Touchpad y grows downward, like the screen, so the deltas are finger travel.
        Assert.Equal(Gestures(Scroll(Began, dyMm: -2)), recognizer.Consume(Frame(0.01, (1, 10, 18), (2, 30, 18))));
    }

    [Fact]
    public void RestingFingersStartNothing()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        Assert.Empty(recognizer.Consume(Frame(0.01, (1, 10.2, 10.1), (2, 30.1, 9.9))));
        Assert.Empty(recognizer.Consume(Frame(0.02)));
    }

    [Fact]
    public void StationaryFramesDuringAScrollSendNothing()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        recognizer.Consume(Frame(0.01, (1, 12, 10), (2, 32, 10)));
        Assert.Empty(recognizer.Consume(Frame(0.02, (1, 12, 10), (2, 32, 10))));
    }

    [Fact]
    public void FingersSpreadingPinch()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        Assert.Equal(Gestures(Pinch(Began, 22.0 / 20 - 1)), recognizer.Consume(Frame(0.01, (1, 9, 10), (2, 31, 10))));
        Assert.Equal(Gestures(Pinch(Changed, 18.0 / 22 - 1)), recognizer.Consume(Frame(0.02, (1, 11, 10), (2, 29, 10))));
        Assert.Equal(Gestures(Pinch(Ended, 0)), recognizer.Consume(Frame(0.03)));
    }

    [Fact]
    public void PinchStaysAPinchWhenTheFingersDrift()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        recognizer.Consume(Frame(0.01, (1, 9, 10), (2, 31, 10)));
        // Both fingers slide 3 mm right while spreading 1 mm more.
        var drift = recognizer.Consume(Frame(0.02, (1, 12, 10), (2, 35, 10)));
        Assert.IsType<RecognizedGesture.Pinch>(Assert.Single(drift));
    }

    [Fact]
    public void ThirdFingerCancelsTheScrollUntilFingersLift()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        recognizer.Consume(Frame(0.01, (1, 12, 10), (2, 32, 10)));
        // Three fingers belong to Windows' own gestures (task view, desktops).
        Assert.Equal(Gestures(Scroll(Cancelled)), recognizer.Consume(Frame(0.02, (1, 12, 10), (2, 32, 10), (3, 50, 10))));
        recognizer.Consume(Frame(0.03, (1, 12, 10), (2, 32, 10)));
        Assert.Empty(recognizer.Consume(Frame(0.04, (1, 20, 10), (2, 40, 10))));
        recognizer.Consume(Frame(0.05));
        recognizer.Consume(Frame(0.06, (4, 10, 10), (5, 30, 10)));
        Assert.Equal(Gestures(Scroll(Began, dxMm: 2)), recognizer.Consume(Frame(0.07, (4, 12, 10), (5, 32, 10))));
    }

    [Fact]
    public void LiftingOneFingerEndsTheScroll()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        recognizer.Consume(Frame(0.01, (1, 12, 10), (2, 32, 10)));
        Assert.Equal(Gestures(Scroll(Ended)), recognizer.Consume(Frame(0.02, (1, 12, 10))));
        Assert.True(recognizer.HasContacts);
    }

    [Fact]
    public void NewPairOfFingersStartsANewGesture()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        recognizer.Consume(Frame(0.01, (1, 12, 10), (2, 32, 10)));
        // Finger 2 lifted and another landed in the same report: the old swipe is over.
        Assert.Equal(Gestures(Scroll(Ended)), recognizer.Consume(Frame(0.02, (1, 12, 10), (3, 40, 10))));
        Assert.Equal(Gestures(Scroll(Began, dxMm: 1)), recognizer.Consume(Frame(0.03, (1, 13, 10), (3, 41, 10))));
    }

    [Fact]
    public void ResetEndsTheOpenGesture()
    {
        var recognizer = new TouchpadGestureRecognizer();
        recognizer.Consume(Frame(0.00, (1, 10, 10), (2, 30, 10)));
        recognizer.Consume(Frame(0.01, (1, 12, 10), (2, 32, 10)));
        Assert.Equal(Gestures(Scroll(Ended)), recognizer.Reset());
        Assert.False(recognizer.HasContacts);
        Assert.Empty(recognizer.Reset());
    }

    [Fact]
    public void OneFingerDoesNothing()
    {
        var recognizer = new TouchpadGestureRecognizer();
        Assert.Empty(recognizer.Consume(Frame(0.00, (1, 10, 10))));
        Assert.Empty(recognizer.Consume(Frame(0.01, (1, 40, 10))));
        Assert.Empty(recognizer.Consume(Frame(0.02)));
    }
}

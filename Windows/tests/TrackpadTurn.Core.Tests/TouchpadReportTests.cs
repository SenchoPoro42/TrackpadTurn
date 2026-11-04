using TrackpadTurn.Core;

namespace TrackpadTurn.Core.Tests;

public class ContactAxisTests
{
    [Fact]
    public void CentimetreUnitsConvertToMillimetres()
    {
        // HID unit 0x11 = SI linear (cm); exponent nibble 0xE = -2. Physical 0…1100 → 0…110 mm.
        var axis = ContactAxis.FromHid(logicalMin: 0, logicalMax: 2200, physicalMin: 0, physicalMax: 1100, units: 0x11, unitExponent: 0x0E);
        Assert.Equal(55.0, axis.ToMillimetres(1100), precision: 6);
        Assert.Equal(110.0, axis.ToMillimetres(2200), precision: 6);
    }

    [Fact]
    public void InchUnitsConvertToMillimetres()
    {
        // HID unit 0x13 = English linear (inch); exponent -2. Physical 400 → 4 in = 101.6 mm.
        var axis = ContactAxis.FromHid(logicalMin: 0, logicalMax: 4000, physicalMin: 0, physicalMax: 400, units: 0x13, unitExponent: 0x0E);
        Assert.Equal(101.6, axis.ToMillimetres(4000), precision: 6);
    }

    [Fact]
    public void MissingPhysicalSizeFallsBackToATypicalTouchpad()
    {
        var axis = ContactAxis.FromHid(logicalMin: 0, logicalMax: 1000, physicalMin: 0, physicalMax: 0, units: 0, unitExponent: 0);
        Assert.Equal(ContactAxis.FallbackSpanMillimetres, axis.ToMillimetres(1000), precision: 6);
    }
}

public class TouchFrameAssemblerTests
{
    private static HidContact Contact(int id, int x, int y, bool tip = true, bool confident = true) =>
        new(id, tip, confident, x, y);

    private static readonly ContactAxis Axis = new(LogicalMin: 0, LogicalMax: 1000, MinMillimetres: 0, MaxMillimetres: 100);

    private static TouchFrameAssembler Assembler() => new(Axis, Axis);

    [Fact]
    public void ParallelReportIsOneFrame()
    {
        var frames = Assembler().Consume(new HidTouchReport(ContactCount: 2, [Contact(1, 100, 200), Contact(2, 300, 200)], IsButtonDown: false), time: 1);
        var frame = Assert.Single(frames);
        Assert.Equal([new TouchpadContact(1, 10, 20), new TouchpadContact(2, 30, 20)], frame.Contacts);
        Assert.Equal(1, frame.Time);
    }

    [Fact]
    public void UnusedFingerSlotsAreIgnored()
    {
        // A report has a slot per possible finger; only the first ContactCount are real.
        var frame = Assert.Single(Assembler().Consume(
            new HidTouchReport(ContactCount: 1, [Contact(1, 100, 100), Contact(7, 900, 900)], IsButtonDown: false), time: 1));
        Assert.Equal([1], frame.Contacts.Select(contact => contact.Id));
    }

    [Fact]
    public void HybridContinuationTakesOnlyTheRemainingContacts()
    {
        var assembler = Assembler();
        assembler.Consume(new HidTouchReport(ContactCount: 3, [Contact(1, 100, 100), Contact(2, 200, 100)], IsButtonDown: false), time: 1);
        var frame = Assert.Single(assembler.Consume(
            new HidTouchReport(ContactCount: 0, [Contact(3, 300, 100), Contact(9, 900, 900)], IsButtonDown: false), time: 1.001));
        Assert.Equal([1, 2, 3], frame.Contacts.Select(contact => contact.Id));
    }

    [Fact]
    public void HybridReportsAreJoinedIntoOneFrame()
    {
        // Hybrid mode: the first report carries the real count, the rest carry zero.
        var assembler = Assembler();
        Assert.Empty(assembler.Consume(new HidTouchReport(ContactCount: 3, [Contact(1, 100, 100), Contact(2, 200, 100)], IsButtonDown: true), time: 1));
        var frame = Assert.Single(assembler.Consume(new HidTouchReport(ContactCount: 0, [Contact(3, 300, 100)], IsButtonDown: false), time: 1.001));
        Assert.Equal([1, 2, 3], frame.Contacts.Select(contact => contact.Id));
        Assert.True(frame.IsButtonDown);
    }

    [Fact]
    public void UnfinishedFrameIsSentWhenTheNextOneStarts()
    {
        // Some touchpads report a contact count that is higher than what they send.
        var assembler = Assembler();
        assembler.Consume(new HidTouchReport(ContactCount: 2, [Contact(1, 100, 100)], IsButtonDown: false), time: 1);
        var frames = assembler.Consume(new HidTouchReport(ContactCount: 1, [Contact(1, 110, 100)], IsButtonDown: false), time: 1.008);
        Assert.Equal(2, frames.Count);
        Assert.Equal(10, frames[0].Contacts[0].X, precision: 6);
        Assert.Equal(11, frames[1].Contacts[0].X, precision: 6);
    }

    [Fact]
    public void LiftedAndPalmContactsAreLeftOut()
    {
        var frame = Assert.Single(Assembler().Consume(
            new HidTouchReport(ContactCount: 3, [Contact(1, 100, 100), Contact(2, 200, 100, tip: false), Contact(3, 300, 100, confident: false)], IsButtonDown: false),
            time: 1));
        Assert.Equal([1], frame.Contacts.Select(contact => contact.Id));
    }

    [Fact]
    public void ReportWithNoContactsIsAnEmptyFrame()
    {
        var frame = Assert.Single(Assembler().Consume(new HidTouchReport(ContactCount: 0, [], IsButtonDown: false), time: 1));
        Assert.Empty(frame.Contacts);
    }
}

namespace TrackpadTurn.Core;

// Touchpad input as the Windows helper reads it: Precision Touchpad HID reports (Raw Input,
// usage page 0x0D / usage 0x05), joined into frames of finger contacts in millimetres.
// Only the HidP_* extraction is Windows-only (Windows/src/TrackpadTurn.Windows/Input/PrecisionTouchpad.cs).

/// <summary>A finger on the touchpad, in millimetres from the top-left corner (x right, y down).</summary>
public readonly record struct TouchpadContact(int Id, double X, double Y);

/// <summary>Every finger touching the touchpad at one moment, plus its physical button.</summary>
/// <param name="Time">Seconds on the helper's monotonic clock.</param>
/// <param name="Contacts">Only fingers that touch (tip switch) and are not a palm (confidence).</param>
public sealed record TouchpadFrame(double Time, IReadOnlyList<TouchpadContact> Contacts, bool IsButtonDown);

/// <summary>One finger as a HID report states it, in the device's logical units.</summary>
public readonly record struct HidContact(int Id, bool TipDown, bool Confident, int X, int Y);

/// <param name="ContactCount">Fingers in this frame. In hybrid mode only the frame's first report carries it; the rest say 0.</param>
public sealed record HidTouchReport(int ContactCount, IReadOnlyList<HidContact> Contacts, bool IsButtonDown);

/// <summary>Maps one touchpad axis from logical units to millimetres.</summary>
public readonly record struct ContactAxis(int LogicalMin, int LogicalMax, double MinMillimetres, double MaxMillimetres)
{
    /// <summary>Used when a touchpad doesn't state its physical size (the spec requires it, some skip it).</summary>
    public const double FallbackSpanMillimetres = 100;

    public double ToMillimetres(int logical)
    {
        var logicalSpan = LogicalMax - LogicalMin;
        if (logicalSpan == 0)
        {
            return MinMillimetres;
        }
        return MinMillimetres + (double)(logical - LogicalMin) * (MaxMillimetres - MinMillimetres) / logicalSpan;
    }

    /// <summary>Builds the mapping from a HIDP_VALUE_CAPS entry.</summary>
    /// <param name="units">HID unit code: low nibble is the system (1 = SI linear → cm, 3 = English linear → inch).</param>
    /// <param name="unitExponent">HID unit exponent: a 4-bit two's-complement power of ten (0xE = -2).</param>
    public static ContactAxis FromHid(int logicalMin, int logicalMax, int physicalMin, int physicalMax, uint units, uint unitExponent)
    {
        var millimetresPerUnit = (units & 0xF) switch
        {
            1 => 10.0, // centimetre
            3 => 25.4, // inch
            _ => 0.0,
        };
        var lengthPower = (units >> 4) & 0xF;
        if (physicalMax <= physicalMin || millimetresPerUnit == 0 || lengthPower != 1)
        {
            return new ContactAxis(logicalMin, logicalMax, 0, FallbackSpanMillimetres);
        }

        var exponentNibble = (int)(unitExponent & 0xF);
        var exponent = exponentNibble >= 8 ? exponentNibble - 16 : exponentNibble;
        var scale = millimetresPerUnit * Math.Pow(10, exponent);
        return new ContactAxis(logicalMin, logicalMax, physicalMin * scale, physicalMax * scale);
    }
}

/// <summary>
/// Joins Precision Touchpad reports into whole frames.
/// </summary>
/// <remarks>
/// A touchpad sends a frame either in one report (parallel mode) or spread over several
/// (hybrid mode), where only the first report says how many contacts follow. Some touchpads
/// claim more contacts than they send, so an unfinished frame is sent when the next one starts.
/// </remarks>
public sealed class TouchFrameAssembler(ContactAxis xAxis, ContactAxis yAxis)
{
    private readonly List<HidContact> pendingContacts = [];
    private int expectedContacts;
    private bool pendingButton;

    public IReadOnlyList<TouchpadFrame> Consume(HidTouchReport report, double time)
    {
        var frames = new List<TouchpadFrame>();
        var startsFrame = report.ContactCount > 0 || expectedContacts == 0;
        if (startsFrame)
        {
            if (expectedContacts > 0)
            {
                frames.Add(MakeFrame(time));
            }
            expectedContacts = report.ContactCount;
            pendingContacts.Clear();
            pendingButton = false;
        }

        // A report has a slot for every finger the touchpad can track; only as many as the frame
        // still expects are real.
        var needed = Math.Max(0, expectedContacts - pendingContacts.Count);
        pendingContacts.AddRange(report.Contacts.Take(needed));
        pendingButton |= report.IsButtonDown;
        if (pendingContacts.Count >= expectedContacts)
        {
            frames.Add(MakeFrame(time));
            expectedContacts = 0;
        }
        return frames;
    }

    private TouchpadFrame MakeFrame(double time)
    {
        var contacts = pendingContacts
            .Where(contact => contact.TipDown && contact.Confident)
            .Select(contact => new TouchpadContact(contact.Id, xAxis.ToMillimetres(contact.X), yAxis.ToMillimetres(contact.Y)))
            .ToArray();
        return new TouchpadFrame(time, contacts, pendingButton);
    }
}

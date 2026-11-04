using System.Runtime.InteropServices;
using TrackpadTurn.Core;
using global::Windows.Win32;
using global::Windows.Win32.Devices.HumanInterfaceDevice;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Input;

namespace TrackpadTurn.Windows.Input;

/// <summary>
/// One Precision Touchpad, read through Raw Input. Turns its HID input reports into
/// <see cref="HidTouchReport"/>s and then frames of finger contacts in millimetres.
/// </summary>
/// <remarks>
/// Usages: Microsoft's "Windows Precision Touchpad required HID top-level collections".
/// Report level: Contact Count (0x0D/0x54) and the integrated button (0x09/0x01).
/// Per finger, one logical collection each: Contact ID (0x0D/0x51), Tip Switch (0x0D/0x42),
/// Confidence (0x0D/0x47), X (0x01/0x30), Y (0x01/0x31).
/// </remarks>
internal sealed unsafe class PrecisionTouchpad : IDisposable
{
    public const ushort DigitizerUsagePage = 0x0D;
    public const ushort TouchPadUsage = 0x05;

    private const ushort GenericDesktopPage = 0x01;
    private const ushort ButtonPage = 0x09;
    private const ushort UsageX = 0x30;
    private const ushort UsageY = 0x31;
    private const ushort UsageTipSwitch = 0x42;
    private const ushort UsageConfidence = 0x47;
    private const ushort UsageContactId = 0x51;
    private const ushort UsageContactCount = 0x54;

    private readonly void* preparsedData;
    private readonly PHIDP_PREPARSED_DATA preparsed;
    private readonly TouchFrameAssembler assembler;
    /// <summary>Link collections that hold one finger each, in report order.</summary>
    private readonly ushort[] fingerCollections;
    private readonly ushort? contactCountCollection;
    private readonly ushort? buttonCollection;
    private readonly bool hasConfidence;
    private readonly uint maxUsages;

    public HANDLE Device { get; }

    /// <summary>Size and fingers, for the log.</summary>
    public string Description { get; }

    private PrecisionTouchpad(HANDLE device, void* preparsedData, ushort[] fingerCollections,
        ushort? contactCountCollection, ushort? buttonCollection, bool hasConfidence, ContactAxis xAxis, ContactAxis yAxis)
    {
        Device = device;
        this.preparsedData = preparsedData;
        preparsed = new PHIDP_PREPARSED_DATA((nint)preparsedData);
        this.fingerCollections = fingerCollections;
        this.contactCountCollection = contactCountCollection;
        this.buttonCollection = buttonCollection;
        this.hasConfidence = hasConfidence;
        maxUsages = Math.Max(1, PInvoke.HidP_MaxUsageListLength(HIDP_REPORT_TYPE.HidP_Input, 0, preparsed));
        assembler = new TouchFrameAssembler(xAxis, yAxis);
        Description = $"{fingerCollections.Length} fingers, {xAxis.MaxMillimetres - xAxis.MinMillimetres:0}×{yAxis.MaxMillimetres - yAxis.MinMillimetres:0} mm"
            + (buttonCollection is null ? ", no button" : "");
    }

    /// <summary>Returns the touchpad behind a Raw Input device handle, or null for any other device.</summary>
    public static PrecisionTouchpad? Open(HANDLE device)
    {
        if (!IsTouchpad(device))
        {
            return null;
        }

        uint size = 0;
        PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, null, &size);
        if (size == 0)
        {
            return null;
        }
        var data = NativeMemory.Alloc(size);
        if (PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, data, &size) == unchecked((uint)-1))
        {
            NativeMemory.Free(data);
            return null;
        }

        var preparsed = new PHIDP_PREPARSED_DATA((nint)data);
        HIDP_CAPS caps;
        if (PInvoke.HidP_GetCaps(preparsed, &caps) != NTSTATUS.HIDP_STATUS_SUCCESS)
        {
            NativeMemory.Free(data);
            return null;
        }

        var valueCaps = new HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
        var valueCount = caps.NumberInputValueCaps;
        fixed (HIDP_VALUE_CAPS* values = valueCaps)
        {
            if (valueCount > 0 && PInvoke.HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, values, &valueCount, preparsed) != NTSTATUS.HIDP_STATUS_SUCCESS)
            {
                valueCount = 0;
            }
        }
        var buttonCaps = new HIDP_BUTTON_CAPS[caps.NumberInputButtonCaps];
        var buttonCount = caps.NumberInputButtonCaps;
        fixed (HIDP_BUTTON_CAPS* buttons = buttonCaps)
        {
            if (buttonCount > 0 && PInvoke.HidP_GetButtonCaps(HIDP_REPORT_TYPE.HidP_Input, buttons, &buttonCount, preparsed) != NTSTATUS.HIDP_STATUS_SUCCESS)
            {
                buttonCount = 0;
            }
        }

        // Fingers: link collections that have an X value.
        var fingers = new SortedSet<ushort>();
        ContactAxis? xAxis = null;
        ContactAxis? yAxis = null;
        ushort? contactCountCollection = null;
        foreach (var value in valueCaps.AsSpan(0, valueCount))
        {
            var usage = value.IsRange ? value.Anonymous.Range.UsageMin : value.Anonymous.NotRange.Usage;
            if (value.UsagePage == GenericDesktopPage && usage == UsageX)
            {
                fingers.Add(value.LinkCollection);
                xAxis ??= Axis(value);
            }
            else if (value.UsagePage == GenericDesktopPage && usage == UsageY)
            {
                yAxis ??= Axis(value);
            }
            else if (value.UsagePage == DigitizerUsagePage && usage == UsageContactCount)
            {
                contactCountCollection = value.LinkCollection;
            }
        }

        ushort? buttonCollection = null;
        var hasConfidence = false;
        foreach (var button in buttonCaps.AsSpan(0, buttonCount))
        {
            var usageMin = button.IsRange ? button.Anonymous.Range.UsageMin : button.Anonymous.NotRange.Usage;
            var usageMax = button.IsRange ? button.Anonymous.Range.UsageMax : button.Anonymous.NotRange.Usage;
            if (button.UsagePage == ButtonPage && usageMin <= 1 && 1 <= usageMax)
            {
                buttonCollection ??= button.LinkCollection;
            }
            if (button.UsagePage == DigitizerUsagePage && usageMin <= UsageConfidence && UsageConfidence <= usageMax)
            {
                hasConfidence = true;
            }
        }

        if (fingers.Count == 0 || xAxis is null || yAxis is null)
        {
            NativeMemory.Free(data);
            return null;
        }
        return new PrecisionTouchpad(device, data, [.. fingers], contactCountCollection, buttonCollection,
            hasConfidence, xAxis.Value, yAxis.Value);
    }

    public static bool IsTouchpad(HANDLE device)
    {
        var info = new RID_DEVICE_INFO { cbSize = (uint)sizeof(RID_DEVICE_INFO) };
        var size = info.cbSize;
        if (PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICEINFO, &info, &size) == unchecked((uint)-1))
        {
            return false;
        }
        return info.dwType == RID_DEVICE_INFO_TYPE.RIM_TYPEHID
            && info.Anonymous.hid.usUsagePage == DigitizerUsagePage
            && info.Anonymous.hid.usUsage == TouchPadUsage;
    }

    /// <summary>Precision Touchpads attached now, for the status line.</summary>
    public static int CountAttached()
    {
        uint count = 0;
        if (PInvoke.GetRawInputDeviceList(null, &count, (uint)sizeof(RAWINPUTDEVICELIST)) != 0 || count == 0)
        {
            return 0;
        }
        var devices = new RAWINPUTDEVICELIST[count];
        fixed (RAWINPUTDEVICELIST* list = devices)
        {
            var listed = PInvoke.GetRawInputDeviceList(list, &count, (uint)sizeof(RAWINPUTDEVICELIST));
            if (listed == unchecked((uint)-1))
            {
                return 0;
            }
            return devices.AsSpan(0, (int)listed).ToArray().Count(device =>
                device.dwType == RID_DEVICE_INFO_TYPE.RIM_TYPEHID && IsTouchpad(device.hDevice));
        }
    }

    /// <summary>Reads one HID input report. Returns the frames it completes (none, one, or two).</summary>
    public IReadOnlyList<TouchpadFrame> Read(byte* report, uint length, double time)
    {
        // The touchpad may send other reports too (another report ID, e.g. diagnostics). Reading
        // the touch usages from one fails; treating it as "no fingers" would end the swipe.
        int contactCount;
        if (contactCountCollection is { } countCollection)
        {
            if (!TryUsageValue(DigitizerUsagePage, countCollection, UsageContactCount, report, length, out var count))
            {
                return [];
            }
            contactCount = (int)count;
        }
        else if (TryUsageValue(GenericDesktopPage, fingerCollections[0], UsageX, report, length, out _))
        {
            contactCount = fingerCollections.Length;
        }
        else
        {
            return [];
        }

        var contacts = new HidContact[fingerCollections.Length];
        var usages = stackalloc ushort[(int)maxUsages];
        for (var index = 0; index < fingerCollections.Length; index++)
        {
            var collection = fingerCollections[index];
            var usageCount = maxUsages;
            var tipDown = false;
            var confident = !hasConfidence;
            if (PInvoke.HidP_GetUsages(HIDP_REPORT_TYPE.HidP_Input, DigitizerUsagePage, collection, usages, &usageCount,
                    preparsed, new PSTR(report), length) == NTSTATUS.HIDP_STATUS_SUCCESS)
            {
                for (var usage = 0; usage < usageCount; usage++)
                {
                    tipDown |= usages[usage] == UsageTipSwitch;
                    confident |= usages[usage] == UsageConfidence;
                }
            }
            contacts[index] = new HidContact(
                Id: (int)UsageValue(DigitizerUsagePage, collection, UsageContactId, report, length),
                TipDown: tipDown,
                Confident: confident,
                X: (int)UsageValue(GenericDesktopPage, collection, UsageX, report, length),
                Y: (int)UsageValue(GenericDesktopPage, collection, UsageY, report, length));
        }

        var buttonDown = false;
        if (buttonCollection is { } button)
        {
            var usageCount = maxUsages;
            if (PInvoke.HidP_GetUsages(HIDP_REPORT_TYPE.HidP_Input, ButtonPage, button, usages, &usageCount,
                    preparsed, new PSTR(report), length) == NTSTATUS.HIDP_STATUS_SUCCESS)
            {
                for (var usage = 0; usage < usageCount; usage++)
                {
                    buttonDown |= usages[usage] == 1;
                }
            }
        }

        return assembler.Consume(new HidTouchReport(contactCount, contacts, buttonDown), time);
    }

    private uint UsageValue(ushort usagePage, ushort collection, ushort usage, byte* report, uint length) =>
        TryUsageValue(usagePage, collection, usage, report, length, out var value) ? value : 0;

    private bool TryUsageValue(ushort usagePage, ushort collection, ushort usage, byte* report, uint length, out uint value)
    {
        uint read = 0;
        var status = PInvoke.HidP_GetUsageValue(HIDP_REPORT_TYPE.HidP_Input, usagePage, collection, usage, &read, preparsed, new PSTR(report), length);
        value = read;
        return status == NTSTATUS.HIDP_STATUS_SUCCESS;
    }

    private static ContactAxis Axis(in HIDP_VALUE_CAPS value) =>
        ContactAxis.FromHid(value.LogicalMin, value.LogicalMax, value.PhysicalMin, value.PhysicalMax, value.Units, value.UnitsExp);

    public void Dispose() => NativeMemory.Free(preparsedData);
}

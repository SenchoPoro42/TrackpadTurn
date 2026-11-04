using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TrackpadTurn.Core;
using TrackpadTurn.Windows.Diagnostics;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Accessibility;
using global::Windows.Win32.UI.Input;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace TrackpadTurn.Windows.Input;

/// <summary>What the settings window shows about input.</summary>
internal sealed record InputStatus(bool WowInFront, bool WowNeedsAdministrator, int TouchpadCount, int SwipeCount);

/// <summary>
/// The thread that sees touchpad and mouse input before WoW does and decides what WoW receives.
/// </summary>
/// <remarks>
/// It runs its own message loop with a hidden message-only window, which receives:
/// - Raw Input reports from every Precision Touchpad (RIDEV_INPUTSINK: also while WoW is in front),
/// - the WH_MOUSE_LL hook's calls (wheel and right button, before any app sees them),
/// - foreground-window changes (SetWinEventHook) and a timer for held presses.
/// All gesture state lives in <see cref="GestureRouter"/>, touched only on this thread. The UI
/// thread never runs here: a busy settings window must not delay the hook, or Windows removes it.
/// </remarks>
internal sealed unsafe class InputThread : IDisposable
{
    private const uint SettingsChangedMessage = PInvoke.WM_APP + 1;
    private const uint ShutdownMessage = PInvoke.WM_APP + 2;
    private const nuint TickTimerId = 1;
    private const uint TickMilliseconds = 15;
    /// <summary>A finger moving this long without one mouse event means Windows dropped the hook.</summary>
    private const double HookSilenceSeconds = 2.0;
    /// <summary>Travel that must move the pointer; sensor jitter alone doesn't.</summary>
    private const double HookCheckTravelMillimetres = 3.0;
    private const double HookReinstallCooldownSeconds = 10.0;

    // Hook and window callbacks are static (UnmanagedCallersOnly); they reach the one instance here.
    private static InputThread? current;

    private readonly Thread thread;
    private readonly SynchronizationContext? uiContext;
    private readonly Action<InputStatus> statusChanged;
    private readonly ManualResetEventSlim ready = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly InputInjector injector = new();
    private readonly GestureRouter router;
    /// <summary>Opened touchpads by Raw Input handle; null for devices that aren't touchpads.</summary>
    private readonly Dictionary<nint, PrecisionTouchpad?> touchpads = [];
    private byte[] rawInputBuffer = new byte[1024];
    private TurnSettings? pendingSettings;

    private HWND window;
    private HHOOK mouseHook;
    private UnhookWinEventSafeHandle? foregroundHook;
    private bool tickTimerRunning;

    private HWND wowWindow;
    private bool wowInFront;
    private bool wowNeedsAdministrator;
    private int touchpadCount;
    private InputStatus? lastStatus;

    /// <summary>The touchpad whose fingers are down; a second touchpad is ignored meanwhile.</summary>
    private nint activeTouchpad;

    // Hook health
    private double lastHookEventAt;
    private double fingerMovingSince = -1;
    private double fingerTravelMillimetres;
    private TouchpadContact? lastSingleFinger;
    private double lastHookReinstallAt = double.NegativeInfinity;

    /// <param name="uiContext">Where <paramref name="statusChanged"/> runs (the UI thread).</param>
    public InputThread(TurnSettings settings, SynchronizationContext? uiContext, Action<InputStatus> statusChanged)
    {
        router = new GestureRouter(settings);
        this.statusChanged = statusChanged;
        this.uiContext = uiContext;
        thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "TrackpadTurn input",
            // The hook must answer within LowLevelHooksTimeout even when the PC is busy.
            Priority = ThreadPriority.Highest,
        };
    }

    private double Now => clock.Elapsed.TotalSeconds;

    public void Start()
    {
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Log.Error("input thread did not start in time");
        }
    }

    /// <summary>Called on the UI thread; applied on the input thread.</summary>
    public void UpdateSettings(TurnSettings settings)
    {
        Volatile.Write(ref pendingSettings, settings);
        PInvoke.PostMessage(window, SettingsChangedMessage, default, default);
    }

    public void Dispose()
    {
        if (thread.IsAlive && PInvoke.PostMessage(window, ShutdownMessage, default, default))
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
        ready.Dispose();
    }

    // Thread body

    private void RunMessageLoop()
    {
        current = this;
        try
        {
            CreateMessageWindow();
            RegisterTouchpads();
            InstallMouseHook();
            foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
                default, &ForegroundChanged, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
            touchpadCount = PrecisionTouchpad.CountAttached();
            Log.Info($"input ready: {touchpadCount} Precision Touchpad(s)");
            OnForegroundChanged(PInvoke.GetForegroundWindow());
        }
        catch (Exception exception)
        {
            Log.Error("input setup failed", exception);
        }
        finally
        {
            ready.Set();
        }

        MSG message;
        while (PInvoke.GetMessage(&message, default, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }

        foreach (var touchpad in touchpads.Values)
        {
            touchpad?.Dispose();
        }
        current = null;
    }

    private void CreateMessageWindow()
    {
        fixed (char* className = "TrackpadTurnInput")
        {
            var module = PInvoke.GetModuleHandle((char*)null);
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = (HINSTANCE)module.Value,
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(&windowClass);
            window = PInvoke.CreateWindowEx(0, className, className, 0, 0, 0, 0, 0,
                HWND.HWND_MESSAGE, default, (HINSTANCE)module.Value, null);
        }
        if (window.IsNull)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
        }
    }

    private void RegisterTouchpads()
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = PrecisionTouchpad.DigitizerUsagePage,
            usUsage = PrecisionTouchpad.TouchPadUsage,
            // INPUTSINK: reports keep coming while WoW, not this app, is in front.
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK | RAWINPUTDEVICE_FLAGS.RIDEV_DEVNOTIFY,
            hwndTarget = window,
        };
        if (!PInvoke.RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
        {
            Log.Error($"RegisterRawInputDevices failed ({Marshal.GetLastPInvokeError()})");
        }
    }

    private void InstallMouseHook()
    {
        var module = PInvoke.GetModuleHandle((char*)null);
        mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, &MouseHookProc, (HINSTANCE)module.Value, 0);
        if (mouseHook.IsNull)
        {
            Log.Error($"SetWindowsHookEx failed ({Marshal.GetLastPInvokeError()})");
        }
        lastHookEventAt = Now;
    }

    // Window messages

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (current is { } self && self.HandleMessage(hwnd, message, wParam, lParam))
            {
                return default;
            }
        }
        catch (Exception exception)
        {
            Log.Error($"window message 0x{message:X}", exception);
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>Returns true when the message is fully handled.</summary>
    private bool HandleMessage(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_INPUT:
                HandleRawInput(lParam);
                return false; // DefWindowProc frees the input
            case PInvoke.WM_INPUT_DEVICE_CHANGE:
                HandleDeviceChange((uint)wParam.Value, lParam.Value);
                return true;
            case PInvoke.WM_TIMER when wParam.Value == TickTimerId:
                var cursor = WowWindow.Cursor();
                Run(router.OnTick(Now, Target(cursor.Position, cursor.IsHidden)), "tick");
                UpdateTickTimer();
                return true;
            case SettingsChangedMessage:
                if (Interlocked.Exchange(ref pendingSettings, null) is { } settings)
                {
                    Run(router.UpdateSettings(settings), "settings");
                }
                return true;
            case ShutdownMessage:
                Run(router.Abandon(), "quit");
                if (!mouseHook.IsNull)
                {
                    PInvoke.UnhookWindowsHookEx(mouseHook);
                }
                foregroundHook?.Dispose();
                PInvoke.DestroyWindow(hwnd);
                PInvoke.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    // Touchpad (Raw Input)

    private void HandleRawInput(LPARAM lParam)
    {
        var headerSize = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        var handle = new HRAWINPUT(lParam.Value);
        if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, headerSize) != 0 || size == 0)
        {
            return;
        }
        if (rawInputBuffer.Length < size)
        {
            rawInputBuffer = new byte[size];
        }

        fixed (byte* data = rawInputBuffer)
        {
            if (PInvoke.GetRawInputData(handle, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, data, &size, headerSize) == uint.MaxValue)
            {
                return;
            }
            var raw = (RAWINPUT*)data;
            if (raw->header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEHID || TouchpadFor(raw->header.hDevice) is not { } touchpad)
            {
                return;
            }

            // Reports can arrive batched; each one is dwSizeHid bytes.
            var hid = &raw->data.hid;
            var reports = (byte*)&hid->bRawData;
            for (var index = 0u; index < hid->dwCount; index++)
            {
                foreach (var frame in touchpad.Read(reports + index * hid->dwSizeHid, hid->dwSizeHid, Now))
                {
                    HandleFrame(touchpad.Device, frame);
                }
            }
        }
    }

    private PrecisionTouchpad? TouchpadFor(HANDLE device)
    {
        if (!touchpads.TryGetValue((nint)device.Value, out var touchpad))
        {
            touchpad = PrecisionTouchpad.Open(device);
            touchpads[(nint)device.Value] = touchpad;
            if (touchpad is not null)
            {
                Log.Info($"touchpad opened: {touchpad.Description}");
            }
        }
        return touchpad;
    }

    private void HandleFrame(HANDLE device, TouchpadFrame frame)
    {
        if (activeTouchpad != 0 && activeTouchpad != (nint)device.Value && router.HasContacts)
        {
            return;
        }
        activeTouchpad = frame.Contacts.Count > 0 ? (nint)device.Value : 0;
        WatchHookHealth(frame);

        var cursor = WowWindow.Cursor();
        var result = router.OnTouchpadFrame(frame, Target(cursor.Position, cursor.IsHidden));
        if (result.Commands.Count > 0)
        {
            Log.Debug(() => $"fingers {frame.Contacts.Count}{(frame.IsButtonDown ? " +button" : "")} → {result}");
        }
        Run(result, null);
        UpdateTickTimer();
    }

    private void HandleDeviceChange(uint change, nint device)
    {
        if (change == PInvoke.GIDC_REMOVAL && touchpads.Remove(device, out var removed))
        {
            removed?.Dispose();
            if (activeTouchpad == device)
            {
                activeTouchpad = 0;
                Run(router.Abandon(), "touchpad removed");
            }
        }
        touchpadCount = PrecisionTouchpad.CountAttached();
        PublishStatus();
    }

    // Mouse hook (WH_MOUSE_LL)

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT MouseHookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && current is { } self)
        {
            try
            {
                if (self.HandleMouse((uint)wParam.Value, (MSLLHOOKSTRUCT*)lParam.Value))
                {
                    return new LRESULT(1); // swallowed: no app sees it
                }
            }
            catch (Exception exception)
            {
                Log.Error("mouse hook", exception);
            }
        }
        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }

    /// <summary>Returns true to drop the event. Must stay fast: the whole system's mouse waits on it.</summary>
    private bool HandleMouse(uint message, MSLLHOOKSTRUCT* info)
    {
        lastHookEventAt = Now;
        if (info->dwExtraInfo == InputInjector.Marker)
        {
            return false;
        }

        RouterResult result;
        switch (message)
        {
            case PInvoke.WM_MOUSEWHEEL:
            case PInvoke.WM_MOUSEHWHEEL:
                result = router.OnWheel(MadeAt(info->time), Target(info->pt, WowWindow.Cursor().IsHidden));
                Log.Debug(() => $"wheel {(short)(info->mouseData >> 16)}{(message == PInvoke.WM_MOUSEHWHEEL ? " horizontal" : "")} → {result.Disposition}");
                break;
            case PInvoke.WM_RBUTTONDOWN:
            case PInvoke.WM_RBUTTONUP:
                var isDown = message == PInvoke.WM_RBUTTONDOWN;
                result = router.OnRightButton(isDown, Now, Target(info->pt, WowWindow.Cursor().IsHidden));
                Log.Debug(() => $"right button {(isDown ? "down" : "up")}, fingers {(router.HasContacts ? "down" : "up")} → {result}");
                UpdateTickTimer();
                break;
            default:
                return false;
        }
        Run(result, null);
        return result.Disposition == HookDisposition.Drop;
    }

    /// <summary>When a hooked event was made, on <see cref="Now"/>'s clock. The hook can run late.</summary>
    private double MadeAt(uint eventTickCount)
    {
        // Both are GetTickCount milliseconds; unsigned subtraction survives the wrap-around.
        var ageMilliseconds = unchecked((uint)Environment.TickCount - eventTickCount);
        return ageMilliseconds > 10_000 ? Now : Now - ageMilliseconds / 1000.0;
    }

    /// <summary>
    /// Windows silently removes a low-level hook that once answered too slowly. A finger moving
    /// the pointer without a single mouse event reaching the hook means that happened.
    /// </summary>
    private void WatchHookHealth(TouchpadFrame frame)
    {
        var now = Now;
        var finger = frame.Contacts.Count == 1 ? frame.Contacts[0] : (TouchpadContact?)null;
        var step = finger is { } contact && lastSingleFinger is { } previous && contact.Id == previous.Id
            ? Math.Sqrt(Math.Pow(contact.X - previous.X, 2) + Math.Pow(contact.Y - previous.Y, 2))
            : (double?)null;
        lastSingleFinger = finger;
        if (step is not { } travelled)
        {
            fingerMovingSince = -1;
            fingerTravelMillimetres = 0;
            return;
        }
        if (fingerMovingSince < 0)
        {
            fingerMovingSince = now;
        }
        fingerTravelMillimetres += travelled;
        if (fingerTravelMillimetres > HookCheckTravelMillimetres && now - fingerMovingSince > HookSilenceSeconds
            && now - lastHookEventAt > HookSilenceSeconds && now - lastHookReinstallAt > HookReinstallCooldownSeconds)
        {
            Log.Info("mouse hook went silent; installing it again");
            lastHookReinstallAt = now;
            if (!mouseHook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(mouseHook);
            }
            InstallMouseHook();
            // Wheel or button-up events may have been missed: don't leave WoW turning or running.
            if (router.IsHoldingKeys)
            {
                Run(router.Abandon(), "hook reinstalled");
            }
        }
    }

    // WoW focus

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void ForegroundChanged(HWINEVENTHOOK hook, uint winEvent, HWND foreground, int objectId, int childId, uint eventThread, uint eventTime)
    {
        try
        {
            current?.OnForegroundChanged(foreground);
        }
        catch (Exception exception)
        {
            Log.Error("foreground change", exception);
        }
    }

    private void OnForegroundChanged(HWND foreground)
    {
        var isWow = WowWindow.IsWow(foreground);
        if (wowInFront && (!isWow || foreground != wowWindow))
        {
            wowInFront = false;
            // Its release events will go elsewhere now: end gestures so WoW isn't left turning or running.
            Run(router.Abandon(), "WoW lost focus");
        }
        if (isWow)
        {
            wowWindow = foreground;
            wowInFront = true;
            wowNeedsAdministrator = WowWindow.IsElevatedAboveUs(foreground);
            Log.Info(wowNeedsAdministrator
                ? "WoW is in front but runs as administrator: Windows blocks TrackpadTurn's input to it"
                : "WoW is in front");
        }
        PublishStatus();
    }

    private TargetState Target(System.Drawing.Point cursor, bool cursorHidden)
    {
        // An administrator WoW gets none of our input (UIPI), so dropping its wheel or right-button
        // events would only take them away.
        var wowIsForeground = wowInFront && !wowNeedsAdministrator && PInvoke.GetForegroundWindow() == wowWindow;
        var cursorOverWow = wowIsForeground && WowWindow.RootWindowAt(cursor) == wowWindow;
        return new TargetState(wowIsForeground, cursorOverWow, cursorHidden);
    }

    // Output

    private void Run(RouterResult result, string? reason)
    {
        if (result.Commands.Count == 0)
        {
            return;
        }
        if (reason is not null)
        {
            Log.Debug(() => $"{reason} → {result}");
        }
        injector.Run(result.Commands, wowWindow);
        PublishStatus();
    }

    private void UpdateTickTimer()
    {
        if (router.NeedsTicks && !tickTimerRunning)
        {
            tickTimerRunning = PInvoke.SetTimer(window, TickTimerId, TickMilliseconds, null) != 0;
        }
        else if (!router.NeedsTicks && tickTimerRunning)
        {
            PInvoke.KillTimer(window, TickTimerId);
            tickTimerRunning = false;
        }
    }

    private void PublishStatus()
    {
        var status = new InputStatus(wowInFront, wowNeedsAdministrator, touchpadCount, router.SwipeCount);
        if (status == lastStatus)
        {
            return;
        }
        lastStatus = status;
        if (uiContext is null)
        {
            statusChanged(status);
        }
        else
        {
            uiContext.Post(_ => statusChanged(status), null);
        }
    }
}

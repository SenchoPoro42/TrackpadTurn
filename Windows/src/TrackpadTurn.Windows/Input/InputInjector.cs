using System.Runtime.InteropServices;
using TrackpadTurn.Core;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TrackpadTurn.Windows.Input;

/// <summary>
/// Injects what WoW receives: signal keys for the addon, mouse movement for mouselook, and the
/// held-back right-button release. Every event carries <see cref="Marker"/> so the app's own
/// mouse hook lets it straight through.
/// </summary>
internal sealed unsafe class InputInjector
{
    /// <summary>"TPTU" in dwExtraInfo, the Windows twin of the Mac helper's eventSourceUserData.</summary>
    public const nuint Marker = 0x5450_5455;

    /// <summary>Where each held key went down, so its release reaches WoW even after it lost focus.</summary>
    private readonly Dictionary<SignalKey, HWND> heldKeyTargets = [];

    public void Run(IReadOnlyList<OutputCommand> commands, HWND wowWindow)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case OutputCommand.KeyDown(var key):
                    SendKey(key, isDown: true);
                    heldKeyTargets[key] = wowWindow;
                    break;
                case OutputCommand.KeyUp(var key):
                    ReleaseKey(key, wowWindow);
                    break;
                case OutputCommand.KeyTap(var key):
                    SendKey(key, isDown: true);
                    SendKey(key, isDown: false);
                    break;
                case OutputCommand.Look(var dx, var dy):
                    SendMouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE, dx, dy);
                    break;
                case OutputCommand.ReleaseRightButton:
                    SendMouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, 0, 0);
                    break;
            }
        }
    }

    private void ReleaseKey(SignalKey key, HWND wowWindow)
    {
        if (!heldKeyTargets.Remove(key, out var target))
        {
            target = wowWindow;
        }
        // Checked now, not from the last focus event: that one can arrive late.
        if (!target.IsNull && PInvoke.GetForegroundWindow() != target && PInvoke.IsWindow(target))
        {
            // WoW is no longer in front, so SendInput hands the release to another app. Post it
            // straight to WoW's window too, the way the Mac helper posts to WoW's process.
            var signal = WowSignalProtocol.KeyFor(key);
            PInvoke.PostMessage(target, PInvoke.WM_KEYUP, new WPARAM(signal.VirtualKey), new LPARAM(KeyUpLParam(signal)));
        }
        // Always through SendInput as well: only that clears the key in Windows' own key state,
        // or the next press would look like a held key repeating.
        SendKey(key, isDown: false);
    }

    // Keyboard message lParam: repeat count 1, scan code, extended flag, previous state and
    // transition bits set (a release).
    private static nint KeyUpLParam(WindowsSignalKey signal) =>
        (nint)(1u | ((uint)signal.ScanCode << 16) | (signal.IsExtended ? 1u << 24 : 0) | (1u << 30) | (1u << 31));

    private static void SendKey(SignalKey key, bool isDown)
    {
        var signal = WowSignalProtocol.KeyFor(key);
        var flags = (KEYBD_EVENT_FLAGS)0;
        if (signal.IsExtended)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }
        if (!isDown)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }
        // Both the virtual key and the scan code, so WoW sees the same message a keyboard makes,
        // however it reads keys. The current modifiers apply as they really are, so Shift+swipe
        // arrives as SHIFT-PAUSE, which the addon binds too.
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)signal.VirtualKey,
                    wScan = signal.ScanCode,
                    dwFlags = flags,
                    dwExtraInfo = Marker,
                },
            },
        };
        Send(input);
    }

    /// <summary>
    /// Relative movement. Windows' pointer speed and "Enhance pointer precision" apply to it
    /// unless WoW reads raw mouse input (rawMouseEnable 1).
    /// </summary>
    private static void SendMouse(MOUSE_EVENT_FLAGS flags, int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = flags,
                    dwExtraInfo = Marker,
                },
            },
        };
        Send(input);
    }

    private static void Send(INPUT input)
    {
        if (PInvoke.SendInput(1, &input, sizeof(INPUT)) != 1)
        {
            // Fails when WoW runs as administrator and this app doesn't (UIPI), with no other sign.
            Diagnostics.Log.Debug(() => $"SendInput failed (error {Marshal.GetLastPInvokeError()})");
        }
    }
}

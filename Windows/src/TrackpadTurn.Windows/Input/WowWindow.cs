using System.Runtime.InteropServices;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Security;
using global::Windows.Win32.System.Threading;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace TrackpadTurn.Windows.Input;

/// <summary>Finds World of Warcraft's window and checks where the cursor is.</summary>
internal static unsafe class WowWindow
{
    /// <summary>Every WoW client's main window has this class; its title differs per flavor and language.</summary>
    private const string WindowClass = "GxWindowClass";

    /// <summary>True when the window is a WoW game window (any flavor).</summary>
    public static bool IsWow(HWND window)
    {
        if (window.IsNull || ClassName(window) != WindowClass)
        {
            return false;
        }
        // Wow.exe, WowClassic.exe, WowT.exe, WowB.exe, WowClassicT.exe, … and their -arm64 builds.
        var image = ProcessImageName(window);
        return image is not null && Path.GetFileName(image).StartsWith("Wow", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The top-level window under a screen point.</summary>
    public static HWND RootWindowAt(System.Drawing.Point point) =>
        PInvoke.GetAncestor(PInvoke.WindowFromPoint(point), GET_ANCESTOR_FLAGS.GA_ROOT);

    /// <summary>The cursor position and whether it is hidden (WoW hides it while mouselooking).</summary>
    public static (System.Drawing.Point Position, bool IsHidden) Cursor()
    {
        var info = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        if (!PInvoke.GetCursorInfo(&info))
        {
            return (default, false);
        }
        // Hidden either way a game can do it: ShowCursor(FALSE) clears CURSOR_SHOWING, SetCursor(NULL)
        // leaves no cursor shape.
        var isHidden = (info.flags & CURSORINFO_FLAGS.CURSOR_SHOWING) == 0 || info.hCursor.IsNull;
        return (info.ptScreenPos, isHidden);
    }

    /// <summary>
    /// WoW runs as administrator while this app doesn't. Windows then blocks our input to it
    /// (UIPI) without any error, so the app warns instead.
    /// </summary>
    public static bool IsElevatedAboveUs(HWND window)
    {
        uint processId;
        if (PInvoke.GetWindowThreadProcessId(window, &processId) == 0)
        {
            return false;
        }
        return IsElevated(processId) == true && IsElevated((uint)Environment.ProcessId) == false;
    }

    private static bool? IsElevated(uint processId)
    {
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            // An elevated process refuses even limited queries from a non-elevated one on some systems.
            return processId == Environment.ProcessId ? null : true;
        }
        try
        {
            HANDLE token;
            if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
            {
                return null;
            }
            try
            {
                TOKEN_ELEVATION elevation;
                uint returned;
                return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(TOKEN_ELEVATION), &returned)
                    ? elevation.TokenIsElevated != 0
                    : null;
            }
            finally
            {
                PInvoke.CloseHandle(token);
            }
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    private static string? ClassName(HWND window)
    {
        var buffer = stackalloc char[64];
        var length = PInvoke.GetClassName(window, buffer, 64);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string? ProcessImageName(HWND window)
    {
        uint processId;
        if (PInvoke.GetWindowThreadProcessId(window, &processId) == 0)
        {
            return null;
        }
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return null;
        }
        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024u;
            return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, &length)
                ? new string(buffer, 0, (int)length)
                : null;
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}

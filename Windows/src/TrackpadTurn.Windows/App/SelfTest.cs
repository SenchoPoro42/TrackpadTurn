using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TrackpadTurn.Core;
using TrackpadTurn.Windows.Input;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace TrackpadTurn.Windows.App;

/// <summary>
/// `TrackpadTurn.exe --self-test &lt;result file&gt;`: checks the Win32 plumbing on a real Windows
/// machine (CI), where unit tests on macOS can't reach. Writes one line per check.
/// </summary>
/// <remarks>
/// It proves that the low-level hooks see this app's own injected keys and mouse moves with
/// <see cref="InputInjector.Marker"/>, which is how the app tells its output from real input.
/// It needs no touchpad and sends only a 1-pixel move and the Pause key.
/// </remarks>
internal static unsafe class SelfTest
{
    public const string Argument = "--self-test";

    private static readonly ManualResetEventSlim SawMarkedMouse = new();
    private static readonly ManualResetEventSlim SawMarkedKey = new();

    public static int Run(string resultFile)
    {
        var lines = new List<string>();
        var failures = 0;
        void Check(string name, bool passed, string detail)
        {
            lines.Add($"{(passed ? "PASS" : "FAIL")} {name}: {detail}");
            failures += passed ? 0 : 1;
        }

        // Addon embedded in the exe, with the version from its TOC.
        var installer = new AddonInstaller();
        var files = AddonInstaller.BundledFiles().Select(file => file.RelativePath).ToArray();
        Check("embedded addon", files.Contains("TrackpadTurn.toc") && files.Contains("TrackpadTurn.lua") && files.Contains("Bindings.xml"),
            string.Join(", ", files));
        Check("addon version", installer.BundledVersion == AppIcon.Version, $"addon {installer.BundledVersion}, app {AppIcon.Version}");

        // Install and update into a throwaway WoW folder: the staging + rename swap on real NTFS.
        var fakeRoot = Path.Combine(Path.GetTempPath(), $"TrackpadTurnSelfTest-{Environment.ProcessId}", "World of Warcraft");
        try
        {
            Directory.CreateDirectory(Path.Combine(fakeRoot, "_classic_era_"));
            installer.Refresh(fakeRoot);
            var flavor = installer.Flavors.Single(found => found.Folder.StartsWith(fakeRoot, StringComparison.OrdinalIgnoreCase));
            var first = installer.Install(flavor);
            var second = installer.Install(installer.Flavors.Single(found => found.Folder == flavor.Folder));
            var leftovers = Directory.GetDirectories(flavor.AddOnsFolder).Select(Path.GetFileName).ToArray();
            var installed = installer.Flavors.Single(found => found.Folder == flavor.Folder).InstalledVersion;
            Check("addon install and update", first == InstallOutcome.Installed && second == InstallOutcome.Installed
                && installed == installer.BundledVersion && leftovers is ["TrackpadTurn"],
                $"{first}, {second}, version {installed}, folders: {string.Join(", ", leftovers)}");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fakeRoot)!, recursive: true);
        }

        // Settings and Windows settings readable.
        var settings = new TurnSettings { TurnSpeed = 2 };
        Check("settings json", TurnSettings.FromJson(settings.ToJson()) == settings, "round trip");
        lines.Add($"INFO precision touchpads: {PrecisionTouchpad.CountAttached()}");
        lines.Add($"INFO ignores touchpad while typing: {TouchpadSettings.IgnoresTouchpadWhileTyping()?.ToString() ?? "unknown"}");
        lines.Add($"INFO wow roots: {string.Join("; ", AddonInstaller.StandardWowRoots())}");

        // Hooks see our own injected input, marked.
        uint hookThreadId = 0;
        var hooksReady = new ManualResetEventSlim();
        var hookThread = new Thread(() =>
        {
            hookThreadId = PInvoke.GetCurrentThreadId();
            var module = (HINSTANCE)PInvoke.GetModuleHandle((char*)null).Value;
            var mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, &MouseHook, module, 0);
            var keyboardHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &KeyboardHook, module, 0);
            lines.Add($"INFO hooks installed: mouse {!mouseHook.IsNull}, keyboard {!keyboardHook.IsNull}");
            hooksReady.Set();
            MSG message;
            while (PInvoke.GetMessage(&message, default, 0, 0) > 0)
            {
                PInvoke.DispatchMessage(&message);
            }
            PInvoke.UnhookWindowsHookEx(mouseHook);
            PInvoke.UnhookWindowsHookEx(keyboardHook);
        });
        hookThread.Start();
        hooksReady.Wait(TimeSpan.FromSeconds(5));

        var injector = new InputInjector();
        injector.Run([new OutputCommand.Look(1, 0), new OutputCommand.Look(-1, 0), new OutputCommand.KeyTap(SignalKey.Swipe)], default);
        var lastError = Marshal.GetLastPInvokeError();
        Check("hook sees marked mouse move", SawMarkedMouse.Wait(TimeSpan.FromSeconds(3)), $"last error {lastError}");
        Check("hook sees marked signal key", SawMarkedKey.Wait(TimeSpan.FromSeconds(3)), $"PAUSE, VK 0x{WowSignalProtocol.KeyFor(SignalKey.Swipe).VirtualKey:X2}");

        PInvoke.PostThreadMessage(hookThreadId, PInvoke.WM_QUIT, default, default);
        hookThread.Join(TimeSpan.FromSeconds(2));

        lines.Add(failures == 0 ? "OK" : $"{failures} check(s) failed");
        File.WriteAllLines(resultFile, lines);
        return failures == 0 ? 0 : 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT MouseHook(int code, WPARAM wParam, LPARAM lParam)
    {
        var info = (MSLLHOOKSTRUCT*)lParam.Value;
        if (code >= 0 && info->dwExtraInfo == InputInjector.Marker && (info->flags & PInvoke.LLMHF_INJECTED) != 0)
        {
            SawMarkedMouse.Set();
        }
        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT KeyboardHook(int code, WPARAM wParam, LPARAM lParam)
    {
        var info = (KBDLLHOOKSTRUCT*)lParam.Value;
        var signal = WowSignalProtocol.KeyFor(SignalKey.Swipe);
        if (code >= 0 && info->dwExtraInfo == InputInjector.Marker && info->vkCode == signal.VirtualKey && info->scanCode == signal.ScanCode
            && (info->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0)
        {
            SawMarkedKey.Set();
        }
        return PInvoke.CallNextHookEx(default, code, wParam, lParam);
    }
}

namespace TrackpadTurn.Core;

/// <summary>
/// The contract between this helper and the TrackpadTurn WoW addon.
/// </summary>
/// <remarks>
/// An addon cannot read touchpad gestures, and the helper cannot see WoW's UI. So the helper
/// reports gestures as key presses on keys nobody uses, and the addon (which knows whether the
/// cursor is over the 3D world or over a frame) decides what they mean.
///
/// WoW on Windows ignores F13–F24, so the Windows keys differ from the Mac's F16–F19. They are
/// keys that type no text (a swipe with chat open must not write into it), toggle no lock state
/// and trigger no Windows shortcut.
///
/// BREADCRUMB: the addon binds these exact keys in `Addon/TrackpadTurn/TrackpadTurn.lua`
/// (`SIGNAL_KEY_SETS.windows`); the Mac helper's keys are in
/// `Sources/TrackpadTurnCore/WowSignalProtocol.swift`. Change all sides together and bump
/// <see cref="Version"/>. `WowSignalProtocolTests` checks the addon still matches.
/// </remarks>
public static class WowSignalProtocol
{
    public const int Version = 3;

    public static WindowsSignalKey KeyFor(SignalKey key) => key switch
    {
        // The scan codes and extended flags are what a real keyboard sends. They matter: without
        // the extended flag, Windows and WoW read Insert/Home/End as numpad 0/7/1.
        SignalKey.Swipe => new("PAUSE", VirtualKey: 0x13, ScanCode: 0x45, IsExtended: false),
        SignalKey.ZoomIn => new("HOME", VirtualKey: 0x24, ScanCode: 0x47, IsExtended: true),
        SignalKey.ZoomOut => new("END", VirtualKey: 0x23, ScanCode: 0x4F, IsExtended: true),
        SignalKey.Forward => new("INSERT", VirtualKey: 0x2D, ScanCode: 0x52, IsExtended: true),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}

public enum SignalKey
{
    /// <summary>Held down for the whole two-finger swipe. Down = swipe began, up = fingers lifted.</summary>
    Swipe,
    /// <summary>Tapped once per pinch-out step.</summary>
    ZoomIn,
    /// <summary>Tapped once per pinch-in step.</summary>
    ZoomOut,
    /// <summary>
    /// Held while a two-finger press is held. The addon binds it to run-while-held or to toggle
    /// autorun (WoW: Settings → AddOns → TrackpadTurn).
    /// </summary>
    Forward,
}

/// <param name="WowKeyName">The key name WoW uses in bindings.</param>
/// <param name="VirtualKey">Win32 virtual-key code (VK_PAUSE, VK_HOME, VK_END, VK_INSERT).</param>
/// <param name="ScanCode">Set-1 scan code, as a real keyboard reports it.</param>
/// <param name="IsExtended">The key sends the E0 prefix (the navigation block, not the numpad).</param>
public readonly record struct WindowsSignalKey(string WowKeyName, ushort VirtualKey, ushort ScanCode, bool IsExtended);

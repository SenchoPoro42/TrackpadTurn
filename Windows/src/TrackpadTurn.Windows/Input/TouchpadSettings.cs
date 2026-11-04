using global::Windows.Win32;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace TrackpadTurn.Windows.Input;

/// <summary>Windows' own touchpad settings (Settings → Bluetooth &amp; devices → Touchpad).</summary>
internal static unsafe class TouchpadSettings
{
    /// <summary>
    /// Windows ignores the touchpad for a moment after each key press unless "Touchpad
    /// sensitivity" is "Most sensitive". In WoW a movement key is often held, so a swipe or a
    /// two-finger click made while running can be lost. Null when Windows can't say (before
    /// Windows 11, or no touchpad).
    /// </summary>
    public static bool? IgnoresTouchpadWhileTyping()
    {
        var parameters = new TOUCHPAD_PARAMETERS_V1 { versionNumber = PInvoke.TOUCHPAD_PARAMETERS_VERSION_1 };
        if (!PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETTOUCHPADPARAMETERS,
                (uint)sizeof(TOUCHPAD_PARAMETERS_V1), &parameters, 0))
        {
            return null;
        }
        if (!parameters.touchpadPresent)
        {
            return null;
        }
        return parameters.sensitivityLevel != TOUCHPAD_SENSITIVITY_LEVEL.TOUCHPAD_SENSITIVITY_LEVEL_MOST_SENSITIVE;
    }
}

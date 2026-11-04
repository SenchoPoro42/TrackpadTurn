using System.Reflection;

namespace TrackpadTurn.Windows.App;

/// <summary>The app icon, embedded from Resources/TrackpadTurn.ico (made by scripts/make-icon.swift).</summary>
internal static class AppIcon
{
    public static Icon Load(Size size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrackpadTurn.ico")
            ?? throw new InvalidOperationException("TrackpadTurn.ico is not embedded");
        return new Icon(stream, size);
    }

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "?";
}

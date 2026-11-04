using System.Text.RegularExpressions;
using TrackpadTurn.Core;

namespace TrackpadTurn.Core.Tests;

/// <summary>The helper and the addon must agree on the signal keys (see WowSignalProtocol).</summary>
public partial class WowSignalProtocolTests
{
    [Fact]
    public void AddonBindsTheWindowsSignalKeys()
    {
        var addon = File.ReadAllText(Path.Combine(RepositoryRoot(), "Addon", "TrackpadTurn", "TrackpadTurn.lua"));
        var windowsSet = WindowsKeySet().Match(addon);
        Assert.True(windowsSet.Success, "SIGNAL_KEY_SETS.windows not found in TrackpadTurn.lua");

        var addonKeys = KeyAssignment().Matches(windowsSet.Groups["body"].Value)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["key"].Value);
        Assert.Equal(WowSignalProtocol.KeyFor(SignalKey.Swipe).WowKeyName, addonKeys["swipe"]);
        Assert.Equal(WowSignalProtocol.KeyFor(SignalKey.ZoomIn).WowKeyName, addonKeys["zoomIn"]);
        Assert.Equal(WowSignalProtocol.KeyFor(SignalKey.ZoomOut).WowKeyName, addonKeys["zoomOut"]);
        Assert.Equal(WowSignalProtocol.KeyFor(SignalKey.Forward).WowKeyName, addonKeys["hold"]);
    }

    [Fact]
    public void AddonSpeaksTheSameProtocolVersion()
    {
        var addon = File.ReadAllText(Path.Combine(RepositoryRoot(), "Addon", "TrackpadTurn", "TrackpadTurn.lua"));
        var version = ProtocolVersion().Match(addon);
        Assert.Equal(WowSignalProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), version.Groups["version"].Value);
    }

    [Fact]
    public void SignalKeysAreDistinct()
    {
        var keys = Enum.GetValues<SignalKey>().Select(key => WowSignalProtocol.KeyFor(key).VirtualKey).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Addon", "TrackpadTurn")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("repository root with Addon/TrackpadTurn");
    }

    [GeneratedRegex(@"windows\s*=\s*\{(?<body>[^}]*)\}")]
    private static partial Regex WindowsKeySet();

    [GeneratedRegex(@"(?<name>\w+)\s*=\s*""(?<key>[^""]+)""")]
    private static partial Regex KeyAssignment();

    [GeneratedRegex(@"local PROTOCOL_VERSION = (?<version>\d+)")]
    private static partial Regex ProtocolVersion();
}

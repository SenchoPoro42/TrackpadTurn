using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackpadTurn.Core;

/// <summary>
/// User tuning for how gestures map to camera motion. Persisted by the app as JSON.
/// Mirrors Sources/TrackpadTurnCore/TurnSettings.swift, including its JSON key names.
/// </summary>
public sealed record TurnSettings
{
    public bool IsEnabled { get; init; } = true;
    /// <summary>Mouse-look pixels per point of finger travel.</summary>
    public double TurnSpeed { get; init; } = 1.0;
    /// <summary>Up/down swipes tilt the camera. When off, only left/right turns.</summary>
    public bool TiltEnabled { get; init; } = true;
    public bool ReverseHorizontal { get; init; }
    public bool ReverseVertical { get; init; }
    public bool PinchZoomEnabled { get; init; } = true;
    /// <summary>Multiplies how many zoom steps one pinch produces.</summary>
    public double ZoomSpeed { get; init; } = 1.0;
    /// <summary>Holding a two-finger press runs forward, like holding both mouse buttons.</summary>
    public bool PressToRunEnabled { get; init; } = true;

    /// <summary>Roblox damps pitch against yaw by this ratio; it keeps a mostly-sideways swipe level.</summary>
    public const double TiltDamping = 0.77;
    public static readonly SpeedRange TurnSpeedRange = new(0.25, 4.0);
    public static readonly SpeedRange ZoomSpeedRange = new(0.25, 4.0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Fields missing from older saved settings keep their defaults, so new fields can be added
    /// without losing what the user saved. Out-of-range speeds are clamped.
    /// </summary>
    public static TurnSettings FromJson(string json) =>
        (JsonSerializer.Deserialize<TurnSettings>(json, JsonOptions) ?? new TurnSettings()).Normalized();

    /// <summary>Speeds clamped into their ranges (a hand-edited settings file can hold anything).</summary>
    public TurnSettings Normalized() => this with
    {
        TurnSpeed = TurnSpeedRange.Clamp(TurnSpeed),
        ZoomSpeed = ZoomSpeedRange.Clamp(ZoomSpeed),
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public readonly record struct SpeedRange(double Min, double Max)
{
    public double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, Min, Max) : 1.0;
}

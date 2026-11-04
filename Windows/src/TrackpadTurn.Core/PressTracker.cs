namespace TrackpadTurn.Core;

/// <summary>What the app must send to WoW for a held two-finger touchpad press.</summary>
public enum PressAction
{
    /// <summary>Press the forward signal key: WoW runs forward while it is mouselooking.</summary>
    RunBegan,
    /// <summary>Release the forward signal key.</summary>
    RunEnded,
}

/// <summary>
/// Turns a held two-finger touchpad press into "run forward", like holding both mouse buttons.
/// Ported one-to-one from Sources/TrackpadTurnCore/PressTracker.swift.
/// </summary>
/// <remarks>
/// A two-finger press reaches WoW as a right-click, which on its own starts mouselook. Holding it
/// a moment longer adds forward movement (or toggles autorun, as chosen in the addon's settings).
/// A quick two-finger click stays a plain right-click (interact, loot) because running only
/// starts after <see cref="HoldBeforeRunning"/>.
/// </remarks>
public sealed class PressTracker
{
    /// <summary>Seconds a press must be held before running starts; a deliberate click is shorter.</summary>
    public const double HoldBeforeRunning = 0.2;

    /// <summary>When the current press, if still held, starts running.</summary>
    private double? runsAt;

    public bool IsRunning { get; private set; }

    /// <summary>A press is down (waiting or running), so its release must be seen.</summary>
    public bool IsTracking => runsAt is not null || IsRunning;

    /// <summary>Returns the time at which to call <see cref="CheckHold"/>, or null when nothing should happen.</summary>
    public double? PressBegan(double time, TurnSettings settings)
    {
        if (!settings.IsEnabled || !settings.PressToRunEnabled)
        {
            return null;
        }
        var deadline = time + HoldBeforeRunning;
        runsAt = deadline;
        return deadline;
    }

    public IReadOnlyList<PressAction> CheckHold(double time)
    {
        // Compare against the stored deadline, not a recomputed hold time: the timer fires at
        // exactly the deadline, and subtracting it back can land a hair short in floating point.
        if (runsAt is not { } deadline || IsRunning || time < deadline)
        {
            return [];
        }
        IsRunning = true;
        return [PressAction.RunBegan];
    }

    /// <summary>The press was released, or must be treated as released (WoW lost focus, events missed).</summary>
    public IReadOnlyList<PressAction> PressEnded()
    {
        runsAt = null;
        if (!IsRunning)
        {
            return [];
        }
        IsRunning = false;
        return [PressAction.RunEnded];
    }
}

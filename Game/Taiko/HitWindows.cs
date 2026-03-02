namespace TaikoNova.Game.Taiko;

/// <summary>
/// Calculates hit timing windows based on Overall Difficulty (OD).
/// Matches osu!taiko windows.
/// </summary>
public class HitWindows
{
    /// <summary>Great (300) window in ms (±).</summary>
    public double Great { get; }

    /// <summary>Good (100) window in ms (±).</summary>
    public double Good { get; }

    /// <summary>Miss window in ms (±). Notes beyond this are auto-missed.</summary>
    public double Miss { get; }

    public HitWindows(double overallDifficulty)
    {
        // osu!taiko hit windows (half-window, ±ms):
        // Great: 50 - 3 * OD
        // Good:  120 - 8 * OD
        // Miss:  135 - 8 * OD
        Great = 50.0 - 3.0 * overallDifficulty;
        Good = 120.0 - 8.0 * overallDifficulty;
        Miss = 135.0 - 8.0 * overallDifficulty;
    }

    /// <summary>
    /// Evaluate the hit result for a given timing offset (absolute ms).
    /// </summary>
    public HitResult Evaluate(double absOffset)
    {
        if (absOffset <= Great) return HitResult.Great;
        if (absOffset <= Good) return HitResult.Good;
        return HitResult.None; // Outside window — not a valid hit
    }

    /// <summary>
    /// Check if a note at the given time has been missed (current time past miss window).
    /// </summary>
    public bool IsMissed(double noteTime, double currentTime)
    {
        return currentTime - noteTime > Miss;
    }
}

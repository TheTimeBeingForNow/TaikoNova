namespace TaikoNova.Game.Taiko;

/// <summary>
/// Types of taiko hit objects.
/// </summary>
public enum HitObjectType
{
    Don,        // Center hit (red)
    Kat,        // Rim hit (blue)
    BigDon,     // Big center hit
    BigKat,     // Big rim hit
    Drumroll,   // Slider — rapid hits
    BigDrumroll,// Big drumroll
    Denden      // Spinner — alternate don/kat
}

/// <summary>
/// Judgment result for a hit.
/// </summary>
public enum HitResult
{
    None,
    Great,  // 300
    Good,   // 100
    Miss    // 0
}

/// <summary>
/// Represents a single taiko hit object during gameplay.
/// </summary>
public class HitObject
{
    /// <summary>Time in ms when this note should be hit.</summary>
    public double Time { get; set; }

    /// <summary>End time for drumrolls and dendens.</summary>
    public double EndTime { get; set; }

    /// <summary>Type of hit object.</summary>
    public HitObjectType Type { get; set; }

    /// <summary>Whether this note has been judged already.</summary>
    public bool IsHit { get; set; }

    /// <summary>The judgment result.</summary>
    public HitResult Result { get; set; } = HitResult.None;

    /// <summary>Time the hit actually occurred (for rendering feedback).</summary>
    public double HitTime { get; set; }

    /// <summary>Number of ticks hit (for drumrolls/dendens).</summary>
    public int TicksHit { get; set; }

    /// <summary>Total ticks required (for drumrolls/dendens).</summary>
    public int TicksRequired { get; set; }

    /// <summary>Scroll speed multiplier from inherited timing point.</summary>
    public double ScrollMultiplier { get; set; } = 1.0;

    /// <summary>Whether this note is during kiai time.</summary>
    public bool IsKiai { get; set; }

    // ── Convenience ──

    public bool IsDon => Type == HitObjectType.Don || Type == HitObjectType.BigDon;
    public bool IsKat => Type == HitObjectType.Kat || Type == HitObjectType.BigKat;
    public bool IsBig => Type == HitObjectType.BigDon || Type == HitObjectType.BigKat || Type == HitObjectType.BigDrumroll;
    public bool IsNote => Type <= HitObjectType.BigKat;
    public bool IsDrumroll => Type == HitObjectType.Drumroll || Type == HitObjectType.BigDrumroll;
    public bool IsDenden => Type == HitObjectType.Denden;
    public bool IsLong => IsDrumroll || IsDenden;
}

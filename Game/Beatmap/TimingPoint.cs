namespace TaikoNova.Game.Beatmap;

/// <summary>
/// Represents a timing point from an osu! beatmap.
/// </summary>
public class TimingPoint
{
    /// <summary>Time in milliseconds.</summary>
    public double Time { get; set; }

    /// <summary>
    /// For uninherited points: milliseconds per beat (60000 / BPM).
    /// For inherited points: negative slider velocity multiplier (-100 = 1x).
    /// </summary>
    public double BeatLength { get; set; }

    /// <summary>Beats per measure (time signature numerator).</summary>
    public int TimeSignature { get; set; } = 4;

    /// <summary>Sample set (0=auto, 1=normal, 2=soft, 3=drum).</summary>
    public int SampleSet { get; set; }

    /// <summary>Sample index.</summary>
    public int SampleIndex { get; set; }

    /// <summary>Volume (0-100).</summary>
    public int Volume { get; set; } = 100;

    /// <summary>True if this is an uninherited (red) timing point.</summary>
    public bool Uninherited { get; set; } = true;

    /// <summary>Kiai time flag.</summary>
    public bool Kiai { get; set; }

    /// <summary>BPM for uninherited points.</summary>
    public double BPM => Uninherited && BeatLength > 0 ? 60000.0 / BeatLength : 0;

    /// <summary>
    /// Effective slider velocity multiplier for inherited points.
    /// Returns 1.0 for uninherited points.
    /// </summary>
    public double SliderVelocityMultiplier =>
        Uninherited ? 1.0 : Math.Clamp(-100.0 / BeatLength, 0.1, 10.0);
}

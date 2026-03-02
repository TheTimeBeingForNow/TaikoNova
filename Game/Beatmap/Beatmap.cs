using TaikoNova.Game.Taiko;

namespace TaikoNova.Game.Beatmap;

/// <summary>
/// Contains all data parsed from an osu! beatmap file.
/// </summary>
public class BeatmapData
{
    // ── General ──
    public string AudioFilename { get; set; } = "";
    public int AudioLeadIn { get; set; }
    public int PreviewTime { get; set; }
    public int Mode { get; set; } // 0=std, 1=taiko, 2=ctb, 3=mania

    // ── Metadata ──
    public string Title { get; set; } = "Unknown";
    public string TitleUnicode { get; set; } = "";
    public string Artist { get; set; } = "Unknown";
    public string ArtistUnicode { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Version { get; set; } = ""; // Difficulty name
    public string Source { get; set; } = "";
    public int BeatmapID { get; set; }
    public int BeatmapSetID { get; set; }

    // ── Difficulty ──
    public float HPDrainRate { get; set; } = 5f;
    public float OverallDifficulty { get; set; } = 5f;
    public float SliderMultiplier { get; set; } = 1.4f;
    public float SliderTickRate { get; set; } = 1f;

    // ── Timing ──
    public List<TimingPoint> TimingPoints { get; set; } = new();

    // ── Hit Objects ──
    public List<HitObject> HitObjects { get; set; } = new();

    // ── File info ──
    public string FilePath { get; set; } = "";
    public string FolderPath { get; set; } = "";

    // ── Background / Video (from [Events] section) ──
    public string BackgroundFilename { get; set; } = "";
    public string VideoFilename { get; set; } = "";
    public int VideoOffset { get; set; } // ms offset for video start

    // ── Computed ──
    public string DisplayTitle => string.IsNullOrEmpty(TitleUnicode) ? Title : TitleUnicode;
    public string DisplayArtist => string.IsNullOrEmpty(ArtistUnicode) ? Artist : ArtistUnicode;

    /// <summary>
    /// Get the active timing point (uninherited) at the given time.
    /// </summary>
    public TimingPoint GetTimingPointAt(double time)
    {
        TimingPoint? result = null;
        foreach (var tp in TimingPoints)
        {
            if (tp.Uninherited && tp.Time <= time)
                result = tp;
        }
        return result ?? TimingPoints.FirstOrDefault(t => t.Uninherited)
               ?? new TimingPoint { BeatLength = 500, Uninherited = true };
    }

    /// <summary>
    /// Get the effective slider velocity multiplier at the given time.
    /// </summary>
    public double GetSliderVelocityAt(double time)
    {
        double multiplier = 1.0;
        foreach (var tp in TimingPoints)
        {
            if (tp.Time > time) break;
            if (!tp.Uninherited)
                multiplier = tp.SliderVelocityMultiplier;
        }
        return multiplier;
    }

    /// <summary>Is kiai active at the given time?</summary>
    public bool IsKiaiAt(double time)
    {
        bool kiai = false;
        foreach (var tp in TimingPoints)
        {
            if (tp.Time > time) break;
            kiai = tp.Kiai;
        }
        return kiai;
    }
}

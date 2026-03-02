using System.IO.Compression;
using TaikoNova.Game.Taiko;

namespace TaikoNova.Game.Beatmap;

/// <summary>
/// Decodes osu! file format (.osu) into BeatmapData.
/// Supports osu file format v3–v14+.
/// Handles both native taiko maps (mode=1) and standard→taiko conversion.
/// </summary>
public static class BeatmapDecoder
{
    public static BeatmapData Decode(string filePath)
    {
        var map = new BeatmapData
        {
            FilePath = filePath,
            FolderPath = Path.GetDirectoryName(filePath) ?? ""
        };

        string[] lines = File.ReadAllLines(filePath);
        string section = "";

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            // Section header
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1];
                continue;
            }

            switch (section)
            {
                case "General":
                    ParseGeneral(map, line);
                    break;
                case "Metadata":
                    ParseMetadata(map, line);
                    break;
                case "Difficulty":
                    ParseDifficulty(map, line);
                    break;
                case "Events":
                    ParseEvent(map, line);
                    break;
                case "TimingPoints":
                    ParseTimingPoint(map, line);
                    break;
                case "HitObjects":
                    ParseHitObject(map, line);
                    break;
            }
        }

        // Sort timing points by time
        map.TimingPoints.Sort((a, b) => a.Time.CompareTo(b.Time));

        // Apply scroll multipliers and kiai to hit objects
        foreach (var ho in map.HitObjects)
        {
            ho.ScrollMultiplier = map.GetSliderVelocityAt(ho.Time);
            ho.IsKiai = map.IsKiaiAt(ho.Time);
        }

        // Sort hit objects by time
        map.HitObjects.Sort((a, b) => a.Time.CompareTo(b.Time));

        return map;
    }

    private static void ParseGeneral(BeatmapData map, string line)
    {
        var (key, value) = SplitKV(line);
        switch (key)
        {
            case "AudioFilename": map.AudioFilename = value; break;
            case "AudioLeadIn": map.AudioLeadIn = ParseInt(value); break;
            case "PreviewTime": map.PreviewTime = ParseInt(value); break;
            case "Mode": map.Mode = ParseInt(value); break;
        }
    }

    private static void ParseEvent(BeatmapData map, string line)
    {
        // [Events] format:
        //   Background: 0,0,"bg.jpg",0,0
        //   Video:      Video,offset,"video.mp4"
        //   or:         1,offset,"video.mp4"
        string[] parts = line.Split(',');
        if (parts.Length < 3) return;

        string type = parts[0].Trim();
        string filename = parts[2].Trim().Trim('"');

        if (type == "0" && string.IsNullOrEmpty(map.BackgroundFilename))
        {
            // Background image
            map.BackgroundFilename = filename;
        }
        else if (type.Equals("Video", StringComparison.OrdinalIgnoreCase) || type == "1")
        {
            // Video background
            map.VideoFilename = filename;
            map.VideoOffset = ParseInt(parts[1].Trim());
        }
    }

    private static void ParseMetadata(BeatmapData map, string line)
    {
        var (key, value) = SplitKV(line);
        switch (key)
        {
            case "Title": map.Title = value; break;
            case "TitleUnicode": map.TitleUnicode = value; break;
            case "Artist": map.Artist = value; break;
            case "ArtistUnicode": map.ArtistUnicode = value; break;
            case "Creator": map.Creator = value; break;
            case "Version": map.Version = value; break;
            case "Source": map.Source = value; break;
            case "BeatmapID": map.BeatmapID = ParseInt(value); break;
            case "BeatmapSetID": map.BeatmapSetID = ParseInt(value); break;
        }
    }

    private static void ParseDifficulty(BeatmapData map, string line)
    {
        var (key, value) = SplitKV(line);
        switch (key)
        {
            case "HPDrainRate": map.HPDrainRate = ParseFloat(value); break;
            case "OverallDifficulty": map.OverallDifficulty = ParseFloat(value); break;
            case "SliderMultiplier": map.SliderMultiplier = ParseFloat(value); break;
            case "SliderTickRate": map.SliderTickRate = ParseFloat(value); break;
        }
    }

    private static void ParseTimingPoint(BeatmapData map, string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length < 2) return;

        var tp = new TimingPoint
        {
            Time = ParseDouble(parts[0]),
            BeatLength = ParseDouble(parts[1]),
        };

        if (parts.Length > 2) tp.TimeSignature = ParseInt(parts[2], 4);
        if (parts.Length > 3) tp.SampleSet = ParseInt(parts[3]);
        if (parts.Length > 4) tp.SampleIndex = ParseInt(parts[4]);
        if (parts.Length > 5) tp.Volume = ParseInt(parts[5], 100);
        if (parts.Length > 6) tp.Uninherited = parts[6].Trim() == "1";
        if (parts.Length > 7)
        {
            int effects = ParseInt(parts[7]);
            tp.Kiai = (effects & 1) != 0;
        }

        // Fix: if BeatLength > 0 and not explicitly marked, treat as uninherited
        if (parts.Length <= 6 && tp.BeatLength > 0)
            tp.Uninherited = true;

        map.TimingPoints.Add(tp);
    }

    private static void ParseHitObject(BeatmapData map, string line)
    {
        string[] parts = line.Split(',');
        if (parts.Length < 4) return;

        double time = ParseDouble(parts[2]);
        int type = ParseInt(parts[3]);
        int hitsound = parts.Length > 4 ? ParseInt(parts[4]) : 0;

        // osu! type flags
        bool isCircle = (type & 1) != 0;
        bool isSlider = (type & 2) != 0;
        bool isSpinner = (type & 8) != 0;

        // Hitsound flags
        bool isWhistle = (hitsound & 2) != 0;
        bool isFinish = (hitsound & 4) != 0;
        bool isClap = (hitsound & 8) != 0;
        bool isRim = isWhistle || isClap; // Kat sounds

        if (isCircle)
        {
            var ho = new HitObject { Time = time };

            if (isRim)
                ho.Type = isFinish ? HitObjectType.BigKat : HitObjectType.Kat;
            else
                ho.Type = isFinish ? HitObjectType.BigDon : HitObjectType.Don;

            map.HitObjects.Add(ho);
        }
        else if (isSlider)
        {
            // Drumroll — calculate end time from slider parameters
            double endTime = time;

            if (parts.Length > 7)
            {
                // parts[5] = curve type and points, parts[6] = slides, parts[7] = length
                int slides = ParseInt(parts[6], 1);
                double length = ParseDouble(parts[7]);

                // Calculate duration: length * slides / (sliderMultiplier * 100 * sv)
                var timingPoint = map.GetTimingPointAt(time);
                double svMult = map.GetSliderVelocityAt(time);
                double beatLen = timingPoint.BeatLength;
                if (beatLen > 0)
                {
                    double velocity = map.SliderMultiplier * 100.0 * svMult;
                    endTime = time + (length * slides / velocity) * beatLen;
                }
            }
            else
            {
                endTime = time + 500; // fallback
            }

            var ho = new HitObject
            {
                Time = time,
                EndTime = endTime,
                Type = isFinish ? HitObjectType.BigDrumroll : HitObjectType.Drumroll
            };

            // Calculate ticks for drumrolls
            var tp = map.GetTimingPointAt(time);
            double tickInterval = tp.BeatLength / map.SliderTickRate;
            if (tickInterval > 0)
                ho.TicksRequired = Math.Max(1, (int)((endTime - time) / tickInterval));
            else
                ho.TicksRequired = 4;

            map.HitObjects.Add(ho);
        }
        else if (isSpinner)
        {
            // Denden (spinner)
            double endTime = parts.Length > 5 ? ParseDouble(parts[5]) : time + 2000;

            var ho = new HitObject
            {
                Time = time,
                EndTime = endTime,
                Type = HitObjectType.Denden
            };

            // Calculate required hits based on duration
            double duration = endTime - time;
            ho.TicksRequired = Math.Max(1, (int)(duration / 150)); // ~6.67 hits per second

            map.HitObjects.Add(ho);
        }
    }

    // ── Helpers ──

    private static (string key, string value) SplitKV(string line)
    {
        int idx = line.IndexOf(':');
        if (idx < 0) return (line, "");
        return (line[..idx].Trim(), line[(idx + 1)..].Trim());
    }

    private static int ParseInt(string s, int fallback = 0) =>
        int.TryParse(s.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static float ParseFloat(string s, float fallback = 0f) =>
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

    private static double ParseDouble(string s, double fallback = 0.0) =>
        double.TryParse(s.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

    // ═══════════════════════════════════════════════════════════════
    // .osz support — .osz files are ZIP archives containing .osu files
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract a .osz file into a subfolder inside the Songs directory.
    /// Returns the path of the extracted folder, or null on failure.
    /// </summary>
    public static string? ExtractOsz(string oszPath, string songsDirectory)
    {
        try
        {
            string folderName = Path.GetFileNameWithoutExtension(oszPath);
            string destDir = Path.Combine(songsDirectory, folderName);

            // Skip if already extracted
            if (Directory.Exists(destDir) &&
                Directory.GetFiles(destDir, "*.osu").Length > 0)
            {
                return destDir;
            }

            Directory.CreateDirectory(destDir);
            ZipFile.ExtractToDirectory(oszPath, destDir, overwriteFiles: true);

            Console.WriteLine($"[Decoder] Extracted .osz: {Path.GetFileName(oszPath)} -> {folderName}/");
            return destDir;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Decoder] Failed to extract .osz '{oszPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extract all .osz files found in a directory into subfolders.
    /// </summary>
    public static int ExtractAllOsz(string directory)
    {
        if (!Directory.Exists(directory)) return 0;

        int count = 0;
        foreach (string oszFile in Directory.GetFiles(directory, "*.osz"))
        {
            if (ExtractOsz(oszFile, directory) != null)
                count++;
        }
        return count;
    }

    // ═══════════════════════════════════════════════════════
    // Beatmap discovery — find all .osu files in a directory
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Scan a Songs directory for all .osu files and return lightweight info.
    /// Automatically extracts any .osz archives found in the directory first.
    /// </summary>
    public static List<BeatmapInfo> ScanSongsDirectory(string songsPath)
    {
        var results = new List<BeatmapInfo>();

        if (!Directory.Exists(songsPath))
            return results;

        // Auto-extract any .osz files first
        int extracted = ExtractAllOsz(songsPath);
        if (extracted > 0)
            Console.WriteLine($"[Decoder] Auto-extracted {extracted} .osz archive(s)");

        // Scan subfolders for .osu files
        foreach (string dir in Directory.GetDirectories(songsPath))
        {
            foreach (string osuFile in Directory.GetFiles(dir, "*.osu"))
            {
                try
                {
                    var info = QuickParse(osuFile);
                    if (info != null)
                        results.Add(info);
                }
                catch { /* skip invalid files */ }
            }
        }

        return results;
    }

    /// <summary>
    /// Quick-parse only metadata from a .osu file (no hit objects).
    /// </summary>
    private static BeatmapInfo? QuickParse(string filePath)
    {
        var info = new BeatmapInfo
        {
            FilePath = filePath,
            FolderPath = Path.GetDirectoryName(filePath) ?? ""
        };
        string section = "";

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1];
                if (section == "HitObjects") break; // Stop early
                continue;
            }

            if (section == "General")
            {
                var (k, v) = SplitKV(line);
                if (k == "Mode") info.Mode = ParseInt(v);
                else if (k == "AudioFilename") info.AudioFilename = v;
                else if (k == "PreviewTime") info.PreviewTime = ParseInt(v);
            }
            else if (section == "Metadata")
            {
                var (k, v) = SplitKV(line);
                switch (k)
                {
                    case "Title": info.Title = v; break;
                    case "Artist": info.Artist = v; break;
                    case "Version": info.Version = v; break;
                    case "Creator": info.Creator = v; break;
                }
            }
            else if (section == "Difficulty")
            {
                var (k, v) = SplitKV(line);
                if (k == "OverallDifficulty") info.OD = ParseFloat(v);
            }
            else if (section == "Events")
            {
                // Parse background image: 0,0,"filename.jpg",0,0
                if (line.StartsWith("0,0,\""))
                {
                    int start = line.IndexOf('"') + 1;
                    int end = line.IndexOf('"', start);
                    if (end > start)
                        info.BackgroundFilename = line[start..end];
                }
            }
        }

        return info;
    }
}

/// <summary>
/// Lightweight beatmap info for song select listing.
/// </summary>
public class BeatmapInfo
{
    public string FilePath { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string BackgroundFilename { get; set; } = "";
    public string AudioFilename { get; set; } = "";
    public int PreviewTime { get; set; } = -1;
    public string Title { get; set; } = "Unknown";
    public string Artist { get; set; } = "Unknown";
    public string Version { get; set; } = "";
    public string Creator { get; set; } = "";
    public int Mode { get; set; }
    public float OD { get; set; }

    public string Display => $"{Artist} - {Title} [{Version}]";
}

using TaikoNova.Game.Taiko;

namespace TaikoNova.Game.Beatmap;

/// <summary>
/// Generates test beatmaps for playing without .osu files.
/// </summary>
public static class TestBeatmapGenerator
{
    /// <summary>
    /// Generate a practice beatmap with common patterns at the given BPM.
    /// </summary>
    public static BeatmapData Generate(double bpm = 160, double durationSeconds = 60)
    {
        var map = new BeatmapData
        {
            Title = "Practice Mode",
            Artist = "TaikoNova",
            Creator = "Auto",
            Version = $"{bpm} BPM",
            Mode = 1,
            HPDrainRate = 4,
            OverallDifficulty = 5,
            SliderMultiplier = 1.4f,
        };

        double beatLen = 60000.0 / bpm;

        map.TimingPoints.Add(new TimingPoint
        {
            Time = 0,
            BeatLength = beatLen,
            TimeSignature = 4,
            Uninherited = true,
            Volume = 100
        });

        double time = beatLen * 4; // 4 beats lead-in
        double endTime = durationSeconds * 1000;

        int pattern = 0;

        while (time < endTime)
        {
            switch (pattern % 8)
            {
                case 0: // Simple dons
                    for (int i = 0; i < 4; i++)
                    {
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                        time += beatLen;
                    }
                    break;

                case 1: // Simple kats
                    for (int i = 0; i < 4; i++)
                    {
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Kat });
                        time += beatLen;
                    }
                    break;

                case 2: // Don-kat alternating
                    for (int i = 0; i < 4; i++)
                    {
                        map.HitObjects.Add(new HitObject
                        {
                            Time = time,
                            Type = i % 2 == 0 ? HitObjectType.Don : HitObjectType.Kat
                        });
                        time += beatLen;
                    }
                    break;

                case 3: // Big notes
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.BigDon });
                    time += beatLen;
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.BigKat });
                    time += beatLen;
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.BigDon });
                    time += beatLen;
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.BigKat });
                    time += beatLen;
                    break;

                case 4: // Fast don stream (1/2 beat)
                    for (int i = 0; i < 8; i++)
                    {
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                        time += beatLen / 2;
                    }
                    break;

                case 5: // Don-Don-Kat-Kat pattern
                    for (int rep = 0; rep < 2; rep++)
                    {
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                        time += beatLen / 2;
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                        time += beatLen / 2;
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Kat });
                        time += beatLen / 2;
                        map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Kat });
                        time += beatLen / 2;
                    }
                    break;

                case 6: // Drumroll
                    map.HitObjects.Add(new HitObject
                    {
                        Time = time,
                        EndTime = time + beatLen * 2,
                        Type = HitObjectType.Drumroll,
                        TicksRequired = 8
                    });
                    time += beatLen * 3;
                    // Follow with a don
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                    time += beatLen;
                    break;

                case 7: // Mixed complexity
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                    time += beatLen / 2;
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Kat });
                    time += beatLen / 2;
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.Don });
                    time += beatLen;
                    map.HitObjects.Add(new HitObject { Time = time, Type = HitObjectType.BigDon });
                    time += beatLen * 2;
                    break;
            }

            pattern++;
        }

        // Apply scroll multiplier and kiai
        foreach (var ho in map.HitObjects)
        {
            ho.ScrollMultiplier = 1.0;
            ho.IsKiai = false;
        }

        // Add kiai section in the middle
        double kiaiStart = endTime * 0.4;
        double kiaiEnd = endTime * 0.6;
        map.TimingPoints.Add(new TimingPoint
        {
            Time = kiaiStart,
            BeatLength = -100,
            Uninherited = false,
            Kiai = true
        });
        map.TimingPoints.Add(new TimingPoint
        {
            Time = kiaiEnd,
            BeatLength = -100,
            Uninherited = false,
            Kiai = false
        });

        foreach (var ho in map.HitObjects)
        {
            if (ho.Time >= kiaiStart && ho.Time <= kiaiEnd)
                ho.IsKiai = true;
        }

        return map;
    }
}

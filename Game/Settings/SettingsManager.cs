using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaikoNova.Game.Settings;

/// <summary>
/// Persistent user settings with JSON save/load.
/// All gameplay, audio, display, and input settings live here.
/// </summary>
public sealed class SettingsManager
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaikoNova");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    // ═══════════════════════════════════════════════════════════════════
    //  Audio
    // ═══════════════════════════════════════════════════════════════════
    public float MasterVolume { get; set; } = 0.80f;
    public float MusicVolume { get; set; } = 0.70f;
    public float SfxVolume { get; set; } = 0.60f;

    // ═══════════════════════════════════════════════════════════════════
    //  Gameplay
    // ═══════════════════════════════════════════════════════════════════
    public float ScrollSpeed { get; set; } = 1.0f;         // multiplier: 0.5–2.0
    public float BackgroundDim { get; set; } = 0.65f;      // 0.0–1.0
    public int GlobalOffset { get; set; } = 0;             // ms: -200 to +200
    public bool ShowHitError { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════════
    //  Display
    // ═══════════════════════════════════════════════════════════════════
    public bool Fullscreen { get; set; } = false;
    public bool ShowFps { get; set; } = false;
    public int FrameLimit { get; set; } = 0;               // 0 = unlimited, else 60/120/144/240
    public bool VSync { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════════
    //  Input
    // ═══════════════════════════════════════════════════════════════════
    public string DonKeys { get; set; } = "D,F";
    public string KatKeys { get; set; } = "J,K";

    // ═══════════════════════════════════════════════════════════════════
    //  Persistence
    // ═══════════════════════════════════════════════════════════════════

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(this, _jsonOpts);
            File.WriteAllText(ConfigPath, json);
            Console.WriteLine($"[Settings] Saved to {ConfigPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Save failed: {ex.Message}");
        }
    }

    public static SettingsManager Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<SettingsManager>(json, _jsonOpts);
                if (loaded != null)
                {
                    Console.WriteLine($"[Settings] Loaded from {ConfigPath}");
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] Load failed, using defaults: {ex.Message}");
        }

        Console.WriteLine("[Settings] Using defaults (no config file found)");
        return new SettingsManager();
    }
}

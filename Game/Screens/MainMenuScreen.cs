using OpenTK.Windowing.GraphicsLibraryFramework;
using TaikoNova.Engine;
using TaikoNova.Engine.GL;
using TaikoNova.Game.Beatmap;
using TaikoNova.Game.Skin;

namespace TaikoNova.Game.Screens;

/// <summary>
/// Main menu with arrow-key navigable options, background music + image
/// from the beatmap library, per-item accent colours, and a "now playing"
/// indicator in the top-right corner.
/// </summary>
public class MainMenuScreen : Screen
{
    // ── Menu items ──
    private readonly struct MenuItem
    {
        public string Label { get; init; }
        public string Description { get; init; }
        public Action OnSelect { get; init; }
        public float[] Accent { get; init; } // RGB
    }

    private MenuItem[] _items = Array.Empty<MenuItem>();
    private int _selected;

    // ── Animation ──
    private double _time;
    private float _enterAnim;      // 0→1 screen fade-in
    private float _selectBounce;   // brief bounce on select change
    private float _confirmAnim;    // 0→1 exit animation
    private bool _confirming;

    // ── Layout ──
    private const float TopBarH    = 48f;
    private const float BottomBarH = 44f;
    private const float ItemH      = 74f;
    private const float ItemGap    = 10f;

    // ── Background music / image ──
    private List<BeatmapInfo> _beatmaps = new();
    private bool _scanned;
    private BackgroundManager _background;
    private BeatmapInfo? _nowPlaying;
    private float _musicVolume;       // current volume lerp 0→1
    private float _musicTarget;      // target volume (1 = fade in, 0 = fade out)
    private bool _musicStarted;
    private bool _changingTrack;     // true while fading out before switching
    private float _bgFade;           // background crossfade 0→1
    private float _npPulse;           // subtle pulse for now-playing icon
    private double _trackEndDelay;   // brief silence between tracks

    // ── Per-item accent colours ──
    private static readonly float[] AccentPlay     = { 0.30f, 0.55f, 0.95f }; // blue
    private static readonly float[] AccentPractice = { 0.90f, 0.72f, 0.20f }; // gold
    private static readonly float[] AccentSettings = { 0.45f, 0.80f, 0.50f }; // green
    private static readonly float[] AccentExit     = { 0.86f, 0.24f, 0.18f }; // red

    public MainMenuScreen(GameEngine engine, TaikoGame game) : base(engine, game)
    {
        _background = new BackgroundManager(engine);
        _background.DimLevel = 0.65f;

        _items = new MenuItem[]
        {
            new() { Label = "Play",     Description = "Select a song and start playing",      Accent = AccentPlay,     OnSelect = () => game.GoToSongSelectFromMenu() },
            new() { Label = "Practice", Description = "Warm up with auto-generated patterns", Accent = AccentPractice, OnSelect = () => game.StartPracticeFromMenu() },
            new() { Label = "Settings", Description = "Adjust audio, display, and gameplay",  Accent = AccentSettings, OnSelect = () => game.OpenSettings() },
            new() { Label = "Exit",     Description = "Close the game",                       Accent = AccentExit,     OnSelect = () => engine.Close() },
        };
    }

    public override void OnEnter()
    {
        _time = 0;
        _enterAnim = 0;
        _selectBounce = 0;
        _confirmAnim = 0;
        _confirming = false;
        _musicVolume = 0f;
        _musicTarget = 1f;
        _musicStarted = false;
        _changingTrack = false;
        _bgFade = 0f;
        _trackEndDelay = 0;

        if (!_scanned)
        {
            ScanBeatmaps();
            _scanned = true;
        }

        PickRandomBackground();
    }

    public override void OnExit()
    {
        // Fade out / stop music when leaving menu
        Engine.Audio.StopMusic();
        _background.Unload();
        _musicStarted = false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Beatmap scanning (lightweight copy of SongSelect logic)
    // ═══════════════════════════════════════════════════════════════════

    private void ScanBeatmaps()
    {
        _beatmaps.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] localPaths =
        {
            Path.Combine(Environment.CurrentDirectory, "Songs"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Songs"),
        };

        foreach (string rawPath in localPaths)
        {
            string path;
            try { path = Path.GetFullPath(rawPath); }
            catch { continue; }
            if (!seen.Add(path)) continue;
            if (Directory.Exists(path))
                AddFromDirectory(path, seen);
        }

        foreach (string stableSongs in OsuInstallDetector.FindStableSongsPaths())
        {
            string full;
            try { full = Path.GetFullPath(stableSongs); }
            catch { continue; }
            if (!seen.Add(full)) continue;
            AddFromDirectory(full, seen);
        }

        var lazerFiles = OsuInstallDetector.FindLazerOsuFiles();
        foreach (string osuFile in lazerFiles)
        {
            if (seen.Contains(osuFile)) continue;
            try
            {
                var map = BeatmapDecoder.Decode(osuFile);
                _beatmaps.Add(new BeatmapInfo
                {
                    FilePath = osuFile,
                    FolderPath = map.FolderPath,
                    BackgroundFilename = map.BackgroundFilename,
                    AudioFilename = map.AudioFilename,
                    PreviewTime = map.PreviewTime,
                    Title = map.Title,
                    Artist = map.Artist,
                    Version = map.Version,
                    Creator = map.Creator,
                    OD = map.OverallDifficulty
                });
            }
            catch { }
        }

        Console.WriteLine($"[MainMenu] Scanned {_beatmaps.Count} beatmaps for ambient playback");
    }

    private void AddFromDirectory(string path, HashSet<string> seen)
    {
        var found = BeatmapDecoder.ScanSongsDirectory(path);
        foreach (var b in found)
            if (seen.Add(b.FilePath))
                _beatmaps.Add(b);
    }

    private void PickRandomBackground()
    {
        _background.Unload();
        _nowPlaying = null;
        _musicStarted = false;

        if (_beatmaps.Count == 0) return;

        // Pre-validate: only keep beatmaps whose audio file actually exists on disk
        var validated = new List<(BeatmapInfo bm, string audioPath, string? bgPath)>();
        foreach (var b in _beatmaps)
        {
            string audio = ResolveAudioPath(b);
            if (string.IsNullOrEmpty(audio)) continue; // no valid audio file

            string? bg = ResolveBgPath(b);
            validated.Add((b, audio, bg));
        }

        if (validated.Count == 0) return;

        // Prefer candidates that have BOTH audio and background image
        var withBg = validated.Where(v => v.bgPath != null).ToList();
        var pool = withBg.Count > 0 ? withBg : validated;

        var rng = new Random();
        var (pick, pickAudio, pickBg) = pool[rng.Next(pool.Count)];

        // Load audio (already validated to exist)
        if (Engine.Audio.LoadMusic(pickAudio))
        {
            double seekMs = pick.PreviewTime > 0 ? pick.PreviewTime : 0;
            Engine.Audio.PlayMusic();
            if (seekMs > 0)
                Engine.Audio.SeekMusic(seekMs);
            else
            {
                double dur = Engine.Audio.MusicDuration;
                if (dur > 0) Engine.Audio.SeekMusic(dur * 0.3);
            }
            Engine.Audio.SetMusicVolume(0f); // will fade in
            _musicVolume = 0f;
            _musicTarget = 1f;
            _musicStarted = true;
            _changingTrack = false;
            _nowPlaying = pick;
        }

        // Load background image (already validated to exist)
        if (pickBg != null)
        {
            string folder = string.IsNullOrEmpty(pick.FolderPath)
                ? (Path.GetDirectoryName(pick.FilePath) ?? "")
                : pick.FolderPath;
            var stub = new BeatmapData
            {
                FilePath = pick.FilePath,
                FolderPath = folder,
                BackgroundFilename = pick.BackgroundFilename
            };
            _background.Load(stub);
        }
    }

    /// <summary>
    /// Check if a beatmap's background image file actually exists on disk.
    /// Returns the resolved path or null.
    /// </summary>
    private string? ResolveBgPath(BeatmapInfo bm)
    {
        if (string.IsNullOrEmpty(bm.BackgroundFilename)) return null;
        string folder = string.IsNullOrEmpty(bm.FolderPath)
            ? (Path.GetDirectoryName(bm.FilePath) ?? "")
            : bm.FolderPath;
        string direct = Path.Combine(folder, bm.BackgroundFilename);
        if (File.Exists(direct)) return direct;

        // Try lazer hash store
        if (LazerAudioResolver.IsLazerPath(bm.FilePath))
        {
            var resolved = LazerFileResolver.ResolveFile(bm.FilePath, bm.BackgroundFilename);
            if (resolved != null) return resolved;
        }
        return null;
    }

    private string ResolveAudioPath(BeatmapInfo bm)
    {
        if (string.IsNullOrEmpty(bm.AudioFilename)) return "";
        string folder = string.IsNullOrEmpty(bm.FolderPath)
            ? (Path.GetDirectoryName(bm.FilePath) ?? "")
            : bm.FolderPath;
        string direct = Path.Combine(folder, bm.AudioFilename);
        if (File.Exists(direct)) return direct;
        var resolved = LazerAudioResolver.ResolveAudio(bm.FilePath, bm.AudioFilename);
        return resolved ?? "";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Update
    // ═══════════════════════════════════════════════════════════════════

    public override void Update(double dt)
    {
        _time += dt;
        float fdt = (float)dt;

        _enterAnim = MathF.Min(1f, _enterAnim + fdt * 3.5f);
        _selectBounce = MathF.Max(0f, _selectBounce - fdt * 6f);
        _npPulse += fdt * 2.5f;

        // Background fade-in
        _bgFade = MathF.Min(1f, _bgFade + fdt * 1.5f);

        // Music volume management with smooth transitions
        if (_musicStarted)
        {
            // Smoothly lerp toward target
            float fadeSpeed = _musicTarget > _musicVolume ? 0.5f : 1.8f; // slow in, faster out
            _musicVolume += (_musicTarget - _musicVolume) * fdt * fadeSpeed;
            _musicVolume = MathF.Max(0f, MathF.Min(1f, _musicVolume));

            if (Engine.Audio.IsMusicLoaded)
                Engine.Audio.SetMusicVolume(_musicVolume * 0.30f * Game.Settings.MasterVolume * Game.Settings.MusicVolume); // scaled by settings

            // When fading out for a track change, wait until quiet then switch
            if (_changingTrack && _musicVolume < 0.02f)
            {
                _changingTrack = false;
                _musicStarted = false;
                Engine.Audio.StopMusic();
                _trackEndDelay = 1.2; // brief silence before next track
            }

            // Track ended naturally — start fade-out then switch
            if (!_changingTrack && Engine.Audio.IsMusicLoaded && !Engine.Audio.IsPlaying && _musicVolume > 0.05f)
            {
                _changingTrack = true;
                _musicTarget = 0f;
            }
        }
        else if (_trackEndDelay > 0)
        {
            // Brief silence between tracks
            _trackEndDelay -= dt;
            if (_trackEndDelay <= 0)
                PickRandomBackground();
        }

        if (_confirming)
        {
            _confirmAnim = MathF.Min(1f, _confirmAnim + fdt * 4f);

            // Fade music out during confirm via target system
            if (_musicStarted)
                _musicTarget = 0f;

            if (_confirmAnim >= 1f)
                _items[_selected].OnSelect();
            return;
        }

        var input = Engine.Input;

        if (input.IsPressed(Keys.Up) || input.IsPressed(Keys.Left))
        {
            _selected = (_selected - 1 + _items.Length) % _items.Length;
            _selectBounce = 1f;
        }
        if (input.IsPressed(Keys.Down) || input.IsPressed(Keys.Right))
        {
            _selected = (_selected + 1) % _items.Length;
            _selectBounce = 1f;
        }

        if (input.IsPressed(Keys.Enter) || input.IsPressed(Keys.Space))
        {
            // Settings opens overlay immediately (no confirm animation needed)
            if (_items[_selected].Label == "Settings")
            {
                _items[_selected].OnSelect();
            }
            else
            {
                _confirming = true;
                _confirmAnim = 0;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Render
    // ═══════════════════════════════════════════════════════════════════

    public override void Render(double dt)
    {
        var batch = Engine.SpriteBatch;
        var font  = Engine.Font;
        var px    = Engine.PixelTex;
        var proj  = Engine.Projection;
        int sw    = Engine.ScreenWidth;
        int sh    = Engine.ScreenHeight;

        float fadeA = EaseOutCubic(_enterAnim);

        batch.Begin(proj);

        // ── Background ──
        batch.Draw(px, 0, 0, sw, sh, 0.04f, 0.03f, 0.07f, 1f);

        // Beatmap background image with crossfade
        if (_background.HasBackground)
        {
            float savedDim = _background.DimLevel;
            // Blend dim from fully black → target dim as bgFade progresses
            _background.DimLevel = 1f - (1f - savedDim) * EaseOutCubic(_bgFade);
            _background.Render();
            _background.DimLevel = savedDim;
        }

        // ── Menu items (centred) ──
        float totalH = _items.Length * ItemH + (_items.Length - 1) * ItemGap;
        float startY = (sh - totalH) * 0.5f + 10f; // nudge slightly below centre
        float itemW  = MathF.Min(480f, sw * 0.50f);
        float itemX  = (sw - itemW) * 0.5f;

        for (int i = 0; i < _items.Length; i++)
        {
            float rowDelay = i * 0.10f + 0.15f;
            float rowT = MathF.Max(0f, ((float)_time - rowDelay) / 0.35f);
            float rowAlpha = MathF.Min(1f, rowT) * fadeA;
            float slideX = (1f - EaseOutCubic(MathF.Min(1f, rowT))) * 40f;

            bool selected = i == _selected;
            float[] accent = _items[i].Accent;
            float y = startY + i * (ItemH + ItemGap);

            // Confirming: selected pulses, others fade
            float itemAlpha = rowAlpha;
            if (_confirming)
            {
                if (selected)
                {
                    float pulse = 0.7f + MathF.Sin((float)_time * 12f) * 0.3f;
                    itemAlpha *= pulse;
                }
                else
                {
                    itemAlpha *= 1f - _confirmAnim;
                }
            }

            // Item background
            float bgBright = selected ? 0.13f : 0.06f;
            float bgA = selected ? 0.92f : 0.55f;
            batch.Draw(px, itemX + slideX, y, itemW, ItemH,
                bgBright, bgBright, bgBright + 0.02f, bgA * itemAlpha);

            // Left accent bar (per-item colour)
            if (selected)
            {
                float barH = ItemH * EaseOutCubic(MathF.Min(1f, (_selectBounce < 0.5f ? 1f : 1f - _selectBounce)));
                float barY = y + (ItemH - barH) * 0.5f;
                batch.Draw(px, itemX + slideX, barY, 4f, barH,
                    accent[0], accent[1], accent[2], itemAlpha);

                // Faint accent glow behind selected item
                batch.Draw(px, itemX + slideX, y, itemW, ItemH,
                    accent[0] * 0.08f, accent[1] * 0.08f, accent[2] * 0.08f, itemAlpha * 0.5f);
            }
            else
            {
                // Thin subtle bar even when not selected
                batch.Draw(px, itemX + slideX, y + 6f, 2f, ItemH - 12f,
                    accent[0] * 0.5f, accent[1] * 0.5f, accent[2] * 0.5f, itemAlpha * 0.35f);
            }

            // Label — bigger when selected
            float labelScale = selected ? 1.5f : 1.2f;
            if (selected && _selectBounce > 0)
                labelScale += _selectBounce * 0.18f;

            float lr, lg, lb;
            if (selected)
            {
                // Tint label with item accent
                lr = 0.7f + accent[0] * 0.3f;
                lg = 0.7f + accent[1] * 0.3f;
                lb = 0.7f + accent[2] * 0.3f;
            }
            else
            {
                lr = 0.60f; lg = 0.60f; lb = 0.65f;
            }

            font.DrawTextShadow(batch, _items[i].Label,
                itemX + 22 + slideX, y + 10, labelScale,
                lr, lg, lb, itemAlpha, 2f);

            // Description
            float descY = y + 12 + font.MeasureHeight(labelScale) + 4;
            font.DrawText(batch, _items[i].Description,
                itemX + 22 + slideX, descY, 0.6f,
                0.42f, 0.42f, 0.48f, itemAlpha * 0.75f);
        }

        // ── Top bar ──
        float[] selAccent = _items[_selected].Accent;
        batch.Draw(px, 0, 0, sw, TopBarH, 0.07f, 0.07f, 0.11f, fadeA);
        batch.Draw(px, 0, TopBarH - 2, sw, 2,
            selAccent[0], selAccent[1], selAccent[2], 0.6f * fadeA);

        font.DrawTextShadow(batch, "TAIKO NOVA", 20, 14, 1.0f,
            1f, 1f, 1f, fadeA, 2f);

        // ── Now playing (top right) ──
        if (_nowPlaying != null)
        {
            string npText = $"{_nowPlaying.Artist} - {_nowPlaying.Title}";
            float npScale = 0.55f;
            float npW = font.MeasureWidth(npText, npScale);
            float npX = sw - npW - 18;
            float npY = 16f;

            // Music note icon (just a dot that pulses)
            float notePulse = 0.5f + MathF.Sin(_npPulse) * 0.15f;
            float noteX = npX - 14;
            batch.Draw(Engine.CircleTex, noteX, npY + 2, 8, 8,
                selAccent[0], selAccent[1], selAccent[2], fadeA * notePulse);

            font.DrawText(batch, npText, npX, npY, npScale,
                0.55f, 0.55f, 0.60f, fadeA * 0.7f);
        }
        else
        {
            // Version when no music
            font.DrawTextRightShadow(batch, "v0.1", sw - 20, 16, 0.7f,
                0.5f, 0.5f, 0.55f, fadeA * 0.5f);
        }

        // ── Bottom bar ──
        float bottomY = sh - BottomBarH;
        batch.Draw(px, 0, bottomY, sw, BottomBarH, 0.07f, 0.07f, 0.11f, fadeA);
        batch.Draw(px, 0, bottomY, sw, 1, 0.25f, 0.25f, 0.30f, 0.4f * fadeA);

        float ctrlY = bottomY + 12;
        float cx = 20;
        cx = DrawKeyHint(batch, font, px, cx, ctrlY, "UP/DOWN", "Navigate", fadeA);
        cx = DrawKeyHint(batch, font, px, cx + 16, ctrlY, "ENTER", "Select", fadeA);
        DrawKeyHint(batch, font, px, cx + 16, ctrlY, "ESC", "Quit", fadeA);

        // ── Confirm overlay (fade to black) ──
        if (_confirmAnim > 0.01f)
            batch.Draw(px, 0, 0, sw, sh, 0f, 0f, 0f, _confirmAnim * 0.6f);

        batch.End();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private float DrawKeyHint(SpriteBatch batch, Engine.Text.BitmapFont font,
        Texture2D px, float x, float y, string key, string action, float fadeA)
    {
        float keyW = font.MeasureWidth(key, 0.6f) + 10;
        float keyH = 20f;
        batch.Draw(px, x, y, keyW, keyH, 0.2f, 0.2f, 0.25f, fadeA * 0.9f);
        font.DrawText(batch, key, x + 5, y + 3, 0.6f, 0.9f, 0.9f, 0.95f, fadeA);
        float actionX = x + keyW + 6;
        font.DrawText(batch, action, actionX, y + 3, 0.6f, 0.5f, 0.5f, 0.55f, fadeA * 0.8f);
        return actionX + font.MeasureWidth(action, 0.6f);
    }

    private static float EaseOutCubic(float t)
    {
        float t1 = 1f - MathF.Max(0f, MathF.Min(1f, t));
        return 1f - t1 * t1 * t1;
    }

    public override void OnEscape()
    {
        int exitIdx = _items.Length - 1;
        if (_selected == exitIdx)
        {
            if (!_confirming)
            {
                _confirming = true;
                _confirmAnim = 0;
            }
        }
        else
        {
            _selected = exitIdx;
            _selectBounce = 1f;
        }
    }

    public override void Dispose()
    {
        _background.Dispose();
    }
}

using TaikoNova.Engine.GL;
using TaikoNova.Engine.Video;
using TaikoNova.Game.Beatmap;

namespace TaikoNova.Engine;

/// <summary>
/// Manages beatmap background rendering — static images and video.
/// Handles loading, aspect-ratio-correct rendering, dimming, and
/// video frame advancement synced to game time.
/// </summary>
public sealed class BackgroundManager : IDisposable
{
    private readonly GameEngine _engine;

    // ── Static background ──
    private Texture2D? _bgTexture;
    private bool _hasBg;

    // ── Video background ──
    private VideoDecoder? _videoDecoder;
    private Texture2D? _videoTexture;
    private bool _hasVideo;
    private double _videoOffset;       // ms offset from beatmap
    private double _lastFrameTime;     // game time of last decoded frame
    private string _videoPath = "";    // stored for seeking
    private bool _videoStarted;

    // ── Settings ──
    private float _dimLevel = 0.65f;   // 0 = no dim, 1 = fully black
    private const int VideoWidth = 854;    // 480p — good balance of quality / perf
    private const int VideoHeight = 480;
    private const double VideoFps = 30;

    public bool HasBackground => _hasBg || _hasVideo;
    public float DimLevel { get => _dimLevel; set => _dimLevel = Math.Clamp(value, 0f, 1f); }

    public BackgroundManager(GameEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Load background image and/or video for a beatmap.
    /// Handles both standard osu! folders and lazer hash store paths.
    /// </summary>
    public void Load(BeatmapData beatmap)
    {
        Unload();

        string folder = beatmap.FolderPath;
        bool isLazer = LazerAudioResolver.IsLazerPath(beatmap.FilePath);

        // ── 1. Try loading background image ──
        if (!string.IsNullOrEmpty(beatmap.BackgroundFilename))
        {
            string bgPath = Path.Combine(folder, beatmap.BackgroundFilename);

            if (File.Exists(bgPath))
            {
                LoadBackgroundImage(bgPath);
            }
            else if (isLazer)
            {
                // Try resolving from lazer hash store
                string? resolved = LazerFileResolver.ResolveFile(
                    beatmap.FilePath, beatmap.BackgroundFilename);
                if (resolved != null)
                    LoadBackgroundImage(resolved);
            }

            if (!_hasBg)
                Console.WriteLine($"[BG] Background image not found: {beatmap.BackgroundFilename}");
        }

        // ── 2. Try loading video ──
        if (!string.IsNullOrEmpty(beatmap.VideoFilename))
        {
            string videoPath = Path.Combine(folder, beatmap.VideoFilename);

            if (File.Exists(videoPath))
            {
                LoadVideo(videoPath, beatmap.VideoOffset);
            }
            else if (isLazer)
            {
                string? resolved = LazerFileResolver.ResolveFile(
                    beatmap.FilePath, beatmap.VideoFilename);
                if (resolved != null)
                    LoadVideo(resolved, beatmap.VideoOffset);
            }

            if (!_hasVideo)
                Console.WriteLine($"[BG] Video not found: {beatmap.VideoFilename}");
        }
    }

    private void LoadBackgroundImage(string path)
    {
        _bgTexture = Texture2D.FromFile(path);
        if (_bgTexture != null)
        {
            _hasBg = true;
            Console.WriteLine($"[BG] Loaded background: {Path.GetFileName(path)} ({_bgTexture.Width}x{_bgTexture.Height})");
        }
    }

    private void LoadVideo(string path, int offsetMs)
    {
        _videoDecoder = new VideoDecoder();
        if (_videoDecoder.Open(path, VideoWidth, VideoHeight, VideoFps))
        {
            _videoTexture = Texture2D.CreateBlank(VideoWidth, VideoHeight);
            _hasVideo = true;
            _videoOffset = offsetMs;
            _videoPath = path;
            _videoStarted = false;
            _lastFrameTime = double.MinValue;
            Console.WriteLine($"[BG] Video loaded: {Path.GetFileName(path)} (offset={offsetMs}ms)");
        }
        else
        {
            _videoDecoder.Dispose();
            _videoDecoder = null;
        }
    }

    /// <summary>
    /// Update video frame based on current game time.
    /// Call this every frame during gameplay.
    /// </summary>
    public void Update(double currentTimeMs)
    {
        if (!_hasVideo || _videoDecoder == null || _videoTexture == null)
            return;

        double videoTime = currentTimeMs - _videoOffset;

        // Don't start video until its offset time
        if (videoTime < 0)
            return;

        // Check if we need a new frame
        double nextFrameTime = _lastFrameTime + _videoDecoder.FrameDuration;
        if (currentTimeMs < nextFrameTime)
            return;

        // Decode the next frame
        byte[]? frame = _videoDecoder.ReadFrame();
        if (frame != null)
        {
            _videoTexture.UpdatePixels(frame);
            _lastFrameTime = currentTimeMs;
            _videoStarted = true;
        }
        else if (!_videoDecoder.IsOpen)
        {
            // Video ended — keep showing last frame (or fall back to static BG)
        }
    }

    /// <summary>
    /// Render the background (image or video + dim overlay).
    /// Call BEFORE rendering the playfield.
    /// </summary>
    public void Render()
    {
        if (!HasBackground) return;

        var batch = _engine.SpriteBatch;
        var pixel = _engine.PixelTex;
        int sw = _engine.ScreenWidth;
        int sh = _engine.ScreenHeight;

        // Choose texture: video takes priority over static BG when playing
        Texture2D? tex = null;
        if (_hasVideo && _videoStarted && _videoTexture != null)
            tex = _videoTexture;
        else if (_hasBg && _bgTexture != null)
            tex = _bgTexture;

        if (tex != null)
        {
            // Aspect-ratio-correct fill (cover the screen)
            float texAspect = (float)tex.Width / tex.Height;
            float screenAspect = (float)sw / sh;

            float drawW, drawH, drawX, drawY;
            if (texAspect > screenAspect)
            {
                // Texture is wider — fit height, crop sides
                drawH = sh;
                drawW = sh * texAspect;
                drawX = (sw - drawW) / 2f;
                drawY = 0;
            }
            else
            {
                // Texture is taller — fit width, crop top/bottom
                drawW = sw;
                drawH = sw / texAspect;
                drawX = 0;
                drawY = (sh - drawH) / 2f;
            }

            batch.Draw(tex, drawX, drawY, drawW, drawH, 1f, 1f, 1f, 1f);

            // Dim overlay
            if (_dimLevel > 0f)
                batch.Draw(pixel, 0, 0, sw, sh, 0f, 0f, 0f, _dimLevel);
        }
    }

    /// <summary>Unload all background resources.</summary>
    public void Unload()
    {
        _bgTexture?.Dispose();
        _bgTexture = null;
        _hasBg = false;

        _videoDecoder?.Dispose();
        _videoDecoder = null;
        _videoTexture?.Dispose();
        _videoTexture = null;
        _hasVideo = false;
        _videoStarted = false;
        _videoPath = "";
    }

    public void Dispose()
    {
        Unload();
    }
}

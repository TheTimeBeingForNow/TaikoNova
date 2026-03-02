using TaikoNova.Engine;
using TaikoNova.Engine.GL;
using TaikoNova.Game.Skin;

namespace TaikoNova.Game.Taiko;

/// <summary>
/// Renders the taiko playfield: track background, judgment area,
/// barlines, hit effects, and the drum receptor.
/// </summary>
public class Playfield
{
    private readonly GameEngine _engine;

    // ── Hit flash state ──
    private double _donFlashTime;
    private double _katFlashTime;

    // ── Hit shockwave (expanding line across track on hit) ──
    private double _shockwaveTime;
    private bool _shockwaveIsDon;

    // ── Barline data ──
    private readonly List<double> _barlineTimes = new();

    public float TrackTop => SkinConfig.PlayfieldY - SkinConfig.TrackHeight / 2f;
    public float TrackBottom => SkinConfig.PlayfieldY + SkinConfig.TrackHeight / 2f;

    public Playfield(GameEngine engine)
    {
        _engine = engine;
    }

    /// <summary>Precompute barline positions from timing points.</summary>
    public void BuildBarlines(Beatmap.BeatmapData map)
    {
        _barlineTimes.Clear();

        if (map.TimingPoints.Count == 0) return;

        // Get first uninherited timing point
        var firstTP = map.GetTimingPointAt(0);
        double beatLen = firstTP.BeatLength;
        if (beatLen <= 0) return;

        // Find last hit object time
        double lastTime = 0;
        foreach (var ho in map.HitObjects)
        {
            double end = ho.IsLong ? ho.EndTime : ho.Time;
            if (end > lastTime) lastTime = end;
        }
        lastTime += 2000;

        // Generate barlines at each measure
        double time = firstTP.Time;
        // Go back to find first barline before map start
        while (time > 0) time -= beatLen * firstTP.TimeSignature;
        if (time < 0) time += beatLen * firstTP.TimeSignature;

        while (time <= lastTime)
        {
            _barlineTimes.Add(time);
            time += beatLen * firstTP.TimeSignature;
        }
    }

    /// <summary>Trigger a don hit flash.</summary>
    public void FlashDon(double time) { _donFlashTime = time; _shockwaveTime = time; _shockwaveIsDon = true; }

    /// <summary>Trigger a kat hit flash.</summary>
    public void FlashKat(double time) { _katFlashTime = time; _shockwaveTime = time; _shockwaveIsDon = false; }

    public void Render(double currentTime, float scrollSpeed)
    {
        var batch = _engine.SpriteBatch;
        var pixel = _engine.PixelTex;
        var circle = _engine.CircleTex;
        var glow = _engine.GlowTex;
        var ring = _engine.RingTex;
        int sw = _engine.ScreenWidth;

        float top = TrackTop;
        float bot = TrackBottom;
        float h = SkinConfig.TrackHeight;
        float hitX = SkinConfig.HitPositionX;
        float cy = SkinConfig.PlayfieldY;

        // Track — earthy band with subtle gradient
        batch.Draw(pixel, 0, top, sw, h, SkinConfig.TrackColor);
        // Subtle inner gradient (lighter at center)
        batch.Draw(pixel, 0, cy - 8, sw, 16, 0.14f, 0.14f, 0.14f, 0.15f);
        // Thick pixel borders (3px)
        batch.Draw(pixel, 0, top, sw, 3f, SkinConfig.TrackBorder);
        batch.Draw(pixel, 0, bot - 3f, sw, 3f, SkinConfig.TrackBorder);

        // Hit position guide line (subtle vertical)
        batch.Draw(pixel, hitX - 1, top + 3, 2f, h - 6, 0.25f, 0.25f, 0.30f, 0.15f);

        // Barlines (2px wide, with subtle glow for major ones)
        int barIdx = 0;
        foreach (double bt in _barlineTimes)
        {
            float x = hitX + (float)(bt - currentTime) * scrollSpeed;
            if (x < hitX - 2 || x > sw + 2) { barIdx++; continue; }

            float barAlpha = SkinConfig.BarlineColor[3];
            batch.Draw(pixel, x, top + 3, 2f, h - 6,
                SkinConfig.BarlineColor[0], SkinConfig.BarlineColor[1],
                SkinConfig.BarlineColor[2], barAlpha);
            barIdx++;
        }

        // ── Hit shockwave (horizontal flash across track) ──
        RenderShockwave(batch, pixel, currentTime, hitX, top, h, sw);

        // Drum receptor — chunky pixel ring + fill
        float ds = SkinConfig.DrumSize;

        // Pulse the drum ring on hits
        float donPulse = GetFlashT(currentTime, _donFlashTime);
        float katPulse = GetFlashT(currentTime, _katFlashTime);
        float pulse = MathF.Max(donPulse, katPulse);
        float ringScale = ds + 14f + pulse * 6f;

        Circ(batch, ring, hitX, cy, ringScale, SkinConfig.DrumRing);
        Circ(batch, circle, hitX, cy, ds, SkinConfig.DrumFill);

        // Inner detail ring
        Circ(batch, ring, hitX, cy, ds * 0.65f, 0.2f, 0.2f, 0.2f, 0.3f);

        // Hit flash — blocky pixel glow
        RenderFlash(batch, glow, circle, hitX, cy, ds, currentTime,
            _donFlashTime, SkinConfig.DonFlash, SkinConfig.DonColor);
        RenderFlash(batch, glow, circle, hitX, cy, ds, currentTime,
            _katFlashTime, SkinConfig.KatFlash, SkinConfig.KatColor);
    }

    private void RenderShockwave(Engine.GL.SpriteBatch batch, Engine.GL.Texture2D pixel,
        double currentTime, float hitX, float top, float h, int sw)
    {
        double age = currentTime - _shockwaveTime;
        if (age < 0 || age > 200) return;

        float t = (float)(age / 200.0);
        float alpha = (1f - t) * 0.2f;
        float[] col = _shockwaveIsDon ? SkinConfig.DonFlash : SkinConfig.KatFlash;

        // Expanding horizontal flash from hit position
        float width = (sw - hitX) * t;
        batch.Draw(pixel, hitX, top + 3, width, h - 6,
            col[0], col[1], col[2], alpha * (1f - t));
    }

    private float GetFlashT(double now, double flashTime)
    {
        double age = now - flashTime;
        if (age < 0 || age >= SkinConfig.HitFlashDuration) return 0f;
        return 1f - (float)(age / SkinConfig.HitFlashDuration);
    }

    private void RenderFlash(Engine.GL.SpriteBatch batch, Engine.GL.Texture2D glow,
        Engine.GL.Texture2D circle, float cx, float cy, float drumSize,
        double now, double flashTime, float[] flashColor, float[] tintColor)
    {
        double age = now - flashTime;
        if (age < 0 || age >= SkinConfig.HitFlashDuration) return;
        float t = (float)(age / SkinConfig.HitFlashDuration);
        float a = (1f - t) * flashColor[3];
        float s = drumSize * (0.9f + t * 0.3f);
        Circ(batch, glow, cx, cy, s, flashColor[0], flashColor[1], flashColor[2], a);
        Circ(batch, circle, cx, cy, drumSize * 0.6f,
            tintColor[0], tintColor[1], tintColor[2], a * 0.35f);
    }

    private static void Circ(Engine.GL.SpriteBatch b, Engine.GL.Texture2D t,
        float cx, float cy, float d, float[] c)
    {
        float r = d / 2f;
        b.Draw(t, cx - r, cy - r, d, d, c);
    }

    private static void Circ(Engine.GL.SpriteBatch b, Engine.GL.Texture2D t,
        float cx, float cy, float d, float r, float g, float bl, float a)
    {
        float h = d / 2f;
        b.Draw(t, cx - h, cy - h, d, d, r, g, bl, a);
    }
}

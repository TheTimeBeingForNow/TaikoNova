using OpenTK.Windowing.GraphicsLibraryFramework;
using TaikoNova.Engine;
using TaikoNova.Game.Beatmap;
using TaikoNova.Game.Skin;
using TaikoNova.Game.Taiko;

namespace TaikoNova.Game.Screens;

/// <summary>
/// Results screen shown after completing a beatmap.
/// Displays score, accuracy, grade, combo, and judgment breakdown.
/// </summary>
public class ResultScreen : Screen
{
    private BeatmapData? _beatmap;
    private ScoreProcessor? _score;
    private double _animTime;
    private bool _exiting;
    private BackgroundManager _background;

    public ResultScreen(GameEngine engine, TaikoGame game) : base(engine, game)
    {
        _background = new BackgroundManager(engine);
        _background.DimLevel = 0.80f; // Heavier dim for readability
    }

    public void SetResults(BeatmapData beatmap, ScoreProcessor score)
    {
        _beatmap = beatmap;
        _score = score;
        _animTime = 0;
        _background.Load(beatmap); // Load BG from beatmap (image only, no video)
    }

    public override void OnEnter()
    {
        _animTime = 0;
        _exiting = false;
    }

    public override void Update(double deltaTime)
    {
        _animTime += deltaTime;

        if (!_exiting && (Engine.Input.IsPressed(Keys.Enter) || Engine.Input.IsPressed(Keys.Space)
            || Engine.Input.IsPressed(Keys.Escape)))
        {
            _exiting = true;
            Game.GoToSongSelect();
        }
    }

    public override void Render(double deltaTime)
    {
        if (_beatmap == null || _score == null) return;

        var batch = Engine.SpriteBatch;
        var font = Engine.Font;
        var pixel = Engine.PixelTex;
        var circle = Engine.CircleTex;
        var proj = Engine.Projection;
        int sw = Engine.ScreenWidth;
        int sh = Engine.ScreenHeight;

        batch.Begin(proj);

        // ── Background ──
        if (_background.HasBackground)
            _background.Render();
        else
            batch.Draw(pixel, 0, 0, sw, sh, SkinConfig.BgColor);

        // ── Title with slide-in ──
        float titleReveal = EaseOutCubic(Math.Min(1f, (float)_animTime * 3f));
        float titleSlide = (1f - titleReveal) * 40f;
        font.DrawCenteredShadow(batch, "RESULTS", sw / 2f, 50 + titleSlide, 2.0f,
            1f, 0.85f, 0.20f, titleReveal);

        string mapText = $"{_beatmap.DisplayArtist} - {_beatmap.DisplayTitle} [{_beatmap.Version}]";
        float mapReveal = EaseOutCubic(Math.Min(1f, Math.Max(0f, (float)_animTime - 0.15f) * 3f));
        font.DrawCentered(batch, mapText, sw / 2f, 100, 0.8f,
            0.60f, 0.50f, 0.38f, mapReveal);

        // ── Grade (bounces in) ──
        float gradeDelay = 0.3f;
        float gradeT = Math.Max(0f, (float)_animTime - gradeDelay);
        float gradeReveal = Math.Min(1f, gradeT * 3f);
        float gradeScale = 4.5f;

        // Bounce easing for grade
        if (gradeT > 0 && gradeT < 0.5f)
        {
            float bt = gradeT / 0.5f;
            gradeScale *= EaseOutBack(bt);
        }

        string grade = _score.Grade;
        float[] gradeColor = grade switch
        {
            "SS" => SkinConfig.GreatColor,
            "S" => SkinConfig.GreatColor,
            "A" => SkinConfig.GoodColor,
            "B" => new float[] { 0.3f, 0.6f, 1f, 1f },
            "C" => new float[] { 0.6f, 0.3f, 0.8f, 1f },
            _ => SkinConfig.MissColor
        };

        // Glow behind grade
        if (gradeReveal > 0.5f && (grade == "SS" || grade == "S"))
        {
            float glowPulse = 0.15f + 0.05f * MathF.Sin((float)_animTime * 3f);
            float glowSize = gradeScale * 30f;
            batch.Draw(Engine.GlowTex, sw / 2f - glowSize, 200 - glowSize,
                glowSize * 2, glowSize * 2,
                gradeColor[0], gradeColor[1], gradeColor[2], glowPulse * gradeReveal);
        }

        font.DrawCenteredShadow(batch, grade, sw / 2f, 200, gradeScale,
            gradeColor[0], gradeColor[1], gradeColor[2], gradeReveal);

        // ── Stats panel ──
        float panelDelay = 0.5f;
        float panelT = Math.Max(0f, (float)_animTime - panelDelay);
        float panelReveal = EaseOutCubic(Math.Min(1f, panelT * 2.5f));

        float panelX = sw / 2f - 260;
        float panelY = 310;
        float panelW = 520;
        float panelH = 380;

        // Panel with subtle glow
        if (panelReveal > 0.01f)
        {
            // Glow behind panel
            batch.Draw(pixel, panelX - 8, panelY - 8, panelW + 16, panelH + 16,
                SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], 0.04f * panelReveal);

            // Panel border
            batch.Draw(pixel, panelX - 3, panelY - 3, panelW + 6, panelH + 6, SkinConfig.NoteBorder);
            batch.Draw(pixel, panelX, panelY, panelW, panelH, 0.10f, 0.08f, 0.06f, 0.94f * panelReveal);

            // Top accent
            batch.Draw(pixel, panelX, panelY, panelW, 3f,
                SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], 0.7f * panelReveal);
        }

        float lineH = 44f;
        float ly = panelY + 24;
        float labelX = panelX + 30;
        float valueX = panelX + panelW - 30;

        // Each stat line reveals with stagger
        int line = 0;

        // Score (animated count-up)
        {
            float lineAlpha = GetLineReveal(panelT, line++);
            float countUp = Math.Min(1f, panelT / SkinConfig.ScoreCountUpTime);
            float easedCount = EaseOutCubic(countUp);
            long displayScore = (long)(_score.Score * easedCount);
            DrawResultLine(font, batch, pixel, "Score", $"{displayScore:N0}",
                labelX, valueX, ly, panelW - 60, lineAlpha, 1f, 1f, 1f);
            ly += lineH;
        }

        // Accuracy with bar
        {
            float lineAlpha = GetLineReveal(panelT, line++);
            double dispAcc = _score.Accuracy * 100;
            DrawResultLine(font, batch, pixel, "Accuracy", $"{dispAcc:F2}%",
                labelX, valueX, ly, panelW - 60, lineAlpha, 1f, 1f, 1f);
            ly += 26;

            // Accuracy bar
            float accBarW = panelW - 60;
            float accBarH = 8f;
            float accBarReveal = lineAlpha * EaseOutCubic(Math.Min(1f, Math.Max(0f, panelT - 0.3f) * 2f));
            batch.Draw(pixel, labelX, ly, accBarW, accBarH, 0.15f, 0.12f, 0.10f, accBarReveal * 0.8f);
            float accFill = (float)_score.Accuracy * accBarW * Math.Min(1f, panelT * 1.5f);
            float[] accCol = _score.Accuracy >= 0.95 ? SkinConfig.GreatColor :
                             _score.Accuracy >= 0.85 ? SkinConfig.GoodColor : SkinConfig.MissColor;
            batch.Draw(pixel, labelX, ly, accFill, accBarH, accCol[0], accCol[1], accCol[2], accBarReveal * 0.9f);
            // Shine
            if (accFill > 1f)
                batch.Draw(pixel, labelX, ly, accFill, 2f, 1f, 1f, 1f, accBarReveal * 0.15f);

            ly += lineH - 8;
        }

        // Max combo
        {
            float lineAlpha = GetLineReveal(panelT, line++);
            DrawResultLine(font, batch, pixel, "Max Combo", $"{_score.MaxCombo}x",
                labelX, valueX, ly, panelW - 60, lineAlpha, 1f, 1f, 1f);
            ly += lineH;
        }

        // Separator
        {
            float sepAlpha = GetLineReveal(panelT, line);
            batch.Draw(pixel, panelX + 20, ly, panelW - 40, 1f, 0.3f, 0.3f, 0.4f, sepAlpha);
            ly += 15;
        }

        // Great
        {
            float lineAlpha = GetLineReveal(panelT, line++);
            DrawResultLine(font, batch, pixel, "Great", $"{_score.CountGreat}",
                labelX, valueX, ly, panelW - 60, lineAlpha,
                SkinConfig.GreatColor[0], SkinConfig.GreatColor[1], SkinConfig.GreatColor[2]);
            ly += lineH;
        }

        // Good
        {
            float lineAlpha = GetLineReveal(panelT, line++);
            DrawResultLine(font, batch, pixel, "Good", $"{_score.CountGood}",
                labelX, valueX, ly, panelW - 60, lineAlpha,
                SkinConfig.GoodColor[0], SkinConfig.GoodColor[1], SkinConfig.GoodColor[2]);
            ly += lineH;
        }

        // Miss
        {
            float lineAlpha = GetLineReveal(panelT, line++);
            DrawResultLine(font, batch, pixel, "Miss", $"{_score.CountMiss}",
                labelX, valueX, ly, panelW - 60, lineAlpha,
                SkinConfig.MissColor[0], SkinConfig.MissColor[1], SkinConfig.MissColor[2]);
        }

        // ── Continue prompt ──
        float promptDelay = panelDelay + line * SkinConfig.ResultLineDelay + 0.3f;
        float promptT = Math.Max(0f, (float)_animTime - promptDelay);
        float promptBase = EaseOutCubic(Math.Min(1f, promptT * 2f));
        float promptPulse = promptBase * (0.5f + 0.5f * (float)Math.Sin(_animTime * 3));
        font.DrawCentered(batch, "Press ENTER to continue", sw / 2f, sh - 50, 1.0f,
            1f, 0.85f, 0.20f, promptPulse);

        batch.End();
    }

    /// <summary>Get reveal alpha for a stat line with stagger delay.</summary>
    private static float GetLineReveal(float panelTime, int lineIndex)
    {
        float delay = lineIndex * SkinConfig.ResultLineDelay;
        float t = Math.Max(0f, panelTime - delay) / SkinConfig.ResultLineFade;
        return EaseOutCubic(Math.Min(1f, t));
    }

    private static float EaseOutCubic(float t)
    {
        float t1 = 1f - t;
        return 1f - t1 * t1 * t1;
    }

    private static float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        float t1 = t - 1f;
        return 1f + c3 * t1 * t1 * t1 + c1 * t1 * t1;
    }

    private void DrawResultLine(Engine.Text.BitmapFont font, Engine.GL.SpriteBatch batch,
        Engine.GL.Texture2D pixel,
        string label, string value, float lx, float rx, float y, float rowW,
        float alpha, float r, float g, float b)
    {
        if (alpha < 0.01f) return;

        // Subtle background stripe for readability
        batch.Draw(pixel, lx - 6, y - 2, rowW + 12, 28f, 0.08f, 0.06f, 0.05f, alpha * 0.3f);

        font.DrawText(batch, label, lx, y, 1.1f, 0.65f, 0.55f, 0.42f, alpha);
        float vw = font.MeasureWidth(value, 1.1f);
        font.DrawText(batch, value, rx - vw, y, 1.1f, r, g, b, alpha);
    }

    public override void OnExit()
    {
        _background.Unload();
    }

    public override void OnEscape()
    {
        if (!_exiting)
        {
            _exiting = true;
            Game.GoToSongSelect();
        }
    }

    public override void Dispose()
    {
        _background?.Dispose();
    }
}

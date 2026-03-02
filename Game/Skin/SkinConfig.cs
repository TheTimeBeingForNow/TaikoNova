namespace TaikoNova.Game.Skin;

/// <summary>
/// Terraria pixel-art inspired palette.
/// Warm earthy tones, chunky proportions, hard edges.
/// </summary>
public static class SkinConfig
{
    // ── Layout ──
    public const float PlayfieldY   = 420f;
    public const float TrackHeight  = 140f;
    public const float HitPositionX = 280f;
    public const float NoteSize     = 64f;
    public const float BigNoteSize  = 88f;
    public const float DrumSize     = 112f;
    public const float BaseScrollSpeed = 0.85f;

    // ── Palette (Terraria vibes) ──
    // Background — deep night sky
    public static readonly float[] BgColor     = { 0.04f, 0.03f, 0.09f, 1f };
    // Track — dark gray / black
    public static readonly float[] TrackColor  = { 0.10f, 0.10f, 0.10f, 0.95f };
    public static readonly float[] TrackBorder = { 0.25f, 0.25f, 0.25f, 0.8f };
    public static readonly float[] BarlineColor= { 0.30f, 0.30f, 0.30f, 0.3f };

    // Don — warm Terraria red
    public static readonly float[] DonColor  = { 0.86f, 0.24f, 0.18f, 1f };
    // Kat — Terraria sky blue
    public static readonly float[] KatColor  = { 0.24f, 0.56f, 0.90f, 1f };
    // Note border — thick dark outline (pixel art style)
    public static readonly float[] NoteBorder = { 0.08f, 0.06f, 0.04f, 1f };

    // Drumroll — Terraria gold
    public static readonly float[] DrumrollColor = { 0.92f, 0.72f, 0.16f, 1f };
    public static readonly float[] DrumrollEnd   = { 0.76f, 0.56f, 0.10f, 1f };
    // Denden — Terraria purple
    public static readonly float[] DendenColor   = { 0.58f, 0.28f, 0.82f, 1f };

    // Judgments
    public static readonly float[] GreatColor = { 1f, 0.85f, 0.20f, 1f };
    public static readonly float[] GoodColor  = { 0.30f, 0.85f, 0.40f, 1f };
    public static readonly float[] MissColor  = { 0.85f, 0.22f, 0.18f, 1f };

    // HP bar — Terraria heart red / green
    public static readonly float[] HpBg     = { 0.14f, 0.10f, 0.08f, 0.9f };
    public static readonly float[] HpFill   = { 0.22f, 0.78f, 0.30f, 1f };
    public static readonly float[] HpDanger = { 0.86f, 0.20f, 0.16f, 1f };

    // Drum receptor — gray
    public static readonly float[] DrumRing = { 0.35f, 0.35f, 0.35f, 0.9f };
    public static readonly float[] DrumFill = { 0.12f, 0.12f, 0.12f, 0.95f };

    // Hit flash
    public static readonly float[] DonFlash = { 1f, 0.40f, 0.25f, 0.7f };
    public static readonly float[] KatFlash = { 0.30f, 0.60f, 1f, 0.7f };

    // Accent
    public static readonly float[] Accent = { 0.86f, 0.24f, 0.18f, 1f };

    // ── Timing ──
    public const float JudgmentFade     = 400f;
    public const float HitFlashDuration = 120f;
    public const float ComboPopScale    = 1.30f;
    public const float ComboPopMs       = 90f;

    // ── HUD ──
    public const float HudMargin  = 20f;
    public const float HpBarWidth = 360f;
    public const float HpBarH     = 10f;

    // ── Kiai ──
    public static readonly float[] KiaiGlow = { 1f, 0.80f, 0.25f, 0.06f };

    // ── Hit explosions ──
    public const int   MaxExplosions       = 8;
    public const float ExplosionDuration   = 350f;   // ms
    public const float ExplosionMaxRadius  = 100f;

    // ── Combo milestones ──
    public const float MilestoneFlashDuration = 600f; // ms
    public static readonly float[] MilestoneColor = { 1f, 0.92f, 0.40f, 1f };

    // ── Result screen ──
    public const float ResultLineDelay  = 0.18f;  // seconds between each stat line reveal
    public const float ResultLineFade   = 0.35f;  // seconds for each line to fade in
    public const float ScoreCountUpTime = 1.8f;   // seconds to count score up

    // ── Song select ──
    public const float CardSelectScale  = 1.0f;
    public const float InfoRevealSpeed  = 6.0f;   // speed of info panel content reveal
}

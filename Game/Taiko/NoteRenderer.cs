using TaikoNova.Engine;
using TaikoNova.Game.Skin;

namespace TaikoNova.Game.Taiko;

/// <summary>
/// Renders taiko hit objects: Don/Kat circles, drumrolls, dendens.
/// Uses procedurally generated textures — clean, competitive look.
/// </summary>
public class NoteRenderer
{
    private readonly GameEngine _engine;

    public NoteRenderer(GameEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Render all visible hit objects.
    /// </summary>
    public void RenderNotes(List<HitObject> hitObjects, double currentTime, float scrollSpeed)
    {
        var batch = _engine.SpriteBatch;
        float hitX = SkinConfig.HitPositionX;
        int sw = _engine.ScreenWidth;

        // Render in reverse order so earlier notes draw on top
        for (int i = hitObjects.Count - 1; i >= 0; i--)
        {
            var ho = hitObjects[i];
            if (ho.IsHit && ho.IsNote) continue; // Skip hit notes (non-long)

            float x = hitX + (float)(ho.Time - currentTime) * scrollSpeed * (float)ho.ScrollMultiplier;

            // Cull off-screen
            if (x < -200 || x > sw + 200) continue;

            // Skip notes that are behind and already missed
            if (ho.IsHit && !ho.IsLong) continue;

            if (ho.IsDrumroll)
                RenderDrumroll(ho, x, currentTime, scrollSpeed);
            else if (ho.IsDenden)
                RenderDenden(ho, x, currentTime);
            else
                RenderNote(ho, x, currentTime);
        }
    }

    private void RenderNote(HitObject ho, float x, double currentTime)
    {
        var batch = _engine.SpriteBatch;
        var circle = _engine.CircleTex;

        float size = ho.IsBig ? SkinConfig.BigNoteSize : SkinConfig.NoteSize;
        float cy = SkinConfig.PlayfieldY;
        float[] col = ho.IsDon ? SkinConfig.DonColor : SkinConfig.KatColor;

        // Approach scaling — notes swell slightly as they near the hit zone
        float distFromHit = MathF.Abs(x - SkinConfig.HitPositionX);
        float approachZone = 200f;
        float approachScale = 1f;
        if (distFromHit < approachZone)
        {
            float proximity = 1f - distFromHit / approachZone;
            approachScale = 1f + proximity * 0.08f; // max 8% bigger at hit position
        }
        float scaledSize = size * approachScale;

        // Thick dark pixel-art border
        float brd = scaledSize + 10f;
        float bh = brd / 2f;
        batch.Draw(circle, x - bh, cy - bh, brd, brd, SkinConfig.NoteBorder);

        // Flat color fill
        float h = scaledSize / 2f;
        batch.Draw(circle, x - h, cy - h, scaledSize, scaledSize, col);

        // Tiny pixel highlight (top-left shine like Terraria items)
        float shine = scaledSize * 0.3f;
        float sh2 = shine / 2f;
        batch.Draw(circle, x - h + 3, cy - h + 3, shine, shine,
            1f, 1f, 1f, 0.18f);

        // Bottom-right shadow for depth
        float shadowSize = scaledSize * 0.25f;
        batch.Draw(circle, x + h - shadowSize - 2, cy + h - shadowSize - 2, shadowSize, shadowSize,
            0f, 0f, 0f, 0.12f);
    }

    private void RenderDrumroll(HitObject ho, float startX, double currentTime, float scrollSpeed)
    {
        var batch = _engine.SpriteBatch;
        var pixel = _engine.PixelTex;
        var circle = _engine.CircleTex;

        float endX = SkinConfig.HitPositionX +
            (float)(ho.EndTime - currentTime) * scrollSpeed * (float)ho.ScrollMultiplier;

        float size = ho.IsBig ? SkinConfig.BigNoteSize : SkinConfig.NoteSize;
        float halfSize = size / 2f;
        float cy = SkinConfig.PlayfieldY;

        float bodyLeft = Math.Min(startX, endX);
        float bodyRight = Math.Max(startX, endX);
        float bodyWidth = bodyRight - bodyLeft;

        float bp = 5f;
        // Thick pixel border
        batch.Draw(pixel, bodyLeft, cy - halfSize - bp, bodyWidth, size + bp * 2, SkinConfig.NoteBorder);
        batch.Draw(circle, startX - halfSize - bp, cy - halfSize - bp, size + bp * 2, size + bp * 2, SkinConfig.NoteBorder);
        batch.Draw(circle, endX - halfSize - bp, cy - halfSize - bp, size + bp * 2, size + bp * 2, SkinConfig.NoteBorder);
        // Body
        batch.Draw(pixel, bodyLeft, cy - halfSize, bodyWidth, size, SkinConfig.DrumrollColor);
        batch.Draw(circle, startX - halfSize, cy - halfSize, size, size, SkinConfig.DrumrollColor);
        batch.Draw(circle, endX - halfSize, cy - halfSize, size, size, SkinConfig.DrumrollEnd);

        // Tick markers inside drumroll
        if (ho.TicksRequired > 0)
        {
            double duration = ho.EndTime - ho.Time;
            for (int t = 0; t <= ho.TicksRequired; t++)
            {
                double tickTime = ho.Time + (duration * t / ho.TicksRequired);
                float tickX = SkinConfig.HitPositionX +
                    (float)(tickTime - currentTime) * scrollSpeed * (float)ho.ScrollMultiplier;

                if (tickX >= bodyLeft && tickX <= bodyRight)
                {
                    batch.Draw(pixel, tickX - 1, cy - halfSize * 0.4f, 2f, size * 0.4f,
                        1f, 1f, 1f, 0.4f);
                }
            }
        }
    }

    private void RenderDenden(HitObject ho, float x, double currentTime)
    {
        var batch = _engine.SpriteBatch;
        var circle = _engine.CircleTex;
        var ring = _engine.RingTex;
        var glow = _engine.GlowTex;

        float size = SkinConfig.BigNoteSize * 1.3f;
        float halfSize = size / 2f;
        float cy = SkinConfig.PlayfieldY;

        // Progress
        float progress = ho.TicksRequired > 0 ? (float)ho.TicksHit / ho.TicksRequired : 0f;

        // Spinning animation
        float spin = (float)(currentTime * 0.005);

        // Outer glow
        float glowSize = size * 1.8f;
        batch.Draw(glow, x - glowSize / 2, cy - glowSize / 2, glowSize, glowSize,
            SkinConfig.DendenColor[0], SkinConfig.DendenColor[1],
            SkinConfig.DendenColor[2], 0.25f);

        // Main circle
        batch.Draw(circle, x - halfSize, cy - halfSize, size, size,
            SkinConfig.DendenColor);

        // Progress ring
        float ringSize = size + 10f;
        batch.Draw(ring, x - ringSize / 2, cy - ringSize / 2, ringSize, ringSize,
            1f, 1f, 1f, 0.8f, spin);

        // Inner progress indicator
        float innerSize = size * progress;
        if (innerSize > 2f)
        {
            batch.Draw(circle, x - innerSize / 2, cy - innerSize / 2, innerSize, innerSize,
                1f, 1f, 1f, 0.5f);
        }

        // Remaining count text
        int remaining = ho.TicksRequired - ho.TicksHit;
        if (remaining > 0)
        {
            _engine.Font.DrawCentered(batch, remaining.ToString(), x, cy, 1f, 1f, 1f, 1f, 1f);
        }
    }
}

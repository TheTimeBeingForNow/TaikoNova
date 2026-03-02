using TaikoNova.Engine.GL;

namespace TaikoNova.Engine.Text;

/// <summary>
/// Pixel-art bitmap font. Renders the 5×7 source glyphs at 3× integer
/// scale with NO anti-aliasing — hard pixel edges. Nearest-neighbor
/// atlas for that crispy Terraria look.
/// </summary>
public sealed class BitmapFont : IDisposable
{
    // ── Atlas layout (3× integer scale of 5×7 source — no AA) ──
    private const int Scale = 3;
    private const int SrcW = 5;
    private const int SrcH = 7;
    private const int CharW = SrcW * Scale;       // 15 pixels per glyph
    private const int CharH = SrcH * Scale;       // 21 pixels per glyph
    private const int GlyphW = CharW + Scale;     // 18 with spacing
    private const int GlyphH = CharH + Scale;     // 24 with spacing
    private const int Cols = 16;
    private const int Rows = 6;
    private const int FirstChar = 32;
    private const int LastChar = 126;

    public Texture2D Atlas { get; }
    public int AtlasWidth { get; }
    public int AtlasHeight { get; }

    public BitmapFont()
    {
        AtlasWidth = Cols * GlyphW;
        AtlasHeight = Rows * GlyphH;
        byte[] pixels = new byte[AtlasWidth * AtlasHeight * 4];

        for (int ch = FirstChar; ch <= LastChar; ch++)
        {
            int idx = ch - FirstChar;
            int col = idx % Cols;
            int row = idx / Cols;
            int baseX = col * GlyphW;
            int baseY = row * GlyphH;

            int fontOffset = idx * 5;
            if (fontOffset + 4 >= FontData.Length) continue;

            // Build source bitmap
            bool[,] src = new bool[SrcW, SrcH];
            for (int cx = 0; cx < SrcW; cx++)
            {
                byte column = FontData[fontOffset + cx];
                for (int ry = 0; ry < SrcH; ry++)
                    src[cx, ry] = (column & (1 << ry)) != 0;
            }

            // Integer scale — hard pixels, no AA
            for (int dy = 0; dy < CharH; dy++)
            for (int dx = 0; dx < CharW; dx++)
            {
                int srcX = dx / Scale;
                int srcY = dy / Scale;
                if (srcX < SrcW && srcY < SrcH && src[srcX, srcY])
                {
                    int px = baseX + dx;
                    int py = baseY + dy;
                    int pi = (py * AtlasWidth + px) * 4;
                    pixels[pi + 0] = 255;
                    pixels[pi + 1] = 255;
                    pixels[pi + 2] = 255;
                    pixels[pi + 3] = 255;
                }
            }
        }

        // Nearest-neighbor filtering for pixel-perfect text
        Atlas = Texture2D.FromPixels(AtlasWidth, AtlasHeight, pixels, true);
    }

    /// <summary>
    /// Draw text using SpriteBatch. Must be called between Begin/End.
    /// </summary>
    public void DrawText(SpriteBatch batch, string text, float x, float y, float scale,
        float r, float g, float b, float a)
    {
        float cx = x;
        float atlasW = AtlasWidth;
        float atlasH = AtlasHeight;
        float advance = (CharW + 1) * scale;

        foreach (char ch in text)
        {
            if (ch < FirstChar || ch > LastChar)
            {
                cx += advance;
                continue;
            }

            int idx = ch - FirstChar;
            int col = idx % Cols;
            int row = idx / Cols;

            float u0 = (col * GlyphW) / atlasW;
            float v0 = (row * GlyphH) / atlasH;
            float u1 = (col * GlyphW + CharW) / atlasW;
            float v1 = (row * GlyphH + CharH) / atlasH;

            batch.Draw(Atlas, cx, y, CharW * scale, CharH * scale,
                u0, v0, u1, v1, r, g, b, a);

            cx += advance;
        }
    }

    /// <summary>Draw text with a float[] color.</summary>
    public void DrawText(SpriteBatch batch, string text, float x, float y, float scale, float[] color)
    {
        DrawText(batch, text, x, y, scale, color[0], color[1], color[2], color[3]);
    }

    /// <summary>Draw right-aligned text.</summary>
    public void DrawTextRight(SpriteBatch batch, string text, float rightX, float y, float scale,
        float r, float g, float b, float a)
    {
        DrawText(batch, text, rightX - MeasureWidth(text, scale), y, scale, r, g, b, a);
    }

    /// <summary>Measure the width of text in pixels at the given scale.</summary>
    public float MeasureWidth(string text, float scale)
    {
        if (text.Length == 0) return 0;
        return text.Length * (CharW + 1) * scale - scale;
    }

    /// <summary>Measure the height of text in pixels at the given scale.</summary>
    public float MeasureHeight(float scale)
    {
        return CharH * scale;
    }

    /// <summary>Draw centered text.</summary>
    public void DrawCentered(SpriteBatch batch, string text, float cx, float cy, float scale,
        float r, float g, float b, float a)
    {
        float w = MeasureWidth(text, scale);
        float h = MeasureHeight(scale);
        DrawText(batch, text, cx - w / 2, cy - h / 2, scale, r, g, b, a);
    }

    public void DrawCentered(SpriteBatch batch, string text, float cx, float cy, float scale, float[] color)
    {
        DrawCentered(batch, text, cx, cy, scale, color[0], color[1], color[2], color[3]);
    }

    /// <summary>Draw text with a drop shadow for readability on any background.</summary>
    public void DrawTextShadow(SpriteBatch batch, string text, float x, float y, float scale,
        float r, float g, float b, float a, float shadowOff = 2f)
    {
        DrawText(batch, text, x + shadowOff, y + shadowOff, scale, 0, 0, 0, a * 0.6f);
        DrawText(batch, text, x, y, scale, r, g, b, a);
    }

    /// <summary>Draw right-aligned text with shadow.</summary>
    public void DrawTextRightShadow(SpriteBatch batch, string text, float rightX, float y, float scale,
        float r, float g, float b, float a, float shadowOff = 2f)
    {
        float x = rightX - MeasureWidth(text, scale);
        DrawTextShadow(batch, text, x, y, scale, r, g, b, a, shadowOff);
    }

    /// <summary>Draw centered text with a drop shadow.</summary>
    public void DrawCenteredShadow(SpriteBatch batch, string text, float cx, float cy, float scale,
        float r, float g, float b, float a, float shadowOff = 2f)
    {
        DrawCentered(batch, text, cx + shadowOff, cy + shadowOff, scale, 0, 0, 0, a * 0.6f);
        DrawCentered(batch, text, cx, cy, scale, r, g, b, a);
    }

    public void Dispose() => Atlas.Dispose();

    // ═══════════════════════════════════════════════════════════════════
    // 5×7 column-encoded font data (public domain).
    // Each character = 5 bytes (columns L→R), each byte has 7 bits (rows T→B).
    // Covers ASCII 32 (space) through 126 (~). 95 chars × 5 = 475 bytes.
    // ═══════════════════════════════════════════════════════════════════
    private static readonly byte[] FontData =
    {
        // 32 ' '
        0x00,0x00,0x00,0x00,0x00,
        // 33 '!'
        0x00,0x00,0x5F,0x00,0x00,
        // 34 '"'
        0x00,0x07,0x00,0x07,0x00,
        // 35 '#'
        0x14,0x7F,0x14,0x7F,0x14,
        // 36 '$'
        0x24,0x2A,0x7F,0x2A,0x12,
        // 37 '%'
        0x23,0x13,0x08,0x64,0x62,
        // 38 '&'
        0x36,0x49,0x55,0x22,0x50,
        // 39 '''
        0x00,0x05,0x03,0x00,0x00,
        // 40 '('
        0x00,0x1C,0x22,0x41,0x00,
        // 41 ')'
        0x00,0x41,0x22,0x1C,0x00,
        // 42 '*'
        0x08,0x2A,0x1C,0x2A,0x08,
        // 43 '+'
        0x08,0x08,0x3E,0x08,0x08,
        // 44 ','
        0x00,0x50,0x30,0x00,0x00,
        // 45 '-'
        0x08,0x08,0x08,0x08,0x08,
        // 46 '.'
        0x00,0x60,0x60,0x00,0x00,
        // 47 '/'
        0x20,0x10,0x08,0x04,0x02,
        // 48 '0'
        0x3E,0x51,0x49,0x45,0x3E,
        // 49 '1'
        0x00,0x42,0x7F,0x40,0x00,
        // 50 '2'
        0x42,0x61,0x51,0x49,0x46,
        // 51 '3'
        0x21,0x41,0x45,0x4B,0x31,
        // 52 '4'
        0x18,0x14,0x12,0x7F,0x10,
        // 53 '5'
        0x27,0x45,0x45,0x45,0x39,
        // 54 '6'
        0x3C,0x4A,0x49,0x49,0x30,
        // 55 '7'
        0x01,0x71,0x09,0x05,0x03,
        // 56 '8'
        0x36,0x49,0x49,0x49,0x36,
        // 57 '9'
        0x06,0x49,0x49,0x29,0x1E,
        // 58 ':'
        0x00,0x36,0x36,0x00,0x00,
        // 59 ';'
        0x00,0x56,0x36,0x00,0x00,
        // 60 '<'
        0x00,0x08,0x14,0x22,0x41,
        // 61 '='
        0x14,0x14,0x14,0x14,0x14,
        // 62 '>'
        0x41,0x22,0x14,0x08,0x00,
        // 63 '?'
        0x02,0x01,0x51,0x09,0x06,
        // 64 '@'
        0x32,0x49,0x79,0x41,0x3E,
        // 65 'A'
        0x7E,0x11,0x11,0x11,0x7E,
        // 66 'B'
        0x7F,0x49,0x49,0x49,0x36,
        // 67 'C'
        0x3E,0x41,0x41,0x41,0x22,
        // 68 'D'
        0x7F,0x41,0x41,0x22,0x1C,
        // 69 'E'
        0x7F,0x49,0x49,0x49,0x41,
        // 70 'F'
        0x7F,0x09,0x09,0x01,0x01,
        // 71 'G'
        0x3E,0x41,0x41,0x51,0x32,
        // 72 'H'
        0x7F,0x08,0x08,0x08,0x7F,
        // 73 'I'
        0x00,0x41,0x7F,0x41,0x00,
        // 74 'J'
        0x20,0x40,0x41,0x3F,0x01,
        // 75 'K'
        0x7F,0x08,0x14,0x22,0x41,
        // 76 'L'
        0x7F,0x40,0x40,0x40,0x40,
        // 77 'M'
        0x7F,0x02,0x04,0x02,0x7F,
        // 78 'N'
        0x7F,0x04,0x08,0x10,0x7F,
        // 79 'O'
        0x3E,0x41,0x41,0x41,0x3E,
        // 80 'P'
        0x7F,0x09,0x09,0x09,0x06,
        // 81 'Q'
        0x3E,0x41,0x51,0x21,0x5E,
        // 82 'R'
        0x7F,0x09,0x19,0x29,0x46,
        // 83 'S'
        0x46,0x49,0x49,0x49,0x31,
        // 84 'T'
        0x01,0x01,0x7F,0x01,0x01,
        // 85 'U'
        0x3F,0x40,0x40,0x40,0x3F,
        // 86 'V'
        0x1F,0x20,0x40,0x20,0x1F,
        // 87 'W'
        0x7F,0x20,0x18,0x20,0x7F,
        // 88 'X'
        0x63,0x14,0x08,0x14,0x63,
        // 89 'Y'
        0x03,0x04,0x78,0x04,0x03,
        // 90 'Z'
        0x61,0x51,0x49,0x45,0x43,
        // 91 '['
        0x00,0x00,0x7F,0x41,0x41,
        // 92 '\'
        0x02,0x04,0x08,0x10,0x20,
        // 93 ']'
        0x41,0x41,0x7F,0x00,0x00,
        // 94 '^'
        0x04,0x02,0x01,0x02,0x04,
        // 95 '_'
        0x40,0x40,0x40,0x40,0x40,
        // 96 '`'
        0x00,0x01,0x02,0x04,0x00,
        // 97 'a'
        0x20,0x54,0x54,0x54,0x78,
        // 98 'b'
        0x7F,0x48,0x44,0x44,0x38,
        // 99 'c'
        0x38,0x44,0x44,0x44,0x20,
        // 100 'd'
        0x38,0x44,0x44,0x48,0x7F,
        // 101 'e'
        0x38,0x54,0x54,0x54,0x18,
        // 102 'f'
        0x08,0x7E,0x09,0x01,0x02,
        // 103 'g'
        0x08,0x14,0x54,0x54,0x3C,
        // 104 'h'
        0x7F,0x08,0x04,0x04,0x78,
        // 105 'i'
        0x00,0x44,0x7D,0x40,0x00,
        // 106 'j'
        0x20,0x40,0x44,0x3D,0x00,
        // 107 'k'
        0x00,0x7F,0x10,0x28,0x44,
        // 108 'l'
        0x00,0x41,0x7F,0x40,0x00,
        // 109 'm'
        0x7C,0x04,0x18,0x04,0x78,
        // 110 'n'
        0x7C,0x08,0x04,0x04,0x78,
        // 111 'o'
        0x38,0x44,0x44,0x44,0x38,
        // 112 'p'
        0x7C,0x14,0x14,0x14,0x08,
        // 113 'q'
        0x08,0x14,0x14,0x18,0x7C,
        // 114 'r'
        0x7C,0x08,0x04,0x04,0x08,
        // 115 's'
        0x48,0x54,0x54,0x54,0x20,
        // 116 't'
        0x04,0x3F,0x44,0x40,0x20,
        // 117 'u'
        0x3C,0x40,0x40,0x20,0x7C,
        // 118 'v'
        0x1C,0x20,0x40,0x20,0x1C,
        // 119 'w'
        0x3C,0x40,0x30,0x40,0x3C,
        // 120 'x'
        0x44,0x28,0x10,0x28,0x44,
        // 121 'y'
        0x0C,0x50,0x50,0x50,0x3C,
        // 122 'z'
        0x44,0x64,0x54,0x4C,0x44,
        // 123 '{'
        0x00,0x08,0x36,0x41,0x00,
        // 124 '|'
        0x00,0x00,0x7F,0x00,0x00,
        // 125 '}'
        0x00,0x41,0x36,0x08,0x00,
        // 126 '~'
        0x10,0x08,0x08,0x10,0x08,
    };
}

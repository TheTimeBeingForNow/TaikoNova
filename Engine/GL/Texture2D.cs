using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace TaikoNova.Engine.GL;

/// <summary>
/// OpenGL 2D texture wrapper. Supports creation from raw RGBA pixel data
/// and procedural generation of circles / rings / glows.
/// </summary>
public sealed class Texture2D : IDisposable
{
    public int Handle { get; }
    public int Width { get; }
    public int Height { get; }

    private Texture2D(int handle, int w, int h)
    {
        Handle = handle;
        Width = w;
        Height = h;
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        OpenTK.Graphics.OpenGL4.GL.ActiveTexture(unit);
        OpenTK.Graphics.OpenGL4.GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose()
    {
        OpenTK.Graphics.OpenGL4.GL.DeleteTexture(Handle);
    }

    // ── Factory methods ──

    /// <summary>Load a texture from an image file (PNG, JPG, BMP, etc.).</summary>
    public static Texture2D? FromFile(string path)
    {
        try
        {
            StbImage.stbi_set_flip_vertically_on_load(0);
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return FromPixels(image.Width, image.Height, image.Data, false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Texture] Failed to load '{Path.GetFileName(path)}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Update an existing texture's pixels (for video frame streaming).
    /// The new data must match the texture's width and height.
    /// </summary>
    public void UpdatePixels(byte[] pixels)
    {
        OpenTK.Graphics.OpenGL4.GL.BindTexture(TextureTarget.Texture2D, Handle);
        OpenTK.Graphics.OpenGL4.GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
            Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
    }

    /// <summary>Create a blank RGBA texture of the given size (for video frame target).</summary>
    public static Texture2D CreateBlank(int width, int height)
    {
        return FromPixels(width, height, new byte[width * height * 4], false);
    }

    /// <summary>Create texture from raw RGBA byte data (linear filtering).</summary>
    public static Texture2D FromPixels(int width, int height, byte[] pixels)
    {
        return FromPixels(width, height, pixels, false);
    }

    /// <summary>Create texture from raw RGBA byte data.</summary>
    public static Texture2D FromPixels(int width, int height, byte[] pixels, bool nearest)
    {
        int tex = OpenTK.Graphics.OpenGL4.GL.GenTexture();
        OpenTK.Graphics.OpenGL4.GL.BindTexture(TextureTarget.Texture2D, tex);
        OpenTK.Graphics.OpenGL4.GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        var filter = nearest ? (int)TextureMinFilter.Nearest : (int)TextureMinFilter.Linear;
        var magFilter = nearest ? (int)TextureMagFilter.Nearest : (int)TextureMagFilter.Linear;
        OpenTK.Graphics.OpenGL4.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        OpenTK.Graphics.OpenGL4.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magFilter);
        OpenTK.Graphics.OpenGL4.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        OpenTK.Graphics.OpenGL4.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        return new Texture2D(tex, width, height);
    }

    /// <summary>
    /// Generates a small pixel-art circle (nearest-neighbor).
    /// Hard edges, no AA — scales up chunky like Terraria sprites.
    /// </summary>
    public static Texture2D CreatePixelCircle(int size = 16)
    {
        byte[] px = new byte[size * size * 4];
        float center = size / 2f;
        float radius = size / 2f - 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            bool inside = dist <= radius;

            int i = (y * size + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = inside ? (byte)255 : (byte)0;
        }

        return FromPixels(size, size, px, true);
    }

    /// <summary>Pixel-art ring (hollow circle, hard edges).</summary>
    public static Texture2D CreatePixelRing(int size = 16, int thickness = 2)
    {
        byte[] px = new byte[size * size * 4];
        float center = size / 2f;
        float outerR = size / 2f - 0.5f;
        float innerR = outerR - thickness;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            bool inside = dist <= outerR && dist >= innerR;

            int i = (y * size + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = inside ? (byte)255 : (byte)0;
        }

        return FromPixels(size, size, px, true);
    }

    /// <summary>1×1 white pixel — used for solid-color rectangles.</summary>
    public static Texture2D CreateWhitePixel()
    {
        return FromPixels(1, 1, new byte[] { 255, 255, 255, 255 });
    }

    /// <summary>
    /// Generates a filled circle texture with smooth anti-aliased edges.
    /// White color; tint at draw time.
    /// </summary>
    public static Texture2D CreateCircle(int size = 128)
    {
        byte[] px = new byte[size * size * 4];
        float center = size / 2f;
        float radius = size / 2f - 1.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float alpha = Math.Clamp(radius - dist + 1.0f, 0f, 1f);

            int i = (y * size + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = (byte)(alpha * 255);
        }

        return FromPixels(size, size, px);
    }

    /// <summary>
    /// Generates a ring (hollow circle) texture.
    /// </summary>
    public static Texture2D CreateRing(int size = 128, float thickness = 0.08f)
    {
        byte[] px = new byte[size * size * 4];
        float center = size / 2f;
        float outerR = size / 2f - 1.5f;
        float innerR = outerR * (1f - thickness);

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float outerAlpha = Math.Clamp(outerR - dist + 1.0f, 0f, 1f);
            float innerAlpha = Math.Clamp(dist - innerR + 1.0f, 0f, 1f);
            float alpha = outerAlpha * innerAlpha;

            int i = (y * size + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = (byte)(alpha * 255);
        }

        return FromPixels(size, size, px);
    }

    /// <summary>
    /// Generates a soft glow texture (for hit effects).
    /// </summary>
    public static Texture2D CreateGlow(int size = 128)
    {
        byte[] px = new byte[size * size * 4];
        float center = size / 2f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy) / center;
            float alpha = Math.Clamp(1f - dist * dist, 0f, 1f);
            alpha *= alpha; // Quadratic falloff for nice glow

            int i = (y * size + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = (byte)(alpha * 255);
        }

        return FromPixels(size, size, px);
    }

    /// <summary>
    /// Generates a rounded rectangle texture for drumrolls.
    /// Width is 4× height for a capsule shape.
    /// </summary>
    public static Texture2D CreateCapsule(int height = 64)
    {
        int width = height; // We'll stretch at draw time; texture is a circle-capped square
        byte[] px = new byte[width * height * 4];
        float cy = height / 2f;
        float cx = width / 2f;
        float radius = height / 2f - 1.5f;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = x - cx + 0.5f;
            float dy = y - cy + 0.5f;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float alpha = Math.Clamp(radius - dist + 1.0f, 0f, 1f);

            int i = (y * width + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = (byte)(alpha * 255);
        }

        return FromPixels(width, height, px);
    }
}

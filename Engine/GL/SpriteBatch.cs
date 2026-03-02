using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace TaikoNova.Engine.GL;

/// <summary>
/// Batched 2D sprite renderer. Accumulates quads and flushes them
/// to the GPU in minimal draw calls. Supports texture, tint, rotation.
/// </summary>
public sealed class SpriteBatch : IDisposable
{
    // Vertex layout: Position(2) + TexCoord(2) + Color(4) = 8 floats
    private const int FloatsPerVertex = 8;
    private const int VerticesPerQuad = 4;
    private const int IndicesPerQuad = 6;
    private const int MaxQuads = 2048;
    private const int MaxVertices = MaxQuads * VerticesPerQuad;
    private const int MaxIndices = MaxQuads * IndicesPerQuad;

    private readonly float[] _vertices = new float[MaxVertices * FloatsPerVertex];
    private int _quadCount;

    private readonly int _vao, _vbo, _ebo;
    private readonly Shader _shader;
    private Texture2D? _currentTexture;
    private Matrix4 _projection;
    private bool _begun;

    // Embedded shader sources
    private const string VertSrc = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;
out vec2 vUV;
out vec4 vColor;
uniform mat4 uProjection;
void main() {
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vUV = aUV;
    vColor = aColor;
}";

    private const string FragSrc = @"
#version 330 core
in vec2 vUV;
in vec4 vColor;
out vec4 FragColor;
uniform sampler2D uTexture;
void main() {
    FragColor = texture(uTexture, vUV) * vColor;
}";

    public SpriteBatch()
    {
        _shader = new Shader(VertSrc, FragSrc);

        // Generate index buffer (shared pattern for all quads)
        uint[] indices = new uint[MaxIndices];
        for (int i = 0, v = 0; i < MaxIndices; i += 6, v += 4)
        {
            indices[i + 0] = (uint)(v + 0);
            indices[i + 1] = (uint)(v + 1);
            indices[i + 2] = (uint)(v + 2);
            indices[i + 3] = (uint)(v + 2);
            indices[i + 4] = (uint)(v + 3);
            indices[i + 5] = (uint)(v + 0);
        }

        _vao = OpenTK.Graphics.OpenGL4.GL.GenVertexArray();
        _vbo = OpenTK.Graphics.OpenGL4.GL.GenBuffer();
        _ebo = OpenTK.Graphics.OpenGL4.GL.GenBuffer();

        OpenTK.Graphics.OpenGL4.GL.BindVertexArray(_vao);

        OpenTK.Graphics.OpenGL4.GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        OpenTK.Graphics.OpenGL4.GL.BufferData(BufferTarget.ArrayBuffer,
            _vertices.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        OpenTK.Graphics.OpenGL4.GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        OpenTK.Graphics.OpenGL4.GL.BufferData(BufferTarget.ElementArrayBuffer,
            indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        int stride = FloatsPerVertex * sizeof(float);
        // Position
        OpenTK.Graphics.OpenGL4.GL.EnableVertexAttribArray(0);
        OpenTK.Graphics.OpenGL4.GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        // TexCoord
        OpenTK.Graphics.OpenGL4.GL.EnableVertexAttribArray(1);
        OpenTK.Graphics.OpenGL4.GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
        // Color
        OpenTK.Graphics.OpenGL4.GL.EnableVertexAttribArray(2);
        OpenTK.Graphics.OpenGL4.GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));

        OpenTK.Graphics.OpenGL4.GL.BindVertexArray(0);
    }

    /// <summary>Begin a new batch with the given projection matrix.</summary>
    public void Begin(Matrix4 projection)
    {
        if (_begun) throw new InvalidOperationException("Already begun");
        _begun = true;
        _projection = projection;
        _quadCount = 0;
        _currentTexture = null;
    }

    /// <summary>Draw a textured, tinted quad.</summary>
    public void Draw(Texture2D texture, float x, float y, float w, float h,
        float r, float g, float b, float a, float rotation = 0f)
    {
        Draw(texture, x, y, w, h, 0, 0, 1, 1, r, g, b, a, rotation);
    }

    /// <summary>Draw a textured, tinted quad with source rectangle (UV).</summary>
    public void Draw(Texture2D texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1,
        float r, float g, float b, float a, float rotation = 0f)
    {
        if (!_begun) throw new InvalidOperationException("Call Begin first");

        // Flush if texture changes or buffer full
        if (_currentTexture != null && _currentTexture.Handle != texture.Handle)
            Flush();
        if (_quadCount >= MaxQuads)
            Flush();

        _currentTexture = texture;

        // Calculate corners
        float cx = x + w * 0.5f;
        float cy = y + h * 0.5f;
        float hw = w * 0.5f;
        float hh = h * 0.5f;

        float cos = 1f, sin = 0f;
        if (MathF.Abs(rotation) > 0.0001f)
        {
            cos = MathF.Cos(rotation);
            sin = MathF.Sin(rotation);
        }

        // Top-left, top-right, bottom-right, bottom-left
        Span<float> px = stackalloc float[8];
        float[] offsets = { -hw, -hh, hw, -hh, hw, hh, -hw, hh };
        for (int i = 0; i < 4; i++)
        {
            float ox = offsets[i * 2];
            float oy = offsets[i * 2 + 1];
            px[i * 2] = cx + ox * cos - oy * sin;
            px[i * 2 + 1] = cy + ox * sin + oy * cos;
        }

        int vi = _quadCount * VerticesPerQuad * FloatsPerVertex;

        void PutVertex(int idx, float vx, float vy, float vu, float vv)
        {
            int off = vi + idx * FloatsPerVertex;
            _vertices[off + 0] = vx;
            _vertices[off + 1] = vy;
            _vertices[off + 2] = vu;
            _vertices[off + 3] = vv;
            _vertices[off + 4] = r;
            _vertices[off + 5] = g;
            _vertices[off + 6] = b;
            _vertices[off + 7] = a;
        }

        PutVertex(0, px[0], px[1], u0, v0); // TL
        PutVertex(1, px[2], px[3], u1, v0); // TR
        PutVertex(2, px[4], px[5], u1, v1); // BR
        PutVertex(3, px[6], px[7], u0, v1); // BL

        _quadCount++;
    }

    /// <summary>Draw with float[] color arrays (convenience for SkinConfig colors).</summary>
    public void Draw(Texture2D texture, float x, float y, float w, float h, float[] color, float rotation = 0f)
    {
        Draw(texture, x, y, w, h, color[0], color[1], color[2], color[3], rotation);
    }

    /// <summary>End the batch and flush remaining quads.</summary>
    public void End()
    {
        if (!_begun) throw new InvalidOperationException("Not begun");
        Flush();
        _begun = false;
    }

    private void Flush()
    {
        if (_quadCount == 0 || _currentTexture == null) return;

        _shader.Use();
        _shader.SetMatrix4("uProjection", _projection);
        _shader.SetInt("uTexture", 0);

        _currentTexture.Bind(TextureUnit.Texture0);

        OpenTK.Graphics.OpenGL4.GL.BindVertexArray(_vao);

        // Upload vertex data
        int dataSize = _quadCount * VerticesPerQuad * FloatsPerVertex * sizeof(float);
        OpenTK.Graphics.OpenGL4.GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        OpenTK.Graphics.OpenGL4.GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, dataSize, _vertices);

        // Draw
        OpenTK.Graphics.OpenGL4.GL.DrawElements(PrimitiveType.Triangles,
            _quadCount * IndicesPerQuad, DrawElementsType.UnsignedInt, 0);

        OpenTK.Graphics.OpenGL4.GL.BindVertexArray(0);

        _quadCount = 0;
    }

    public void Dispose()
    {
        _shader.Dispose();
        OpenTK.Graphics.OpenGL4.GL.DeleteVertexArray(_vao);
        OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(_vbo);
        OpenTK.Graphics.OpenGL4.GL.DeleteBuffer(_ebo);
    }
}

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace TaikoNova.Engine.GL;

/// <summary>
/// OpenGL shader program wrapper — compiles, links, caches uniforms.
/// </summary>
public sealed class Shader : IDisposable
{
    public int Handle { get; }

    private readonly Dictionary<string, int> _uniformCache = new();

    public Shader(string vertexSource, string fragmentSource)
    {
        int vs = CompileStage(ShaderType.VertexShader, vertexSource);
        int fs = CompileStage(ShaderType.FragmentShader, fragmentSource);

        Handle = OpenTK.Graphics.OpenGL4.GL.CreateProgram();
        OpenTK.Graphics.OpenGL4.GL.AttachShader(Handle, vs);
        OpenTK.Graphics.OpenGL4.GL.AttachShader(Handle, fs);
        OpenTK.Graphics.OpenGL4.GL.LinkProgram(Handle);

        OpenTK.Graphics.OpenGL4.GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
        {
            string log = OpenTK.Graphics.OpenGL4.GL.GetProgramInfoLog(Handle);
            throw new Exception($"Shader link error: {log}");
        }

        OpenTK.Graphics.OpenGL4.GL.DetachShader(Handle, vs);
        OpenTK.Graphics.OpenGL4.GL.DetachShader(Handle, fs);
        OpenTK.Graphics.OpenGL4.GL.DeleteShader(vs);
        OpenTK.Graphics.OpenGL4.GL.DeleteShader(fs);
    }

    public void Use() => OpenTK.Graphics.OpenGL4.GL.UseProgram(Handle);

    // ── Uniform setters ──

    public void SetInt(string name, int value)
    {
        OpenTK.Graphics.OpenGL4.GL.Uniform1(GetLocation(name), value);
    }

    public void SetFloat(string name, float value)
    {
        OpenTK.Graphics.OpenGL4.GL.Uniform1(GetLocation(name), value);
    }

    public void SetVector2(string name, Vector2 v)
    {
        OpenTK.Graphics.OpenGL4.GL.Uniform2(GetLocation(name), v);
    }

    public void SetVector4(string name, Vector4 v)
    {
        OpenTK.Graphics.OpenGL4.GL.Uniform4(GetLocation(name), v);
    }

    public void SetMatrix4(string name, Matrix4 m)
    {
        OpenTK.Graphics.OpenGL4.GL.UniformMatrix4(GetLocation(name), false, ref m);
    }

    private int GetLocation(string name)
    {
        if (_uniformCache.TryGetValue(name, out int loc))
            return loc;

        loc = OpenTK.Graphics.OpenGL4.GL.GetUniformLocation(Handle, name);
        _uniformCache[name] = loc;
        return loc;
    }

    private static int CompileStage(ShaderType type, string source)
    {
        int shader = OpenTK.Graphics.OpenGL4.GL.CreateShader(type);
        OpenTK.Graphics.OpenGL4.GL.ShaderSource(shader, source);
        OpenTK.Graphics.OpenGL4.GL.CompileShader(shader);

        OpenTK.Graphics.OpenGL4.GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            string log = OpenTK.Graphics.OpenGL4.GL.GetShaderInfoLog(shader);
            throw new Exception($"{type} compile error: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        OpenTK.Graphics.OpenGL4.GL.DeleteProgram(Handle);
    }
}

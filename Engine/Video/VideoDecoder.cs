using System.Diagnostics;

namespace TaikoNova.Engine.Video;

/// <summary>
/// Decodes video files by piping raw RGBA frames from FFmpeg.
/// Runs FFmpeg as a subprocess and reads raw pixel data from stdout.
/// No native bindings needed — just requires FFmpeg on PATH.
/// </summary>
public sealed class VideoDecoder : IDisposable
{
    private Process? _ffmpeg;
    private Stream? _stdout;
    private byte[]? _frameBuffer;
    private bool _disposed;
    private bool _eof;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public double Fps { get; private set; }
    public double FrameDuration { get; private set; } // ms per frame
    public bool IsOpen => _ffmpeg != null && !_eof;

    // Track which frame we last decoded
    private int _frameIndex;
    public int FrameIndex => _frameIndex;

    /// <summary>
    /// Open a video file for decoding. Scales to the given resolution.
    /// Returns false if FFmpeg is not available or the file can't be opened.
    /// </summary>
    public bool Open(string path, int targetWidth, int targetHeight, double fps = 30)
    {
        Close();

        if (!File.Exists(path))
        {
            Console.WriteLine($"[Video] File not found: {path}");
            return false;
        }

        // Find FFmpeg
        string? ffmpegPath = FindFFmpeg();
        if (ffmpegPath == null)
        {
            Console.WriteLine("[Video] FFmpeg not found on PATH. Video backgrounds disabled.");
            Console.WriteLine("[Video] Install FFmpeg: brew install ffmpeg (macOS) / apt install ffmpeg (Linux)");
            return false;
        }

        Width = targetWidth;
        Height = targetHeight;
        Fps = fps;
        FrameDuration = 1000.0 / fps;
        _frameBuffer = new byte[Width * Height * 4]; // RGBA
        _frameIndex = 0;
        _eof = false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{path}\" " +
                            $"-vf \"scale={Width}:{Height}:force_original_aspect_ratio=decrease," +
                            $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black\" " +
                            $"-r {fps} " +
                            "-pix_fmt rgba " +
                            "-f rawvideo " +
                            "-loglevel error " +
                            "-",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _ffmpeg = Process.Start(psi);
            if (_ffmpeg == null)
            {
                Console.WriteLine("[Video] Failed to start FFmpeg process");
                return false;
            }

            _stdout = _ffmpeg.StandardOutput.BaseStream;

            // Drain stderr asynchronously to prevent deadlocks
            _ffmpeg.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[Video/FFmpeg] {e.Data}");
            };
            _ffmpeg.BeginErrorReadLine();

            Console.WriteLine($"[Video] Opened: {Path.GetFileName(path)} ({Width}x{Height} @ {fps}fps)");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Video] Failed to open: {ex.Message}");
            Close();
            return false;
        }
    }

    /// <summary>
    /// Read the next frame into the internal buffer.
    /// Returns the buffer, or null if EOF / error.
    /// </summary>
    public byte[]? ReadFrame()
    {
        if (_stdout == null || _frameBuffer == null || _eof)
            return null;

        try
        {
            int totalRead = 0;
            int remaining = _frameBuffer.Length;

            while (remaining > 0)
            {
                int read = _stdout.Read(_frameBuffer, totalRead, remaining);
                if (read == 0)
                {
                    _eof = true;
                    return null; // EOF
                }
                totalRead += read;
                remaining -= read;
            }

            _frameIndex++;
            return _frameBuffer;
        }
        catch
        {
            _eof = true;
            return null;
        }
    }

    /// <summary>
    /// Seek to a specific time in the video.
    /// This restarts the FFmpeg process with a seek parameter.
    /// Expensive — only use for large jumps.
    /// </summary>
    public bool Seek(string path, double timeMs, int targetWidth, int targetHeight, double fps = 30)
    {
        Close();

        string? ffmpegPath = FindFFmpeg();
        if (ffmpegPath == null) return false;

        Width = targetWidth;
        Height = targetHeight;
        Fps = fps;
        FrameDuration = 1000.0 / fps;
        _frameBuffer = new byte[Width * Height * 4];
        _frameIndex = (int)(timeMs / FrameDuration);
        _eof = false;

        try
        {
            double seekSec = timeMs / 1000.0;
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-ss {seekSec:F3} -i \"{path}\" " +
                            $"-vf \"scale={Width}:{Height}:force_original_aspect_ratio=decrease," +
                            $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black\" " +
                            $"-r {fps} " +
                            "-pix_fmt rgba " +
                            "-f rawvideo " +
                            "-loglevel error " +
                            "-",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _ffmpeg = Process.Start(psi);
            if (_ffmpeg == null) return false;

            _stdout = _ffmpeg.StandardOutput.BaseStream;
            _ffmpeg.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[Video/FFmpeg] {e.Data}");
            };
            _ffmpeg.BeginErrorReadLine();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Video] Seek failed: {ex.Message}");
            Close();
            return false;
        }
    }

    public void Close()
    {
        if (_ffmpeg != null)
        {
            try
            {
                if (!_ffmpeg.HasExited)
                    _ffmpeg.Kill();
                _ffmpeg.Dispose();
            }
            catch { }
            _ffmpeg = null;
        }
        _stdout = null;
        _eof = true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Close();
            _disposed = true;
        }
    }

    /// <summary>Find FFmpeg executable on PATH.</summary>
    private static string? FindFFmpeg()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        // Check PATH
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar != null)
        {
            char sep = OperatingSystem.IsWindows() ? ';' : ':';
            foreach (string dir in pathVar.Split(sep))
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        // Common locations
        string[] common = OperatingSystem.IsWindows()
            ? new[] { @"C:\ffmpeg\bin\ffmpeg.exe", @"C:\Program Files\ffmpeg\bin\ffmpeg.exe" }
            : new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" };

        foreach (string c in common)
            if (File.Exists(c)) return c;

        return null;
    }
}

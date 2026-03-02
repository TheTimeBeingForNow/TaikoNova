using ManagedBass;

namespace TaikoNova.Engine.Audio;

/// <summary>
/// Cross-platform audio playback using BASS (via ManagedBass).
/// BASS handles decoding (MP3, OGG, WAV, AIFF, etc.) and output natively on
/// macOS, Windows, and Linux — no separate decoder or output layer needed.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private int _musicStream;
    private bool _bassReady;
    private float _musicVolume = 1.0f;

    /// <summary>Current music playback position in milliseconds.</summary>
    public double MusicPosition
    {
        get
        {
            if (_musicStream == 0) return 0;
            long pos = Bass.ChannelGetPosition(_musicStream);
            return Bass.ChannelBytes2Seconds(_musicStream, pos) * 1000.0;
        }
    }

    /// <summary>Total music duration in milliseconds.</summary>
    public double MusicDuration
    {
        get
        {
            if (_musicStream == 0) return 0;
            long len = Bass.ChannelGetLength(_musicStream);
            return Bass.ChannelBytes2Seconds(_musicStream, len) * 1000.0;
        }
    }

    /// <summary>Whether music is currently playing.</summary>
    public bool IsPlaying
    {
        get
        {
            if (_musicStream == 0) return false;
            return Bass.ChannelIsActive(_musicStream) == PlaybackState.Playing;
        }
    }

    /// <summary>Whether music has been loaded.</summary>
    public bool IsMusicLoaded => _musicStream != 0;

    public AudioManager()
    {
        InitBass();
    }

    private void InitBass()
    {
        try
        {
            // Auto-download the native BASS library if not present
            if (!BassNativeLoader.EnsureNativeLibrary())
            {
                Console.WriteLine("[Audio] Could not obtain BASS native library");
                return;
            }

            bool ok = Bass.Init(-1, 44100, DeviceInitFlags.Default);
            if (!ok)
            {
                var err = Bass.LastError;
                Console.WriteLine($"[Audio] Bass.Init failed: {err}");
                return;
            }

            _bassReady = true;
            Console.WriteLine("[Audio] BASS initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Audio] BASS init failed: {ex.Message}");
            _bassReady = false;
        }
    }

    /// <summary>Load a music file (mp3, wav, ogg, aiff, etc.).</summary>
    public bool LoadMusic(string path)
    {
        StopMusic();

        if (!_bassReady)
        {
            Console.WriteLine("[Audio] BASS not available — cannot play audio");
            return false;
        }

        try
        {
            int stream = Bass.CreateStream(path, 0, 0, BassFlags.Default);
            if (stream == 0)
            {
                var err = Bass.LastError;
                Console.WriteLine($"[Audio] Failed to create stream for '{Path.GetFileName(path)}': {err}");
                return false;
            }

            _musicStream = stream;
            Bass.ChannelSetAttribute(_musicStream, ChannelAttribute.Volume, _musicVolume);

            Console.WriteLine($"[Audio] Loaded: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Audio] Failed to load '{Path.GetFileName(path)}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Start or resume music playback.</summary>
    public void PlayMusic()
    {
        if (!_bassReady || _musicStream == 0) return;
        Bass.ChannelPlay(_musicStream, false);
    }

    /// <summary>Pause music.</summary>
    public void PauseMusic()
    {
        if (!_bassReady || _musicStream == 0) return;
        Bass.ChannelPause(_musicStream);
    }

    /// <summary>Stop and release the current music stream.</summary>
    public void StopMusic()
    {
        if (_musicStream != 0)
        {
            Bass.ChannelStop(_musicStream);
            Bass.StreamFree(_musicStream);
            _musicStream = 0;
        }
    }

    /// <summary>Seek to a position in ms.</summary>
    public void SeekMusic(double ms)
    {
        if (_musicStream == 0) return;
        double seconds = Math.Max(0, ms) / 1000.0;
        long bytePos = Bass.ChannelSeconds2Bytes(_musicStream, seconds);
        Bass.ChannelSetPosition(_musicStream, bytePos);
    }

    /// <summary>Set music volume (0.0 – 1.0).</summary>
    public void SetMusicVolume(float volume)
    {
        _musicVolume = Math.Clamp(volume, 0f, 1f);
        if (_musicStream != 0)
            Bass.ChannelSetAttribute(_musicStream, ChannelAttribute.Volume, _musicVolume);
    }

    /// <summary>
    /// Play a short sound effect (fire and forget).
    /// For hit sounds — don, kat, etc.
    /// </summary>
    public void PlaySfx(string path, float volume = 0.5f)
    {
        if (!_bassReady) return;

        try
        {
            // AutoFree flag makes BASS free the stream automatically when playback ends
            int stream = Bass.CreateStream(path, 0, 0, BassFlags.AutoFree);
            if (stream == 0) return;

            Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, volume);
            Bass.ChannelPlay(stream, false);
        }
        catch { /* Silently ignore SFX errors */ }
    }

    public void Dispose()
    {
        StopMusic();
        if (_bassReady)
        {
            Bass.Free();
            _bassReady = false;
        }
    }
}

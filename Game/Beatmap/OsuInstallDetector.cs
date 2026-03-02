using System.Runtime.InteropServices;

namespace TaikoNova.Game.Beatmap;

/// <summary>
/// Detects osu! stable and lazer installations on the user's system.
/// Returns directories/files that can be scanned for beatmaps.
/// </summary>
public static class OsuInstallDetector
{
    /// <summary>
    /// Finds all osu! stable "Songs" directories on this machine.
    /// Checks Windows registry first, then well-known fallback paths.
    /// </summary>
    public static List<string> FindStableSongsPaths()
    {
        var found = new List<string>();

        if (!OperatingSystem.IsWindows()) return found;

        // ── 1. Windows Registry ──
        // osu! stable stores its install path at HKCU\Software\osu!
        try
        {
            string? regPath = ReadRegistryStablePath();
            if (regPath != null)
            {
                string songsDir = ResolveSongsDir(regPath);
                if (Directory.Exists(songsDir))
                {
                    Console.WriteLine($"[OsuDetect] stable (registry): {songsDir}");
                    found.Add(songsDir);
                }
            }
        }
        catch { /* registry access can fail */ }

        // ── 2. Well-known fallback locations ──
        string[] fallbacks =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!"),
            @"C:\osu!",
            @"D:\osu!",
            @"E:\osu!",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "osu!"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "osu!"),
        };

        foreach (string installDir in fallbacks)
        {
            if (!Directory.Exists(installDir)) continue;

            string songsDir = ResolveSongsDir(installDir);
            if (Directory.Exists(songsDir) && !found.Any(p =>
                    string.Equals(Path.GetFullPath(p), Path.GetFullPath(songsDir),
                        StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"[OsuDetect] stable (fallback): {songsDir}");
                found.Add(songsDir);
            }
        }

        return found;
    }

    /// <summary>
    /// Finds osu! lazer's data directory and scans the hashed file store
    /// for .osu files. Returns individual file paths that can be parsed.
    /// Lazer stores ALL files (audio, images, .osu) as hash-named blobs.
    /// </summary>
    public static List<string> FindLazerOsuFiles()
    {
        var files = new List<string>();
        string? filesDir = FindLazerFilesDir();
        if (filesDir == null) return files;

        Console.WriteLine($"[OsuDetect] Scanning lazer hash store: {filesDir}");
        int scanned = 0;
        const int maxScan = 80_000; // safety limit

        try
        {
            foreach (string file in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
            {
                if (++scanned > maxScan) break;

                try
                {
                    // Quick size filter: .osu files are typically 500 bytes – 300 KB
                    long len = new FileInfo(file).Length;
                    if (len < 200 || len > 400_000) continue;

                    // Sniff first line — only read minimal bytes
                    string? firstLine = ReadFirstLine(file);
                    if (firstLine != null && firstLine.StartsWith("osu file format", StringComparison.Ordinal))
                    {
                        files.Add(file);
                    }
                }
                catch { /* skip unreadable files */ }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OsuDetect] Lazer scan error: {ex.Message}");
        }

        Console.WriteLine($"[OsuDetect] Lazer: scanned {scanned} files, found {files.Count} .osu files");
        return files;
    }

    // ═══════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Read osu! stable install path from Windows registry.
    /// </summary>
    private static string? ReadRegistryStablePath()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // Use reflection to access Registry to avoid compile-time platform dependency
        try
        {
            var regType = Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry")
                       ?? Type.GetType("Microsoft.Win32.Registry, mscorlib");

            if (regType == null) return null;

            var hkcu = regType.GetField("CurrentUser")?.GetValue(null);
            if (hkcu == null) return null;

            var openSubKey = hkcu.GetType().GetMethod("OpenSubKey", new[] { typeof(string) });
            if (openSubKey == null) return null;

            using var key = openSubKey.Invoke(hkcu, new object[] { @"Software\osu!" }) as IDisposable;
            if (key == null) return null;

            var getValue = key.GetType().GetMethod("GetValue", new[] { typeof(string) });
            return getValue?.Invoke(key, new object[] { "" }) as string; // Default value = install path
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the actual Songs directory for a stable installation,
    /// checking osu!.*.cfg files for a custom BeatmapDirectory setting.
    /// </summary>
    private static string ResolveSongsDir(string installDir)
    {
        // Check .cfg files for a custom BeatmapDirectory
        try
        {
            foreach (string cfg in Directory.GetFiles(installDir, "osu!.*.cfg"))
            {
                foreach (string line in File.ReadLines(cfg))
                {
                    if (line.StartsWith("BeatmapDirectory", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            string dir = parts[1].Trim();
                            if (dir.Length > 0)
                            {
                                // Can be absolute or relative
                                string resolved = Path.IsPathRooted(dir)
                                    ? dir
                                    : Path.Combine(installDir, dir);
                                if (Directory.Exists(resolved))
                                    return resolved;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // Default
        return Path.Combine(installDir, "Songs");
    }

    /// <summary>
    /// Finds osu! lazer's data/files directory.
    /// Lazer stores data at %APPDATA%/osu (Windows).
    /// Respects storage.ini redirect if present.
    /// </summary>
    private static string? FindLazerFilesDir()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string lazerRoot = Path.Combine(appData, "osu");

        // Check storage.ini for custom path redirect
        string storageIni = Path.Combine(lazerRoot, "storage.ini");
        if (File.Exists(storageIni))
        {
            try
            {
                string customPath = File.ReadAllText(storageIni).Trim();
                if (customPath.Length > 0 && Directory.Exists(customPath))
                {
                    string redirected = Path.Combine(customPath, "files");
                    if (Directory.Exists(redirected))
                    {
                        Console.WriteLine($"[OsuDetect] Lazer storage redirect: {redirected}");
                        return redirected;
                    }
                }
            }
            catch { }
        }

        string filesDir = Path.Combine(lazerRoot, "files");
        if (Directory.Exists(filesDir))
            return filesDir;

        return null;
    }

    /// <summary>
    /// Efficiently reads just the first line of a file.
    /// </summary>
    private static string? ReadFirstLine(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256);
        using var sr = new StreamReader(fs);
        return sr.ReadLine();
    }
}

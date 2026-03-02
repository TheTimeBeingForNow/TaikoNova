using System.IO.Compression;
using System.Runtime.InteropServices;

namespace TaikoNova.Engine.Audio;

/// <summary>
/// Automatically downloads the correct BASS native library for the current
/// platform if it isn't already present next to the executable.
/// Downloads from un4seen.com (the official BASS distribution site).
/// </summary>
public static class BassNativeLoader
{
    private const string BassVersion = "24"; // BASS 2.4

    /// <summary>
    /// Ensures the BASS native library is present in the application directory.
    /// Downloads and extracts it if missing. Returns true if the library is ready.
    /// </summary>
    public static bool EnsureNativeLibrary()
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string libName = GetNativeLibraryName();
        string libPath = Path.Combine(appDir, libName);

        if (File.Exists(libPath))
        {
            Console.WriteLine($"[BASS] Native library found: {libName}");
            return true;
        }

        Console.WriteLine($"[BASS] Native library '{libName}' not found — downloading...");

        try
        {
            DownloadAndExtract(appDir, libName);
            if (File.Exists(libPath))
            {
                Console.WriteLine($"[BASS] Successfully downloaded {libName}");
                return true;
            }

            Console.WriteLine($"[BASS] Download completed but {libName} not found");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Failed to download native library: {ex.Message}");
            return false;
        }
    }

    private static string GetNativeLibraryName()
    {
        if (OperatingSystem.IsWindows()) return "bass.dll";
        if (OperatingSystem.IsLinux()) return "libbass.so";
        if (OperatingSystem.IsMacOS()) return "libbass.dylib";
        throw new PlatformNotSupportedException("Unsupported operating system");
    }

    private static (string url, string entryPath) GetDownloadInfo()
    {
        string baseUrl = $"https://www.un4seen.com/files/bass{BassVersion}";

        if (OperatingSystem.IsWindows())
        {
            string url = $"{baseUrl}.zip";
            // x64 DLL is under x64/ in the zip, x86 is at root
            string entry = RuntimeInformation.OSArchitecture == Architecture.X64
                ? "x64/bass.dll"
                : "bass.dll";
            return (url, entry);
        }

        if (OperatingSystem.IsLinux())
        {
            string url = $"{baseUrl}-linux.zip";
            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                Architecture.Arm => "armhf",
                Architecture.X86 => "x86",
                _ => "x86_64"
            };
            return (url, $"libs/{arch}/libbass.so");
        }

        if (OperatingSystem.IsMacOS())
        {
            // macOS zip has libbass.dylib at the root (universal binary: x86_64 + arm64)
            string url = $"{baseUrl}-osx.zip";
            return (url, "libbass.dylib");
        }

        throw new PlatformNotSupportedException("Unsupported operating system");
    }

    private static void DownloadAndExtract(string targetDir, string libName)
    {
        var (url, entryPath) = GetDownloadInfo();
        string tempZip = Path.Combine(Path.GetTempPath(), $"bass_{Guid.NewGuid():N}.zip");

        try
        {
            Console.WriteLine($"[BASS] Downloading from {url} ...");

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                // un4seen.com may require a browser-like User-Agent
                http.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (compatible; TaikoNova/1.0)");

                using var response = http.GetAsync(url).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                using var fs = File.Create(tempZip);
                response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
            }

            Console.WriteLine($"[BASS] Extracting '{entryPath}' ...");

            using var zip = ZipFile.OpenRead(tempZip);

            // Try exact path first, then case-insensitive search
            var entry = zip.GetEntry(entryPath)
                ?? zip.Entries.FirstOrDefault(e =>
                    e.FullName.Equals(entryPath, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                // Fallback: look for any file matching the library name
                entry = zip.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals(libName, StringComparison.OrdinalIgnoreCase)
                    && e.Length > 0);
            }

            if (entry == null)
            {
                Console.WriteLine($"[BASS] Could not find '{entryPath}' in the archive");
                Console.WriteLine("[BASS] Archive contents:");
                foreach (var e in zip.Entries.Where(e => e.Length > 0).Take(20))
                    Console.WriteLine($"  {e.FullName} ({e.Length} bytes)");
                return;
            }

            string destPath = Path.Combine(targetDir, libName);
            entry.ExtractToFile(destPath, overwrite: true);

            // On Unix-like systems, ensure the library is executable
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(destPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { /* Best effort */ }
            }
        }
        finally
        {
            // Clean up temp zip
            try { File.Delete(tempZip); } catch { }
        }
    }
}

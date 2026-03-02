namespace TaikoNova.Game.Beatmap;

/// <summary>
/// Resolves audio files for beatmaps imported from osu! lazer's hash based file store.
///
/// osu! lazer stores ALL files (audio, images, .osu) as SHA256-hashed blobs in a flat
/// "files/{hash[0..1]}/{hash}" structure. The original filenames are only stored in the
/// Realm database (client.realm). Without reading Realm, we can't reliably map audio
/// filenames to their hashed paths.
///
/// This resolver uses a heuristic approach:
///   1. Read the client.realm binary to find the .osu file's hash
///   2. Search nearby bytes for other hash strings that belong to the same beatmap set
///   3. Check those hashes against the file store, looking for audio files by magic bytes
/// </summary>
public static class LazerAudioResolver
{
    /// <summary>
    /// Known audio file magic bytes for quick identification.
    /// </summary>
    private static readonly (byte[] magic, string format)[] AudioMagicBytes =
    {
        (new byte[] { 0x49, 0x44, 0x33 },             "MP3 (ID3)"),     // ID3 tag header
        (new byte[] { 0xFF, 0xFB },                    "MP3 (raw)"),     // MPEG sync word
        (new byte[] { 0xFF, 0xF3 },                    "MP3 (raw)"),     // MPEG sync word variant
        (new byte[] { 0xFF, 0xF2 },                    "MP3 (raw)"),     // MPEG sync word variant
        (new byte[] { 0x4F, 0x67, 0x67, 0x53 },       "OGG"),           // "OggS"
        (new byte[] { 0x52, 0x49, 0x46, 0x46 },       "WAV"),           // "RIFF"
        (new byte[] { 0x66, 0x4C, 0x61, 0x43 },       "FLAC"),          // "fLaC"
    };

    /// <summary>
    /// Attempt to find the audio file for a lazer beatmap.
    /// Returns the full path if found, or null if resolution fails.
    /// </summary>
    /// <param name="osuFilePath">Path to the .osu file in the lazer hash store</param>
    /// <param name="audioFilename">Original audio filename from the .osu file (e.g. "audio.mp3")</param>
    /// <returns>Resolved audio file path, or null</returns>
    public static string? ResolveAudio(string osuFilePath, string audioFilename)
    {
        try
        {
            // Step 1: Determine the lazer files directory
            string? filesDir = FindFilesDirectory(osuFilePath);
            if (filesDir == null)
            {
                Console.WriteLine("[LazerAudio] Could not determine lazer files directory");
                return null;
            }

            // Step 2: Get the .osu file's hash (its filename in the store)
            string osuHash = Path.GetFileName(osuFilePath);
            Console.WriteLine($"[LazerAudio] Resolving audio for .osu hash: {osuHash}");

            // Step 3: Try to find associated audio hash from client.realm
            string? lazerRoot = FindLazerRoot(filesDir);
            if (lazerRoot != null)
            {
                string realmPath = Path.Combine(lazerRoot, "client.realm");
                if (File.Exists(realmPath))
                {
                    string? audioHash = SearchRealmForAudioHash(realmPath, osuHash, audioFilename);
                    if (audioHash != null)
                    {
                        string audioPath = HashToPath(filesDir, audioHash);
                        if (File.Exists(audioPath))
                        {
                            Console.WriteLine($"[LazerAudio] Resolved audio via realm: {audioHash}");
                            return audioPath;
                        }
                    }
                }
            }

            // Step 4: Fallback — scan the hash store for audio files near the .osu hash
            Console.WriteLine("[LazerAudio] Realm lookup failed, trying heuristic scan...");
            return HeuristicAudioSearch(filesDir, osuHash, audioFilename);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LazerAudio] Resolution failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if a file path appears to be inside a lazer hash store.
    /// </summary>
    public static bool IsLazerPath(string filePath)
    {
        // Lazer paths look like: .../files/ab/abcdef01234567890...
        // The parent directory is a 2-char hex prefix
        string? dir = Path.GetDirectoryName(filePath);
        if (dir == null) return false;

        string dirName = Path.GetFileName(dir);
        if (dirName.Length != 2) return false;

        // Check if the directory name is a valid hex prefix
        if (!IsHexString(dirName)) return false;

        // Check if the grandparent is named "files"
        string? grandParent = Path.GetDirectoryName(dir);
        if (grandParent == null) return false;
        string grandParentName = Path.GetFileName(grandParent);

        return string.Equals(grandParentName, "files", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get the lazer files/ directory from a file path inside it.
    /// </summary>
    private static string? FindFilesDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath); // e.g., files/ab
        if (dir == null) return null;

        string? filesDir = Path.GetDirectoryName(dir); // e.g., files
        if (filesDir == null) return null;

        if (string.Equals(Path.GetFileName(filesDir), "files", StringComparison.OrdinalIgnoreCase))
            return filesDir;

        return null;
    }

    /// <summary>
    /// Get the lazer root directory (parent of files/).
    /// </summary>
    private static string? FindLazerRoot(string filesDir)
    {
        return Path.GetDirectoryName(filesDir);
    }

    /// <summary>
    /// Search the client.realm binary for the audio file hash associated with the .osu file.
    /// 
    /// Realm stores strings inline in its binary format. We search for the .osu file's
    /// hash string and look for other hex hash strings nearby that might be the audio file.
    /// This is a heuristic — it won't always work, but it's the best we can do without
    /// the Realm SDK.
    /// </summary>
    private static string? SearchRealmForAudioHash(string realmPath, string osuHash, string audioFilename)
    {
        try
        {
            byte[] realmData = File.ReadAllBytes(realmPath);
            byte[] searchBytes = System.Text.Encoding.ASCII.GetBytes(osuHash);

            // Find all occurrences of the .osu hash in the realm file
            var positions = FindAllOccurrences(realmData, searchBytes);
            if (positions.Count == 0)
            {
                Console.WriteLine("[LazerAudio] .osu hash not found in realm database");
                return null;
            }

            // Get the audio filename extension to help identify the right file
            string audioExt = Path.GetExtension(audioFilename).ToLowerInvariant();

            // Search for the audio filename near the .osu hash position
            byte[] audioNameBytes = System.Text.Encoding.UTF8.GetBytes(audioFilename);

            foreach (int pos in positions)
            {
                // Search within ±8KB of the .osu hash for the audio filename
                int searchStart = Math.Max(0, pos - 8192);
                int searchEnd = Math.Min(realmData.Length, pos + 8192);

                // Look for the audio filename string
                var audioNamePositions = FindAllOccurrences(realmData, audioNameBytes, searchStart, searchEnd);

                foreach (int audioNamePos in audioNamePositions)
                {
                    // Now search near the audio filename for SHA256-like hash strings (64 hex chars)
                    int hashSearchStart = Math.Max(0, audioNamePos - 4096);
                    int hashSearchEnd = Math.Min(realmData.Length, audioNamePos + 4096);

                    var candidateHashes = FindHexStrings(realmData, hashSearchStart, hashSearchEnd, 32);

                    foreach (string hash in candidateHashes)
                    {
                        if (hash == osuHash) continue; // Skip the .osu file's own hash

                        // Check if this hash exists in the file store and is an audio file
                        string? filesDir = Path.GetDirectoryName(Path.GetDirectoryName(
                            Path.Combine(Path.GetDirectoryName(realmPath)!, "files", hash[..2], hash)));

                        // Reconstruct files dir
                        string lazerRoot = Path.GetDirectoryName(realmPath)!;
                        string candidatePath = Path.Combine(lazerRoot, "files", hash[..2], hash);

                        if (File.Exists(candidatePath) && IsAudioFile(candidatePath))
                        {
                            return hash;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LazerAudio] Realm binary search failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Fallback heuristic: scan the hash store for audio files.
    /// Limited scan to avoid being too slow.
    /// </summary>
    private static string? HeuristicAudioSearch(string filesDir, string osuHash, string audioFilename)
    {
        // This is a brute-force fallback — scan a limited number of files
        // looking for audio by magic bytes
        int scanned = 0;
        const int maxScan = 5000;
        var audioFiles = new List<string>();

        try
        {
            foreach (string file in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
            {
                if (++scanned > maxScan) break;

                try
                {
                    var fi = new FileInfo(file);
                    // Audio files are typically 500KB - 50MB
                    if (fi.Length < 100_000 || fi.Length > 100_000_000) continue;

                    if (IsAudioFile(file))
                    {
                        audioFiles.Add(file);
                    }
                }
                catch { }
            }
        }
        catch { }

        if (audioFiles.Count == 0)
        {
            Console.WriteLine($"[LazerAudio] No audio files found in {scanned} scanned files");
            return null;
        }

        Console.WriteLine($"[LazerAudio] Found {audioFiles.Count} audio file(s) in {scanned} scanned files");

        // If we only found one audio file with the right prefix, use it
        // This won't reliably find the RIGHT audio, but it's better than nothing
        // for small lazer installations
        if (audioFiles.Count == 1)
        {
            Console.WriteLine("[LazerAudio] Single audio file found — using it");
            return audioFiles[0];
        }

        // Too many candidates, can't determine which one is correct
        Console.WriteLine("[LazerAudio] Multiple audio files found — cannot determine correct one");
        Console.WriteLine("[LazerAudio] Tip: Export the beatmap from osu! lazer as .osz and place it in the Songs folder");
        return null;
    }

    /// <summary>
    /// Convert a file hash to its path in the lazer file store.
    /// </summary>
    private static string HashToPath(string filesDir, string hash)
    {
        return Path.Combine(filesDir, hash[..2], hash);
    }

    /// <summary>
    /// Check if a file starts with known audio magic bytes.
    /// </summary>
    private static bool IsAudioFile(string path)
    {
        try
        {
            byte[] header = new byte[8];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16);
            int read = fs.Read(header, 0, header.Length);
            if (read < 2) return false;

            foreach (var (magic, _) in AudioMagicBytes)
            {
                if (read >= magic.Length && header.AsSpan(0, magic.Length).SequenceEqual(magic))
                    return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>Find all byte pattern occurrences in data.</summary>
    private static List<int> FindAllOccurrences(byte[] data, byte[] pattern, int start = 0, int end = -1)
    {
        var results = new List<int>();
        if (end < 0) end = data.Length;

        for (int i = start; i <= end - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                results.Add(i);
        }
        return results;
    }

    /// <summary>
    /// Find sequences of hex characters of a specific length (likely SHA256 hashes).
    /// </summary>
    private static List<string> FindHexStrings(byte[] data, int start, int end, int minHexPairs)
    {
        var results = new List<string>();
        int targetLen = minHexPairs * 2; // SHA256 = 32 bytes = 64 hex chars
        int i = start;

        while (i <= end - targetLen)
        {
            if (IsHexChar(data[i]))
            {
                int runStart = i;
                while (i < end && i - runStart < targetLen + 10 && IsHexChar(data[i]))
                    i++;

                int runLen = i - runStart;
                if (runLen >= targetLen)
                {
                    string candidate = System.Text.Encoding.ASCII.GetString(data, runStart, targetLen);
                    if (!results.Contains(candidate))
                        results.Add(candidate);
                }
            }
            else
            {
                i++;
            }
        }

        return results;
    }

    private static bool IsHexChar(byte b) =>
        (b >= (byte)'0' && b <= (byte)'9') ||
        (b >= (byte)'a' && b <= (byte)'f') ||
        (b >= (byte)'A' && b <= (byte)'F');

    private static bool IsHexString(string s)
    {
        foreach (char c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }
}

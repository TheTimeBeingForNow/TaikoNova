namespace TaikoNova.Game.Beatmap;

/// <summary>
/// General-purpose file resolver for osu! lazer's hash-based file store.
/// Given a .osu file from the hash store and a filename (e.g. "bg.jpg"),
/// attempts to find the hashed blob in the store.
///
/// Uses the same realm-binary-search heuristic as LazerAudioResolver.
/// </summary>
public static class LazerFileResolver
{
    /// <summary>
    /// Resolve any file associated with a lazer beatmap.
    /// Works for background images, videos, audio, etc.
    /// </summary>
    /// <param name="osuFilePath">Path to the .osu file in the lazer hash store</param>
    /// <param name="targetFilename">Original filename to find (e.g. "bg.jpg", "video.mp4")</param>
    /// <returns>Resolved path in the hash store, or null</returns>
    public static string? ResolveFile(string osuFilePath, string targetFilename)
    {
        try
        {
            string? filesDir = FindFilesDirectory(osuFilePath);
            if (filesDir == null) return null;

            string osuHash = Path.GetFileName(osuFilePath);
            string? lazerRoot = Path.GetDirectoryName(filesDir);
            if (lazerRoot == null) return null;

            string realmPath = Path.Combine(lazerRoot, "client.realm");
            if (!File.Exists(realmPath))
            {
                Console.WriteLine("[LazerFile] client.realm not found");
                return null;
            }

            Console.WriteLine($"[LazerFile] Resolving '{targetFilename}' for hash {osuHash[..8]}...");
            return SearchRealmForFile(realmPath, osuHash, targetFilename, filesDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LazerFile] Resolution failed: {ex.Message}");
            return null;
        }
    }

    private static string? SearchRealmForFile(string realmPath, string osuHash,
        string targetFilename, string filesDir)
    {
        try
        {
            byte[] realmData = File.ReadAllBytes(realmPath);
            byte[] searchBytes = System.Text.Encoding.ASCII.GetBytes(osuHash);

            // Find .osu hash in realm
            var positions = FindAllOccurrences(realmData, searchBytes);
            if (positions.Count == 0) return null;

            // Search for the target filename near the .osu hash
            byte[] filenameBytes = System.Text.Encoding.UTF8.GetBytes(targetFilename);

            foreach (int pos in positions)
            {
                int searchStart = Math.Max(0, pos - 8192);
                int searchEnd = Math.Min(realmData.Length, pos + 8192);

                // Look for the target filename near the hash
                var filenamePositions = FindAllOccurrences(realmData, filenameBytes, searchStart, searchEnd);

                foreach (int fnPos in filenamePositions)
                {
                    // Search near the filename for SHA256 hashes
                    int hashStart = Math.Max(0, fnPos - 4096);
                    int hashEnd = Math.Min(realmData.Length, fnPos + 4096);

                    var candidates = FindHexStrings(realmData, hashStart, hashEnd, 32);

                    foreach (string hash in candidates)
                    {
                        if (hash == osuHash) continue;

                        string candidatePath = Path.Combine(filesDir, hash[..2], hash);
                        if (File.Exists(candidatePath))
                        {
                            // Verify the file type matches what we expect
                            if (IsExpectedFileType(candidatePath, targetFilename))
                            {
                                Console.WriteLine($"[LazerFile] Resolved '{targetFilename}' -> {hash[..8]}...");
                                return candidatePath;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LazerFile] Realm search error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Check if a file's magic bytes match the expected type based on the original extension.
    /// </summary>
    private static bool IsExpectedFileType(string filePath, string originalFilename)
    {
        string ext = Path.GetExtension(originalFilename).ToLowerInvariant();

        try
        {
            byte[] header = new byte[16];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 32);
            int read = fs.Read(header, 0, header.Length);
            if (read < 4) return false;

            return ext switch
            {
                // Image types
                ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8,
                ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                ".bmp" => header[0] == 0x42 && header[1] == 0x4D,

                // Video types
                ".mp4" or ".m4v" => ContainsAt(header, read, new byte[] { 0x66, 0x74, 0x79, 0x70 }, 4) // "ftyp" at offset 4
                                    || (header[0] == 0x00 && header[1] == 0x00),
                ".avi" => header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46, // RIFF
                ".flv" => header[0] == 0x46 && header[1] == 0x4C && header[2] == 0x56, // FLV
                ".mkv" or ".webm" => header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3, // EBML

                // Audio types (in case someone uses this for audio too)
                ".mp3" => (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33) // ID3
                          || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0),          // MPEG sync
                ".ogg" => header[0] == 0x4F && header[1] == 0x67 && header[2] == 0x67 && header[3] == 0x53, // OggS
                ".wav" => header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46, // RIFF

                // Unknown extension — accept anything
                _ => true,
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsAt(byte[] data, int dataLen, byte[] pattern, int offset)
    {
        if (offset + pattern.Length > dataLen) return false;
        for (int i = 0; i < pattern.Length; i++)
            if (data[offset + i] != pattern[i]) return false;
        return true;
    }

    // ── Shared helpers (same as LazerAudioResolver) ──

    private static string? FindFilesDirectory(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir == null) return null;
        string? filesDir = Path.GetDirectoryName(dir);
        if (filesDir == null) return null;
        if (string.Equals(Path.GetFileName(filesDir), "files", StringComparison.OrdinalIgnoreCase))
            return filesDir;
        return null;
    }

    private static List<int> FindAllOccurrences(byte[] data, byte[] pattern, int start = 0, int end = -1)
    {
        var results = new List<int>();
        if (end < 0) end = data.Length;
        for (int i = start; i <= end - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) results.Add(i);
        }
        return results;
    }

    private static List<string> FindHexStrings(byte[] data, int start, int end, int minHexPairs)
    {
        var results = new List<string>();
        int targetLen = minHexPairs * 2;
        int i = start;
        while (i <= end - targetLen)
        {
            if (IsHexChar(data[i]))
            {
                int runStart = i;
                while (i < end && i - runStart < targetLen + 10 && IsHexChar(data[i])) i++;
                int runLen = i - runStart;
                if (runLen >= targetLen)
                {
                    string candidate = System.Text.Encoding.ASCII.GetString(data, runStart, targetLen);
                    if (!results.Contains(candidate))
                        results.Add(candidate);
                }
            }
            else i++;
        }
        return results;
    }

    private static bool IsHexChar(byte b) =>
        (b >= (byte)'0' && b <= (byte)'9') ||
        (b >= (byte)'a' && b <= (byte)'f') ||
        (b >= (byte)'A' && b <= (byte)'F');
}

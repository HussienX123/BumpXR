using System;
using System.IO;
using System.Text;

namespace BumpXR.Transfer
{
    /// <summary>
    /// The only place received files are created. Enforces: sanitized names, extension forced from the MIME type,
    /// files only directly inside the receive directory, and never overwriting an existing file.
    /// </summary>
    public static class ReceivedStore
    {
        public const string JpegMime = "image/jpeg";
        public const string PngMime = "image/png";
        private const int MaxStemLength = 48;

        public static bool IsAllowedMime(string mime) => mime == JpegMime || mime == PngMime;

        public static string ExtensionFor(string mime) => mime == PngMime ? ".png" : ".jpg";

        /// <summary>True if the leading bytes match the declared image type.</summary>
        public static bool MatchesMagic(string mime, byte[] buf, int count)
        {
            if (mime == JpegMime) return count >= 3 && buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF;
            if (mime == PngMime)
                return count >= 8 && buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47
                       && buf[4] == 0x0D && buf[5] == 0x0A && buf[6] == 0x1A && buf[7] == 0x0A;
            return false;
        }

        /// <summary>Strip directories, keep [A-Za-z0-9_-], cap length, force the extension from the MIME type.</summary>
        public static string SanitizeFileName(string name, string mime)
        {
            string leaf = string.IsNullOrEmpty(name) ? "" : name.Replace('\\', '/');
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0) leaf = leaf.Substring(slash + 1);
            int dot = leaf.LastIndexOf('.');
            string stem = dot > 0 ? leaf.Substring(0, dot) : leaf;

            var sb = new StringBuilder();
            foreach (char c in stem)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                sb.Append(ok ? c : '_');
                if (sb.Length >= MaxStemLength) break;
            }

            string clean = sb.ToString().Trim('_');
            if (clean.Length == 0) clean = "photo";
            return clean + ExtensionFor(mime);
        }

        /// <summary>Create a brand-new file (FileMode.CreateNew) inside <paramref name="dir"/>. Never overwrites.</summary>
        public static FileStream CreateNew(string dir, string fileName, string mime, out string path)
        {
            Directory.CreateDirectory(dir);
            string safe = SanitizeFileName(fileName, mime);
            string stem = Path.GetFileNameWithoutExtension(safe);
            string ext = Path.GetExtension(safe);
            string fullDir = Path.GetFullPath(dir);

            for (int i = 0; i < 1000; i++)
            {
                string candidate = i == 0 ? safe : $"{stem}_{i}{ext}";
                string full = Path.GetFullPath(Path.Combine(fullDir, candidate));
                if (!string.Equals(Path.GetDirectoryName(full), fullDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                    throw new IOException("Resolved path escaped the receive directory.");
                try
                {
                    var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    path = full;
                    return fs;
                }
                catch (IOException) when (File.Exists(full))
                {
                    // exists: try next suffix
                }
            }

            throw new IOException("Could not allocate a unique file name.");
        }

        public static string SaveBytes(string dir, string fileName, string mime, byte[] data)
        {
            string path;
            using (var fs = CreateNew(dir, fileName, mime, out path))
                fs.Write(data, 0, data.Length);
            return path;
        }
    }
}


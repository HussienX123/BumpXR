using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BumpXR.Core;

namespace BumpXR.Discovery
{
    /// <summary>
    /// IMediaSource over photos saved in Application.persistentDataPath/Received/ (Moments), sorted newest first.
    /// Requires zero Android storage permissions because it lives inside the app's private sandbox directory.
    /// </summary>
    public sealed class MomentsMediaSource : IMediaSource
    {
        private readonly string _receiveDir;
        private readonly List<MediaItem> _items = new List<MediaItem>();
        private readonly Dictionary<string, string> _pathsById = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyList<MediaItem> Items => _items;

        public MomentsMediaSource(string receiveDir)
        {
            _receiveDir = receiveDir;
            Refresh();
        }

        public void Refresh()
        {
            _items.Clear();
            _pathsById.Clear();
            if (string.IsNullOrEmpty(_receiveDir) || !Directory.Exists(_receiveDir)) return;

            IEnumerable<FileInfo> files = new DirectoryInfo(_receiveDir)
                .EnumerateFiles()
                .Where(f =>
                {
                    string ext = f.Extension.ToLowerInvariant();
                    return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
                })
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal);

            foreach (FileInfo f in files)
            {
                string id = "moment:" + f.FullName;
                string mime = f.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                _items.Add(new MediaItem
                {
                    Id = id,
                    FileName = f.Name,
                    MimeType = mime,
                    Size = f.Length
                });
                _pathsById[id] = f.FullName;
            }
        }

        public bool TryGetPath(MediaItem item, out string path)
        {
            if (item != null && _pathsById.TryGetValue(item.Id, out path)) return true;
            path = null;
            return false;
        }

        public MediaItem FindOrAddByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string full = Path.GetFullPath(path);
            string id = "moment:" + full;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Id == id) return _items[i];

            Refresh();
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Id == id) return _items[i];
            return null;
        }

        public byte[] ReadBytes(MediaItem item)
        {
            if (!TryGetPath(item, out string path) || !File.Exists(path))
                throw new FileNotFoundException("Moment file not found.", item?.FileName);
            return File.ReadAllBytes(path);
        }

        public Task<byte[]> ReadThumbnailBytesAsync(MediaItem item)
        {
            if (!TryGetPath(item, out string path)) return Task.FromResult<byte[]>(null);
            return Task.Run(() => File.Exists(path) ? File.ReadAllBytes(path) : null);
        }
    }
}


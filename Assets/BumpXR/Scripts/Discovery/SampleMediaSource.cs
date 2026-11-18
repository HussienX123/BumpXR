using System.Collections.Generic;
using System.Threading.Tasks;
using BumpXR.Core;
using UnityEngine;

namespace BumpXR.Discovery
{
    /// <summary>
    /// IMediaSource over the 6 sample images in Resources/SamplePhotos (raw JPEG/PNG bytes stored as
    /// "name.jpg.bytes" / "name.png.bytes" TextAssets). Does not touch system captures.
    /// </summary>
    public sealed class SampleMediaSource : IMediaSource
    {
        private readonly List<MediaItem> _items = new List<MediaItem>();
        private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>();

        public IReadOnlyList<MediaItem> Items => _items;

        public SampleMediaSource()
        {
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>("SamplePhotos"))
            {
                string name = asset.name; // e.g. "sunset.jpg"
                string mime = name.EndsWith(".png") ? "image/png" : name.EndsWith(".jpg") ? "image/jpeg" : null;
                if (mime == null) continue;
                byte[] data = asset.bytes;
                _items.Add(new MediaItem { Id = "sample:" + name, FileName = name, MimeType = mime, Size = data.Length });
                _bytes["sample:" + name] = data;
            }

            _items.Sort((a, b) => string.CompareOrdinal(a.FileName, b.FileName));
        }

        public byte[] ReadBytes(MediaItem item) => _bytes[item.Id];

        public Task<byte[]> ReadThumbnailBytesAsync(MediaItem item) => Task.FromResult(_bytes[item.Id]);
    }
}

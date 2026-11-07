using System.Collections.Generic;

namespace BumpXR.Core
{
    public sealed class MediaItem
    {
        public string Id;
        public string FileName;
        public string MimeType;
    }

    /// <summary>Where shareable media comes from. Only sample images for now; system captures are unverified.</summary>
    public interface IMediaSource
    {
        IReadOnlyList<MediaItem> Items { get; }
        byte[] ReadBytes(MediaItem item);
    }
}


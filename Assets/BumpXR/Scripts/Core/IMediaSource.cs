using System.Collections.Generic;
using System.Threading.Tasks;

namespace BumpXR.Core
{
    public sealed class MediaItem
    {
        public string Id;
        public string FileName;
        public string MimeType;
        public long Size;
    }

    /// <summary>Where shareable image media comes from (My Captures or Moments).</summary>
    public interface IMediaSource
    {
        IReadOnlyList<MediaItem> Items { get; }
        byte[] ReadBytes(MediaItem item);
        Task<byte[]> ReadThumbnailBytesAsync(MediaItem item);
    }
}

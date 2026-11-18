using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BumpXR.Core
{
    public enum TransferStatus { Completed, Declined, Rejected, Failed, TimedOut }

    public readonly struct TransferResult
    {
        public readonly TransferStatus Status;
        public readonly string Message;

        public TransferResult(TransferStatus status, string message = null)
        {
            Status = status;
            Message = message;
        }

        public bool Success => Status == TransferStatus.Completed;
        public override string ToString() => string.IsNullOrEmpty(Message) ? Status.ToString() : $"{Status}: {Message}";
    }

    public readonly struct OutgoingFile
    {
        public readonly string FileName;
        public readonly string MimeType;
        public readonly byte[] Data;
        public readonly Func<byte[]> ReadData;

        public OutgoingFile(string fileName, string mimeType, byte[] data)
        {
            FileName = fileName;
            MimeType = mimeType;
            Data = data;
            ReadData = null;
        }

        public OutgoingFile(string fileName, string mimeType, Func<byte[]> readData)
        {
            FileName = fileName;
            MimeType = mimeType;
            Data = null;
            ReadData = readData;
        }

        public byte[] ResolveBytes() => Data ?? ReadData?.Invoke();
    }

    public readonly struct OfferItemInfo
    {
        public readonly string SafeName;
        public readonly string MimeType;
        public readonly long Size;

        public OfferItemInfo(string safeName, string mimeType, long size)
        {
            SafeName = safeName;
            MimeType = mimeType;
            Size = size;
        }
    }

    public readonly struct BatchProgress
    {
        public readonly int FileIndex;
        public readonly int FileCount;
        public readonly string FileName;
        public readonly float FileProgress;
        public readonly float OverallProgress;

        public BatchProgress(int fileIndex, int fileCount, string fileName, float fileProgress, float overallProgress)
        {
            FileIndex = fileIndex;
            FileCount = fileCount;
            FileName = fileName;
            FileProgress = fileProgress;
            OverallProgress = overallProgress;
        }
    }

    /// <summary>A validated incoming transfer offer (single file or batch of up to 10 images). Nothing touches disk until accepted.</summary>
    public abstract class IncomingOffer
    {
        public string SenderName;
        public string FileName;
        public string MimeType;
        public long Size;
        public IReadOnlyList<OfferItemInfo> Items = Array.Empty<OfferItemInfo>();

        public int Count => Items != null && Items.Count > 0 ? Items.Count : 1;
        public long TotalSize => Size;

        /// <summary>Accept the batch, stream all files sequentially, and return their saved paths (atomic cleanup on error/cancel).</summary>
        public abstract Task<IReadOnlyList<string>> AcceptBatchAsync(
            IProgress<BatchProgress> progress,
            CancellationToken cancellationToken = default);

        /// <summary>Single-file convenience wrapper over AcceptBatchAsync.</summary>
        public virtual async Task<string> AcceptAsync(IProgress<float> progress)
        {
            IProgress<BatchProgress> mapped = progress == null
                ? null
                : new Progress<BatchProgress>(bp => progress.Report(bp.OverallProgress));
            IReadOnlyList<string> paths = await AcceptBatchAsync(mapped);
            return paths != null && paths.Count > 0 ? paths[0] : null;
        }

        public abstract void Decline();
    }

    public interface ITransferService
    {
        /// <summary>One-time token this device advertises; a sender must present it.</summary>
        string LocalToken { get; }

        /// <summary>TCP port this device listens on (valid after StartListening).</summary>
        int ListenPort { get; }

        /// <summary>Raised when a validated offer arrives. The receiver must explicitly accept.</summary>
        event Action<IncomingOffer> OfferReceived;

        /// <summary>Raised after the token rotated (one-time use); re-advertise to publish the new token.</summary>
        event Action TokenChanged;

        void StartListening();
        void StopListening();

        Task<TransferResult> SendAsync(PeerInfo peer, string senderName, string fileName, string mimeType,
            byte[] data, IProgress<float> progress);

        Task<TransferResult> SendBatchAsync(PeerInfo peer, string senderName, IReadOnlyList<OutgoingFile> files,
            IProgress<BatchProgress> progress = null, CancellationToken cancellationToken = default);
    }
}

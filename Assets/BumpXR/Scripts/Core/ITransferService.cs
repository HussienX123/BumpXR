using System;
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

    /// <summary>A validated incoming transfer offer. Nothing is written to disk until AcceptAsync is called.</summary>
    public abstract class IncomingOffer
    {
        public string SenderName;
        public string FileName;
        public string MimeType;
        public long Size;

        /// <summary>Accept the offer, stream the bytes, and return the saved file path (or throw on failure).</summary>
        public abstract Task<string> AcceptAsync(IProgress<float> progress);

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
    }
}

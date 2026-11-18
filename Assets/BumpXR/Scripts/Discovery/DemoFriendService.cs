using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BumpXR.Core;
using BumpXR.Transfer;

namespace BumpXR.Discovery
{
    /// <summary>
    /// Solo-demo decorator. Wraps real discovery + transfer and adds one simulated peer, always labeled "Demo Friend".
    /// It shows up if no real peer is found within 10 s of discovery starting, or via the manual toggle.
    /// Sending a single photo or batch simulates acceptance + sequential progress, then sends the same photos back.
    /// </summary>
    public sealed class DemoFriendService : IDiscoveryService, ITransferService
    {
        public const string FriendName = "Demo Friend";
        public const int AutoShowDelayMs = 10000;
        public static readonly Guid FriendId = new Guid("00000000-0000-0000-0000-00000000d3a0");

        private readonly IDiscoveryService _discovery;
        private readonly ITransferService _transfer;
        private readonly string _receiveDir;
        private bool _realPeerSeen;
        private int _discoveryGeneration;

        public PeerInfo Friend { get; } = new PeerInfo
        {
            SessionId = FriendId, DisplayName = FriendName, Token = "demo", Ip = "demo", Port = 0, IsDemo = true
        };

        public bool DemoFriendVisible { get; private set; }

        /// <summary>Raised when the Demo Friend appears or disappears (bool = visible).</summary>
        public event Action<bool> DemoFriendVisibilityChanged;

        public event Action<PeerInfo> PeerFound;
        public event Action<string> ErrorOccurred;
        public event Action<IncomingOffer> OfferReceived;
        public event Action TokenChanged;

        public DemoFriendService(IDiscoveryService discovery, ITransferService transfer, string receiveDir)
        {
            _discovery = discovery;
            _transfer = transfer;
            _receiveDir = receiveDir;

            _discovery.PeerFound += p => { _realPeerSeen = true; PeerFound?.Invoke(p); };
            _discovery.ErrorOccurred += e => ErrorOccurred?.Invoke(e);
            _transfer.OfferReceived += o => OfferReceived?.Invoke(o);
            _transfer.TokenChanged += () => TokenChanged?.Invoke();
        }

        // ---- IDiscoveryService ----
        public bool IsAdvertising => _discovery.IsAdvertising;
        public bool IsDiscovering => _discovery.IsDiscovering;

        public Task<bool> StartAdvertisingAsync(PeerInfo self) => _discovery.StartAdvertisingAsync(self);
        public Task<bool> StopAdvertisingAsync() => _discovery.StopAdvertisingAsync();

        public async Task<bool> StartDiscoveryAsync()
        {
            bool ok = await _discovery.StartDiscoveryAsync();
            AutoShowAfterDelay(++_discoveryGeneration);
            return ok;
        }

        public Task<bool> StopDiscoveryAsync()
        {
            _discoveryGeneration++;
            return _discovery.StopDiscoveryAsync();
        }

        private async void AutoShowAfterDelay(int generation)
        {
            await Task.Delay(AutoShowDelayMs);
            if (generation == _discoveryGeneration && !_realPeerSeen) SetDemoFriendVisible(true);
        }

        /// <summary>Manual toggle for the Demo Friend.</summary>
        public void SetDemoFriendVisible(bool visible)
        {
            if (DemoFriendVisible == visible) return;
            DemoFriendVisible = visible;
            if (visible) PeerFound?.Invoke(Friend);
            DemoFriendVisibilityChanged?.Invoke(visible);
        }

        // ---- ITransferService ----
        public string LocalToken => _transfer.LocalToken;
        public int ListenPort => _transfer.ListenPort;
        public void StartListening() => _transfer.StartListening();
        public void StopListening() => _transfer.StopListening();

        public Task<TransferResult> SendAsync(PeerInfo peer, string senderName, string fileName, string mimeType,
            byte[] data, IProgress<float> progress)
        {
            IProgress<BatchProgress> mapped = progress == null
                ? null
                : new Progress<BatchProgress>(bp => progress.Report(bp.OverallProgress));
            return SendBatchAsync(peer, senderName, new[] { new OutgoingFile(fileName, mimeType, data) }, mapped);
        }

        public async Task<TransferResult> SendBatchAsync(
            PeerInfo peer,
            string senderName,
            IReadOnlyList<OutgoingFile> files,
            IProgress<BatchProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (peer == null || !peer.IsDemo)
                return await _transfer.SendBatchAsync(peer, senderName, files, progress, cancellationToken);

            if (files == null || files.Count == 0 || files.Count > LocalNetworkTransferService.MaxBatchItems)
                return new TransferResult(TransferStatus.Rejected, "batch count not allowed");

            long total = 0;
            var payloads = new byte[files.Count][];
            var items = new OfferItemInfo[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                OutgoingFile f = files[i];
                if (!ReceivedStore.IsAllowedMime(f.MimeType))
                    return new TransferResult(TransferStatus.Rejected, "type_not_allowed");
                byte[] bytes = f.ResolveBytes();
                if (bytes == null || bytes.Length == 0 || bytes.Length > LocalNetworkTransferService.MaxFileBytes)
                    return new TransferResult(TransferStatus.Rejected, "size_not_allowed");
                if (total > LocalNetworkTransferService.MaxBatchBytes - bytes.Length)
                    return new TransferResult(TransferStatus.Rejected, "batch_size_not_allowed");
                total += bytes.Length;
                payloads[i] = bytes;
                items[i] = new OfferItemInfo(ReceivedStore.SanitizeFileName(f.FileName, f.MimeType), f.MimeType, bytes.Length);
            }

            try
            {
                await Task.Delay(800, cancellationToken);
                const int stepsPerFile = 5;
                int totalSteps = files.Count * stepsPerFile;
                int doneSteps = 0;
                for (int fi = 0; fi < files.Count; fi++)
                {
                    for (int s = 1; s <= stepsPerFile; s++)
                    {
                        await Task.Delay(60, cancellationToken);
                        doneSteps++;
                        progress?.Report(new BatchProgress(fi, files.Count, items[fi].SafeName, s / (float)stepsPerFile, doneSteps / (float)totalSteps));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return new TransferResult(TransferStatus.Failed, "cancelled");
            }

            _ = ReplyWithPhotosAsync(items, payloads, total);
            return new TransferResult(TransferStatus.Completed);
        }

        private async Task ReplyWithPhotosAsync(OfferItemInfo[] items, byte[][] payloads, long total)
        {
            await Task.Delay(2000);
            if (DemoFriendVisible) OfferReceived?.Invoke(new DemoIncomingOffer(_receiveDir, items, payloads, total));
        }

        private sealed class DemoIncomingOffer : IncomingOffer
        {
            private readonly string _receiveDir;
            private readonly byte[][] _payloads;

            public DemoIncomingOffer(string receiveDir, OfferItemInfo[] items, byte[][] payloads, long total)
            {
                _receiveDir = receiveDir;
                _payloads = payloads;
                SenderName = FriendName;
                Items = items;
                FileName = items[0].SafeName;
                MimeType = items[0].MimeType;
                Size = total;
            }

            public override async Task<IReadOnlyList<string>> AcceptBatchAsync(
                IProgress<BatchProgress> progress,
                CancellationToken cancellationToken = default)
            {
                var saved = new List<string>(Items.Count);
                try
                {
                    const int stepsPerFile = 4;
                    int totalSteps = Items.Count * stepsPerFile;
                    int doneSteps = 0;
                    for (int fi = 0; fi < Items.Count; fi++)
                    {
                        for (int s = 1; s <= stepsPerFile; s++)
                        {
                            await Task.Delay(60, cancellationToken);
                            doneSteps++;
                            progress?.Report(new BatchProgress(
                                fi,
                                Items.Count,
                                Items[fi].SafeName,
                                s / (float)stepsPerFile,
                                doneSteps / (float)totalSteps));
                        }
                        saved.Add(ReceivedStore.SaveBytes(_receiveDir, Items[fi].SafeName, Items[fi].MimeType, _payloads[fi]));
                    }
                    return saved;
                }
                catch
                {
                    for (int i = 0; i < saved.Count; i++)
                    {
                        try { if (System.IO.File.Exists(saved[i])) System.IO.File.Delete(saved[i]); } catch { }
                    }
                    throw;
                }
            }

            public override void Decline() { }
        }
    }
}

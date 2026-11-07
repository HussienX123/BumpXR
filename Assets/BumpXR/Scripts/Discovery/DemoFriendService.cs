using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BumpXR.Core;
using BumpXR.Transfer;

namespace BumpXR.Discovery
{
    /// <summary>
    /// Solo-demo decorator. Wraps real discovery + transfer and adds one simulated peer, always labeled "Demo Friend".
    /// It shows up if no real peer is found within 10 s of discovery starting, or via the manual toggle.
    /// Sending to it simulates acceptance + progress, then it sends a sample photo back through the normal incoming flow.
    /// </summary>
    public sealed class DemoFriendService : IDiscoveryService, ITransferService
    {
        public const string FriendName = "Demo Friend";
        public const int AutoShowDelayMs = 10000;
        public static readonly Guid FriendId = new Guid("00000000-0000-0000-0000-00000000d3a0");

        private readonly IDiscoveryService _discovery;
        private readonly ITransferService _transfer;
        private readonly IMediaSource _media;
        private readonly string _receiveDir;
        private readonly Random _random = new Random();
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

        public DemoFriendService(IDiscoveryService discovery, ITransferService transfer, IMediaSource media, string receiveDir)
        {
            _discovery = discovery;
            _transfer = transfer;
            _media = media;
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

        public async Task<TransferResult> SendAsync(PeerInfo peer, string senderName, string fileName, string mimeType,
            byte[] data, IProgress<float> progress)
        {
            if (peer == null || !peer.IsDemo)
                return await _transfer.SendAsync(peer, senderName, fileName, mimeType, data, progress);

            if (!ReceivedStore.IsAllowedMime(mimeType) || data == null || data.Length == 0 || data.Length > LocalNetworkTransferService.MaxFileBytes)
                return new TransferResult(TransferStatus.Rejected, "not allowed");

            await Task.Delay(800); // "Demo Friend" thinks, then accepts
            for (int i = 1; i <= 10; i++)
            {
                await Task.Delay(150);
                progress?.Report(i / 10f);
            }

            ReplyWithSamplePhoto(fileName);
            return new TransferResult(TransferStatus.Completed);
        }

        private async void ReplyWithSamplePhoto(string sentFileName)
        {
            await Task.Delay(2000);
            IReadOnlyList<MediaItem> items = _media.Items;
            if (items.Count == 0) return;

            int index = _random.Next(items.Count);
            if (items.Count > 1 && items[index].FileName == sentFileName) index = (index + 1) % items.Count;
            OfferReceived?.Invoke(new DemoIncomingOffer(this, items[index]));
        }

        private sealed class DemoIncomingOffer : IncomingOffer
        {
            private readonly DemoFriendService _owner;
            private readonly MediaItem _item;
            private readonly byte[] _bytes;

            public DemoIncomingOffer(DemoFriendService owner, MediaItem item)
            {
                _owner = owner;
                _item = item;
                _bytes = owner._media.ReadBytes(item);
                SenderName = FriendName;
                FileName = item.FileName;
                MimeType = item.MimeType;
                Size = _bytes.Length;
            }

            public override async Task<string> AcceptAsync(IProgress<float> progress)
            {
                for (int i = 1; i <= 8; i++)
                {
                    await Task.Delay(150);
                    progress?.Report(i / 8f);
                }

                return ReceivedStore.SaveBytes(_owner._receiveDir, _item.FileName, _item.MimeType, _bytes);
            }

            public override void Decline() { }
        }
    }
}

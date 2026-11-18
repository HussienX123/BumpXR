using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BumpXR.Core;

namespace BumpXR.Transfer
{
    /// <summary>Superseded by LocalNetworkTransferService. Kept as an inert placeholder (files are not deleted).</summary>
    public sealed class StubTransferService : ITransferService
    {
        public string LocalToken => null;
        public int ListenPort => 0;
        public event Action<IncomingOffer> OfferReceived { add { } remove { } }
        public event Action TokenChanged { add { } remove { } }

        public void StartListening() => throw new NotImplementedException();
        public void StopListening() { }

        public Task<TransferResult> SendAsync(PeerInfo peer, string senderName, string fileName, string mimeType,
            byte[] data, IProgress<float> progress) => throw new NotImplementedException();

        public Task<TransferResult> SendBatchAsync(PeerInfo peer, string senderName, IReadOnlyList<OutgoingFile> files,
            IProgress<BatchProgress> progress = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}

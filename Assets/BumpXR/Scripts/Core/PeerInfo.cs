using System;

namespace BumpXR.Core
{
    /// <summary>A nearby BumpXR peer as seen through the discovery layer.</summary>
    public sealed class PeerInfo
    {
        /// <summary>Session UUID of the peer's advertisement (OVRColocationSession.Data.AdvertisementUuid).</summary>
        public Guid SessionId;

        public string DisplayName;

        /// <summary>One-time token the sender must present to this peer.</summary>
        public string Token;

        /// <summary>Peer's Wi-Fi IPv4 address.</summary>
        public string Ip;

        /// <summary>Peer's TCP listen port.</summary>
        public int Port;

        /// <summary>True for the simulated Demo Friend.</summary>
        public bool IsDemo;

        public override string ToString() => $"{DisplayName} ({SessionId}) {Ip}:{Port}";
    }
}

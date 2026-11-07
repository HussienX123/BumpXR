using System;
using System.Threading.Tasks;

namespace BumpXR.Core
{
    public interface IDiscoveryService
    {
        bool IsAdvertising { get; }
        bool IsDiscovering { get; }

        /// <summary>Raised on the main thread each time a nearby peer advertisement is received.</summary>
        event Action<PeerInfo> PeerFound;

        /// <summary>Raised with a human-readable message when any discovery operation fails.</summary>
        event Action<string> ErrorOccurred;

        /// <summary>Advertise this device (name, token, ip, port from <paramref name="self"/>). true on success.</summary>
        Task<bool> StartAdvertisingAsync(PeerInfo self);
        Task<bool> StopAdvertisingAsync();
        Task<bool> StartDiscoveryAsync();
        Task<bool> StopDiscoveryAsync();
    }
}

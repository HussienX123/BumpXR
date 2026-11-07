using System;
using System.Text;
using System.Threading.Tasks;
using BumpXR.Core;
using UnityEngine;

namespace BumpXR.Discovery
{
    /// <summary>
    /// IDiscoveryService backed by Meta's Colocation Discovery API (SDK class: OVRColocationSession).
    /// Docs: https://developers.meta.com/horizon/documentation/unity/unity-colocation-discovery/
    /// Requires colocationSessionSupport enabled in OculusProjectConfig (adds
    /// com.oculus.permission.USE_COLOCATION_DISCOVERY_API to the manifest at build time).
    /// </summary>
    public sealed class MetaDiscoveryService : IDiscoveryService
    {
        [Serializable]
        private sealed class Payload
        {
            public string n; // display name
            public string t; // one-time token
            public string ip; // sender Wi-Fi IPv4
            public int p;     // TCP listen port
        }

        public bool IsAdvertising { get; private set; }
        public bool IsDiscovering { get; private set; }

        public event Action<PeerInfo> PeerFound;
        public event Action<string> ErrorOccurred;

        public async Task<bool> StartAdvertisingAsync(PeerInfo self)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                new Payload { n = self.DisplayName, t = self.Token, ip = self.Ip, p = self.Port }));
            Debug.Assert(data.Length <= OVRColocationSession.Data.MaxMetadataSize,
                $"Advertisement metadata is {data.Length} bytes, max is {OVRColocationSession.Data.MaxMetadataSize}");
            if (data.Length > OVRColocationSession.Data.MaxMetadataSize)
            {
                RaiseError($"Metadata too large: {data.Length} > {OVRColocationSession.Data.MaxMetadataSize} bytes");
                return false;
            }

            try
            {
                var result = await OVRColocationSession.StartAdvertisementAsync(data);
                if (!result.Success)
                {
                    RaiseError($"StartAdvertisement failed: {result.Status}");
                    return false;
                }

                IsAdvertising = true;
                return true;
            }
            catch (Exception e)
            {
                RaiseError($"StartAdvertisement exception: {e.Message}");
                return false;
            }
        }

        public async Task<bool> StopAdvertisingAsync()
        {
            try
            {
                var result = await OVRColocationSession.StopAdvertisementAsync();
                if (!result.Success)
                {
                    RaiseError($"StopAdvertisement failed: {result.Status}");
                    return false;
                }

                IsAdvertising = false;
                return true;
            }
            catch (Exception e)
            {
                RaiseError($"StopAdvertisement exception: {e.Message}");
                return false;
            }
        }

        public async Task<bool> StartDiscoveryAsync()
        {
            // Doc: register the listener before starting discovery; avoid duplicate listeners.
            OVRColocationSession.ColocationSessionDiscovered -= OnSessionDiscovered;
            OVRColocationSession.ColocationSessionDiscovered += OnSessionDiscovered;

            try
            {
                var result = await OVRColocationSession.StartDiscoveryAsync();
                if (!result.Success)
                {
                    OVRColocationSession.ColocationSessionDiscovered -= OnSessionDiscovered;
                    RaiseError($"StartDiscovery failed: {result.Status}");
                    return false;
                }

                IsDiscovering = true;
                return true;
            }
            catch (Exception e)
            {
                OVRColocationSession.ColocationSessionDiscovered -= OnSessionDiscovered;
                RaiseError($"StartDiscovery exception: {e.Message}");
                return false;
            }
        }

        public async Task<bool> StopDiscoveryAsync()
        {
            try
            {
                var result = await OVRColocationSession.StopDiscoveryAsync();
                // Doc: stopping does NOT unregister listeners, so do it ourselves.
                OVRColocationSession.ColocationSessionDiscovered -= OnSessionDiscovered;
                if (!result.Success)
                {
                    RaiseError($"StopDiscovery failed: {result.Status}");
                    return false;
                }

                IsDiscovering = false;
                return true;
            }
            catch (Exception e)
            {
                OVRColocationSession.ColocationSessionDiscovered -= OnSessionDiscovered;
                RaiseError($"StopDiscovery exception: {e.Message}");
                return false;
            }
        }

        private void OnSessionDiscovered(OVRColocationSession.Data session)
        {
            var peer = new PeerInfo { SessionId = session.AdvertisementUuid, DisplayName = "(unparsed)" };
            try
            {
                var payload = JsonUtility.FromJson<Payload>(Encoding.UTF8.GetString(session.Metadata));
                if (payload != null)
                {
                    peer.DisplayName = payload.n;
                    peer.Token = payload.t;
                    peer.Ip = payload.ip;
                    peer.Port = payload.p;
                }
            }
            catch (Exception e)
            {
                RaiseError($"Bad metadata from {session.AdvertisementUuid}: {e.Message}");
            }

            PeerFound?.Invoke(peer);
        }

        private void RaiseError(string message)
        {
            Debug.LogWarning($"[BumpXR.Discovery] {message}");
            ErrorOccurred?.Invoke(message);
        }
    }
}

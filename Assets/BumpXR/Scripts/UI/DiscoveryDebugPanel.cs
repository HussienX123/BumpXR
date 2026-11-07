using System.Collections;
using System.Collections.Generic;
using System.Text;
using BumpXR.Core;
using BumpXR.Discovery;
using BumpXR.Transfer;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BumpXR.UI
{
    /// <summary>
    /// World-space debug panel for the discovery spike. Auto-starts advertising + discovery on enable.
    /// Shows: advertising state, discovered peers (name, UUID), last error, internet reachability.
    /// </summary>
    public sealed class DiscoveryDebugPanel : MonoBehaviour
    {
        // Public endpoint that returns HTTP 204; used only to test real internet access.
        private const string ConnectivityUrl = "https://clients3.google.com/generate_204";

        [SerializeField] private Text output;
        [SerializeField] private string displayName = "";

        private IDiscoveryService _discovery;
        private LocalNetworkTransferService _transfer;
        private string _ip;
        private readonly Dictionary<System.Guid, PeerInfo> _peers = new Dictionary<System.Guid, PeerInfo>();
        private string _lastError = "(none)";
        private string _internetProbe = "(probing...)";

        private void OnEnable()
        {
            if (output != null && output.font == null)
                output.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (string.IsNullOrEmpty(displayName))
                displayName = SystemInfo.deviceModel;

            _discovery = new MetaDiscoveryService();
            _discovery.PeerFound += OnPeerFound;
            _discovery.ErrorOccurred += OnError;

            _transfer = new LocalNetworkTransferService();
            _transfer.StartListening();
            _ip = NetworkUtil.GetWifiIPv4() ?? "(none)";
            StartDiscoveryAndAdvertising();
            StartCoroutine(ProbeInternetLoop());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (_discovery == null) return;
            _discovery.PeerFound -= OnPeerFound;
            _discovery.ErrorOccurred -= OnError;
            _ = _discovery.StopAdvertisingAsync();
            _ = _discovery.StopDiscoveryAsync();
            _transfer?.Dispose();
            _transfer = null;
            _discovery = null;
        }

        private async void StartDiscoveryAndAdvertising()
        {
            await _discovery.StartAdvertisingAsync(new PeerInfo
            {
                DisplayName = displayName, Token = _transfer.LocalToken, Ip = _ip, Port = _transfer.ListenPort
            });
            await _discovery.StartDiscoveryAsync();
        }

        private void OnPeerFound(PeerInfo peer) => _peers[peer.SessionId] = peer;
        private void OnError(string message) => _lastError = message;

        private IEnumerator ProbeInternetLoop()
        {
            while (true)
            {
                using (var req = UnityWebRequest.Head(ConnectivityUrl))
                {
                    req.timeout = 5;
                    yield return req.SendWebRequest();
                    _internetProbe = req.result == UnityWebRequest.Result.Success
                        ? $"OK (HTTP {req.responseCode})"
                        : $"FAILED ({req.error})";
                }
                yield return new WaitForSeconds(10f);
            }
        }

        private void Update()
        {
            if (output == null || _discovery == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("BumpXR Discovery Debug");
            sb.AppendLine($"Name: {displayName}");
            sb.AppendLine($"Wi-Fi IPv4: {_ip}   TCP port: {_transfer.ListenPort}");
            sb.AppendLine($"Advertising: {_discovery.IsAdvertising}   Discovering: {_discovery.IsDiscovering}");
            sb.AppendLine($"Internet (Unity): {Application.internetReachability}");
            sb.AppendLine($"Internet (probe): {_internetProbe}");
            sb.AppendLine($"Last error: {_lastError}");
            sb.AppendLine($"Peers ({_peers.Count}):");
            foreach (var p in _peers.Values)
                sb.AppendLine($" - {p.DisplayName}  [{p.SessionId}]");
            output.text = sb.ToString();
        }
    }
}


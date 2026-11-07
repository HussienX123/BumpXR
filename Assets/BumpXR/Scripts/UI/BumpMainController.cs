using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BumpXR.Core;
using BumpXR.Discovery;
using BumpXR.Transfer;
using UnityEngine;
using UnityEngine.UI;

namespace BumpXR.UI
{
    /// <summary>
    /// BumpMain app: wires discovery + transfer + Demo Friend + sample media and builds a hands-only world-space UI.
    /// All buttons are plain UGUI Buttons; they are driven by Meta Interaction SDK hand poke / pinch-ray
    /// (PointableCanvas + PokeInteractable/RayInteractable on the canvases, PointableCanvasModule on the EventSystem).
    /// Nothing here needs a controller.
    /// </summary>
    public sealed class BumpMainController : MonoBehaviour
    {
        private const float AutoDeclineSeconds = 30f;
        private const int MaxPeerRows = 5;
        private const int MaxReceivedThumbs = 6;

        [SerializeField] private RectTransform shareRoot;
        [SerializeField] private RectTransform incomingRoot;
        [SerializeField] private string displayName = "";

        // services
        private LocalNetworkTransferService _transfer;
        private MetaDiscoveryService _meta;
        private DemoFriendService _demo;
        private IDiscoveryService _discovery;
        private ITransferService _transferApi;
        private IMediaSource _media;
        private PeerInfo _self;
        private string _ip;
        private string _receiveDir;
        private string _lastError = "";

        // state
        private readonly List<PeerInfo> _peers = new List<PeerInfo>();
        private readonly List<Button> _peerButtons = new List<Button>();
        private readonly List<Button> _photoButtons = new List<Button>();
        private readonly List<string> _receivedPaths = new List<string>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly Queue<IncomingOffer> _offerQueue = new Queue<IncomingOffer>();
        private PeerInfo _selectedPeer;
        private MediaItem _selectedItem;
        private bool _sending;
        private bool _peersDirty = true;
        private bool _receivedDirty = true;
        private IncomingOffer _current;
        private bool _accepting;
        private float _incomingDeadline;

        // share UI
        private Text _statusText;
        private RectTransform _peerListRoot;
        private Text _demoToggleLabel;
        private Button _sendButton;
        private RectTransform _sendFill;
        private Text _sendStatus;
        private RawImage _viewer;
        private Text _viewerCaption;
        private RectTransform _receivedRoot;
        private CanvasGroup _shareGroup;
        private float _nextStatusRefresh;

        // incoming UI
        private Text _incFrom, _incFile, _incInfo;
        private RectTransform _incFill;
        private GameObject _incButtons;

        private static Font _font;

        // ---- test/automation surface (same code paths the buttons use) ----
        public int PeerCount => _peers.Count;
        public int ReceivedCount => _receivedPaths.Count;
        public bool IncomingVisible => _current != null;
        public bool IsSending => _sending;
        public string SendStatus => _sendStatus != null ? _sendStatus.text : "";
        public DemoFriendService Demo => _demo;
        public int PhotoCount => _media?.Items.Count ?? 0;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (string.IsNullOrEmpty(displayName)) displayName = Application.isEditor ? "Editor" : SystemInfo.deviceModel;

            _receiveDir = Path.Combine(Application.persistentDataPath, "Received");
            _media = new SampleMediaSource();
            _transfer = new LocalNetworkTransferService(_receiveDir);
            _meta = new MetaDiscoveryService();
            _demo = new DemoFriendService(_meta, _transfer, _media, _receiveDir);
            _discovery = _demo;
            _transferApi = _demo;

            _discovery.PeerFound += OnPeerFound;
            _discovery.ErrorOccurred += e => _lastError = e;
            _transferApi.OfferReceived += OnOfferReceived;
            _transferApi.TokenChanged += () => ReAdvertise();
            _demo.DemoFriendVisibilityChanged += OnDemoVisibility;

            BuildShareUi();
            BuildIncomingUi();
            incomingRoot.gameObject.SetActive(false);
        }

        private void Start()
        {
            PlaceInFront(shareRoot, 0.6f, -0.05f);
            StartCoroutine(ReplaceAfterTrackingSettles());
            Begin();
        }

        private IEnumerator ReplaceAfterTrackingSettles()
        {
            yield return new WaitForSeconds(1.0f);
            PlaceInFront(shareRoot, 0.6f, -0.05f);
        }

        private async void Begin()
        {
            _ip = NetworkUtil.GetWifiIPv4() ?? "(none)";
            _transferApi.StartListening();
            _self = new PeerInfo { DisplayName = displayName, Token = _transferApi.LocalToken, Ip = _ip, Port = _transferApi.ListenPort };
            await _discovery.StartAdvertisingAsync(_self);
            await _discovery.StartDiscoveryAsync();
        }

        private async void ReAdvertise()
        {
            if (_self == null) return;
            _self.Token = _transferApi.LocalToken;
            await _discovery.StopAdvertisingAsync();
            await _discovery.StartAdvertisingAsync(_self);
        }

        private void OnDestroy()
        {
            _transfer?.Dispose();
            _ = _discovery?.StopAdvertisingAsync();
            _ = _discovery?.StopDiscoveryAsync();
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
        }

        // ================= discovery / peers =================

        private void OnPeerFound(PeerInfo peer)
        {
            if (!peer.IsDemo) _peers.RemoveAll(p => !p.IsDemo && p.Ip == peer.Ip && p.Port == peer.Port); // re-advertisement
            _peers.RemoveAll(p => p.SessionId == peer.SessionId);
            _peers.Add(peer);
            _peersDirty = true;
        }

        private void OnDemoVisibility(bool visible)
        {
            if (!visible)
            {
                _peers.RemoveAll(p => p.IsDemo);
                if (_selectedPeer != null && _selectedPeer.IsDemo) _selectedPeer = null;
            }

            _peersDirty = true;
        }

        public void ToggleDemoFriend() => _demo.SetDemoFriendVisible(!_demo.DemoFriendVisible);

        public void SelectPeerByIndex(int index)
        {
            if (index < 0 || index >= _peers.Count) return;
            _selectedPeer = _peers[index];
            _peersDirty = true;
        }

        public void SelectPhotoByIndex(int index)
        {
            if (index < 0 || index >= _media.Items.Count) return;
            _selectedItem = _media.Items[index];
        }

        // ================= sending =================

        public async void Send()
        {
            if (_sending || _selectedPeer == null || _selectedItem == null || _current != null) return;
            _sending = true;
            PeerInfo peer = _selectedPeer;
            MediaItem item = _selectedItem;
            SetFill(_sendFill, 0f);
            _sendStatus.text = $"Sending {item.FileName} to {peer.DisplayName}...";
            try
            {
                var progress = new Progress<float>(p => SetFill(_sendFill, p));
                TransferResult r = await _transferApi.SendAsync(peer, displayName, item.FileName, item.MimeType, _media.ReadBytes(item), progress);
                _sendStatus.text = r.Success ? $"Sent to {peer.DisplayName}!" : $"Not sent: {r}";
                if (!r.Success) SetFill(_sendFill, 0f);
            }
            catch (Exception e)
            {
                _sendStatus.text = "Send failed: " + e.Message;
            }
            finally
            {
                _sending = false;
            }
        }

        // ================= incoming =================

        private void OnOfferReceived(IncomingOffer offer)
        {
            _offerQueue.Enqueue(offer);
            if (_current == null) ShowNextOffer();
        }

        private void ShowNextOffer()
        {
            if (_offerQueue.Count == 0)
            {
                _current = null;
                incomingRoot.gameObject.SetActive(false);
                _shareGroup.blocksRaycasts = true;
                _shareGroup.alpha = 1f;
                return;
            }

            _current = _offerQueue.Dequeue();
            _accepting = false;
            _incomingDeadline = Time.unscaledTime + AutoDeclineSeconds;
            _incFrom.text = "From: " + _current.SenderName;
            _incFile.text = _current.FileName;
            _incInfo.text = $"{FormatSize(_current.Size)}  -  auto-decline in {AutoDeclineSeconds:0}s";
            SetFill(_incFill, 0f);
            _incButtons.SetActive(true);
            incomingRoot.gameObject.SetActive(true);
            PlaceInFront(incomingRoot, 0.45f, -0.08f);
            _shareGroup.blocksRaycasts = false; // keep stray pokes off the panel behind the card
            _shareGroup.alpha = 0.35f;
        }

        public void DeclineIncoming()
        {
            if (_current == null || _accepting) return;
            _current.Decline();
            _sendStatus.text = "Declined photo from " + _current.SenderName;
            ShowNextOffer();
        }

        public async void AcceptIncoming()
        {
            if (_current == null || _accepting) return;
            _accepting = true;
            IncomingOffer offer = _current;
            _incButtons.SetActive(false);
            _incInfo.text = "Receiving...";
            try
            {
                string path = await offer.AcceptAsync(new Progress<float>(p => SetFill(_incFill, p)));
                _receivedPaths.Add(path);
                _receivedDirty = true;
                ShowReceived(path);
                _sendStatus.text = $"Received {Path.GetFileName(path)} from {offer.SenderName}";
            }
            catch (Exception e)
            {
                _sendStatus.text = "Receive failed: " + e.Message;
            }
            finally
            {
                _accepting = false;
                ShowNextOffer();
            }
        }

        // ================= update =================

        private void Update()
        {
            if (_current != null && !_accepting)
            {
                float left = _incomingDeadline - Time.unscaledTime;
                if (left <= 0f) { DeclineIncoming(); }
                else _incInfo.text = $"{FormatSize(_current.Size)}  -  auto-decline in {Mathf.CeilToInt(left)}s";
            }

            if (_peersDirty) RefreshPeers();
            if (_receivedDirty) RefreshReceived();

            for (int i = 0; i < _photoButtons.Count; i++)
                _photoButtons[i].image.color = _media.Items[i] == _selectedItem ? new Color(0.2f, 0.85f, 1f) : new Color(0.25f, 0.25f, 0.3f);

            _sendButton.interactable = !_sending && _selectedPeer != null && _selectedItem != null && _current == null;

            if (Time.unscaledTime >= _nextStatusRefresh)
            {
                _nextStatusRefresh = Time.unscaledTime + 0.5f;
                string err = string.IsNullOrEmpty(_lastError) ? "" : "\nLast error: " + (_lastError.Length > 70 ? _lastError.Substring(0, 70) : _lastError);
                _statusText.text = $"You: {displayName}   Wi-Fi {_ip}:{_transferApi.ListenPort}\n" +
                                   $"Advertising: {_discovery.IsAdvertising}  Discovering: {_discovery.IsDiscovering}  Net: {Application.internetReachability}{err}";
                _demoToggleLabel.text = _demo.DemoFriendVisible ? "Demo Friend: ON" : "Demo Friend: OFF";
            }
        }

        // ================= UI building =================

        private void BuildShareUi()
        {
            if (!shareRoot.TryGetComponent(out _shareGroup)) _shareGroup = shareRoot.gameObject.AddComponent<CanvasGroup>();
            RectTransform r = shareRoot;

            Label(r, "BumpXR", new Vector2(20, 8), new Vector2(300, 50), 40, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _statusText = Label(r, "", new Vector2(250, 8), new Vector2(630, 56), 20, TextAnchor.MiddleRight, new Color(0.75f, 0.85f, 1f));

            // Column A: nearby
            Label(r, "Nearby", new Vector2(20, 72), new Vector2(250, 36), 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _peerListRoot = Rect("Peers", r, new Vector2(20, 112), new Vector2(250, 5 * 62));
            Button demoBtn = MakeButton(r, "Demo Friend", new Vector2(20, 430), new Vector2(250, 56), new Color(0.35f, 0.25f, 0.55f), 24, ToggleDemoFriend);
            _demoToggleLabel = demoBtn.GetComponentInChildren<Text>();

            // Column B: photo grid
            Label(r, "Photos", new Vector2(290, 72), new Vector2(330, 36), 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            IReadOnlyList<MediaItem> items = _media.Items;
            for (int i = 0; i < items.Count && i < 6; i++)
            {
                int index = i;
                var pos = new Vector2(290 + (i % 3) * 112, 112 + (i / 3) * 112);
                Button b = MakeButton(r, "", pos, new Vector2(104, 104), new Color(0.25f, 0.25f, 0.3f), 20, () => SelectPhotoByIndex(index));
                RawImage thumb = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                thumb.transform.SetParent(b.transform, false);
                var tr = (RectTransform)thumb.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(6, 6); tr.offsetMax = new Vector2(-6, -6);
                thumb.texture = Decode(_media.ReadBytes(items[i]));
                thumb.raycastTarget = false;
                _photoButtons.Add(b);
            }

            // Send + progress
            _sendButton = MakeButton(r, "SEND", new Vector2(290, 345), new Vector2(330, 80), new Color(0.1f, 0.6f, 0.3f), 36, Send);
            RectTransform bar = Rect("ProgressBack", r, new Vector2(290, 440), new Vector2(330, 22));
            bar.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f);
            _sendFill = Rect("ProgressFill", bar, Vector2.zero, Vector2.zero);
            _sendFill.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.85f, 1f);
            SetFill(_sendFill, 0f);
            _sendStatus = Label(r, "Pick a photo and a person, then press SEND.", new Vector2(20, 500), new Vector2(860, 90), 24, TextAnchor.UpperLeft, Color.white);

            // Column C: viewer + received
            Label(r, "Received", new Vector2(640, 72), new Vector2(240, 36), 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            RectTransform viewerBg = Rect("ViewerBg", r, new Vector2(640, 112), new Vector2(240, 180));
            viewerBg.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f);
            _viewer = new GameObject("Viewer", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _viewer.transform.SetParent(viewerBg, false);
            var vr = (RectTransform)_viewer.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = new Vector2(4, 4); vr.offsetMax = new Vector2(-4, -4);
            _viewer.color = new Color(1, 1, 1, 0);
            _viewerCaption = Label(r, "Nothing received yet", new Vector2(640, 296), new Vector2(240, 30), 18, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.85f));
            _receivedRoot = Rect("ReceivedThumbs", r, new Vector2(640, 332), new Vector2(240, 160));

            LoadExistingReceived();
        }

        private void BuildIncomingUi()
        {
            RectTransform r = incomingRoot;
            Label(r, "Incoming photo", new Vector2(20, 12), new Vector2(480, 56), 42, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _incFrom = Label(r, "", new Vector2(20, 80), new Vector2(480, 44), 32, TextAnchor.MiddleLeft, new Color(0.6f, 0.9f, 1f));
            _incFile = Label(r, "", new Vector2(20, 128), new Vector2(480, 40), 26, TextAnchor.MiddleLeft, Color.white);
            _incInfo = Label(r, "", new Vector2(20, 168), new Vector2(480, 36), 22, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.85f));

            RectTransform bar = Rect("IncProgressBack", r, new Vector2(20, 214), new Vector2(480, 18));
            bar.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f);
            _incFill = Rect("IncProgressFill", bar, Vector2.zero, Vector2.zero);
            _incFill.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.85f, 1f);

            _incButtons = Rect("IncButtons", r, new Vector2(20, 244), new Vector2(480, 120)).gameObject;
            MakeButton((RectTransform)_incButtons.transform, "ACCEPT", new Vector2(0, 0), new Vector2(230, 110), new Color(0.1f, 0.65f, 0.3f), 40, AcceptIncoming);
            MakeButton((RectTransform)_incButtons.transform, "DECLINE", new Vector2(250, 0), new Vector2(230, 110), new Color(0.75f, 0.2f, 0.2f), 40, DeclineIncoming);
        }

        private void RefreshPeers()
        {
            _peersDirty = false;
            foreach (Button b in _peerButtons) if (b != null) Destroy(b.gameObject);
            _peerButtons.Clear();
            if (_selectedPeer != null && !_peers.Contains(_selectedPeer)) _selectedPeer = null;

            for (int i = 0; i < _peers.Count && i < MaxPeerRows; i++)
            {
                PeerInfo p = _peers[i];
                int index = i;
                bool sel = p == _selectedPeer;
                Button b = MakeButton(_peerListRoot, p.DisplayName, new Vector2(0, i * 62), new Vector2(250, 56),
                    sel ? new Color(0.2f, 0.85f, 1f) : new Color(0.25f, 0.25f, 0.3f), 24, () => SelectPeerByIndex(index));
                if (sel) b.GetComponentInChildren<Text>().color = Color.black;
                _peerButtons.Add(b);
            }

            if (_peers.Count == 0)
            {
                var hint = Label(_peerListRoot, "Searching... (Demo Friend appears in 10s)", Vector2.zero, new Vector2(250, 120), 20, TextAnchor.UpperLeft, new Color(0.8f, 0.8f, 0.85f));
                _peerButtons.Add(hint.gameObject.AddComponent<Button>());
            }
        }

        private void RefreshReceived()
        {
            _receivedDirty = false;
            for (int i = _receivedRoot.childCount - 1; i >= 0; i--) Destroy(_receivedRoot.GetChild(i).gameObject);

            int start = Mathf.Max(0, _receivedPaths.Count - MaxReceivedThumbs);
            for (int i = start; i < _receivedPaths.Count; i++)
            {
                string path = _receivedPaths[i];
                int slot = i - start;
                var pos = new Vector2((slot % 3) * 80, (slot / 3) * 80);
                Button b = MakeButton(_receivedRoot, "", pos, new Vector2(72, 72), new Color(0.25f, 0.25f, 0.3f), 14, () => ShowReceived(path));
                RawImage ri = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(b.transform, false);
                var tr = (RectTransform)ri.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(4, 4); tr.offsetMax = new Vector2(-4, -4);
                ri.raycastTarget = false;
                try { ri.texture = Decode(File.ReadAllBytes(path)); } catch { /* unreadable file: leave blank thumb */ }
            }
        }

        private void LoadExistingReceived()
        {
            if (!Directory.Exists(_receiveDir)) return;
            string[] files = Directory.GetFiles(_receiveDir);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".png") _receivedPaths.Add(f);
            }
        }

        private void ShowReceived(string path)
        {
            try
            {
                _viewer.texture = Decode(File.ReadAllBytes(path));
                _viewer.color = Color.white;
                _viewerCaption.text = Path.GetFileName(path);
            }
            catch (Exception e)
            {
                _viewerCaption.text = "Can't open: " + e.Message;
            }
        }

        // ================= helpers =================

        private Texture2D Decode(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(tex, bytes);
            _textures.Add(tex);
            return tex;
        }

        private static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576f:0.0} MB" : $"{Mathf.Max(1, (int)(bytes / 1024))} KB";

        private static void SetFill(RectTransform fill, float p)
        {
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        private static void PlaceInFront(Transform t, float distance, float yOffset)
        {
            Transform head = Camera.main != null ? Camera.main.transform : null;
            Vector3 origin = head != null ? head.position : Vector3.zero;
            Vector3 fwd = head != null ? head.forward : Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            t.position = origin + fwd * distance + Vector3.up * yOffset;
            t.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 topLeft, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rt.sizeDelta = size;
            return rt;
        }

        private static Text Label(Transform parent, string text, Vector2 topLeft, Vector2 size, int fontSize,
            TextAnchor anchor, Color color, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Rect("Label", parent, topLeft, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        private static Button MakeButton(Transform parent, string label, Vector2 topLeft, Vector2 size, Color color,
            int fontSize, Action onClick)
        {
            RectTransform rt = Rect("Button", parent, topLeft, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());

            Text t = Label(rt, label, Vector2.zero, size, fontSize, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            var tr = (RectTransform)t.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            return btn;
        }
    }
}

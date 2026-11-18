using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BumpXR.Core;
using BumpXR.Discovery;
using BumpXR.Gestures;
using BumpXR.Transfer;
using UnityEngine;
using UnityEngine.UI;

namespace BumpXR.UI
{
    public enum MediaTab { Captures = 1, Moments = 2 }

    /// <summary>
    /// BumpMain app: wires discovery + batch transfer + Demo Friend + My Captures and Moments behind IMediaSource
    /// and builds a hands-only world-space UI.
    /// All buttons are plain UGUI Buttons driven by Meta Interaction SDK hand poke / pinch-ray
    /// (PointableCanvas + PokeInteractable/RayInteractable on the canvases, PointableCanvasModule on the EventSystem).
    /// A fist bump (IBumpDetector) triggers the same Send / Accept code paths as the poke buttons.
    /// </summary>
    public sealed class BumpMainController : MonoBehaviour
    {
        public const int MaxSelectedPhotos = 10;
        public const int VersionPokesToRevealDev = 5;
        private const int GridPageSize = 6;
        private const float AutoDeclineSeconds = 30f;
        private const int MaxPeerRows = 5;
        private const string CoachDoneKey = "BumpXR.CoachDone";
        private const float StatusFlashSeconds = 3f;
        private const float HealthCheckSeconds = 15f;
        private const int MaxDiscoveryRetries = 3;
        private const float NoHandPromptSeconds = 10f;
        public const string HandsPromptMessage = "Put your controllers down. BumpXR uses your hands.";

        [SerializeField] private RectTransform shareRoot;
        [SerializeField] private RectTransform incomingRoot;
        [SerializeField] private string displayName = "";

        // services & media sources
        private LocalNetworkTransferService _transfer;
        private MetaDiscoveryService _meta;
        private DemoFriendService _demo;
        private IDiscoveryService _discovery;
        private ITransferService _transferApi;
        private MomentsMediaSource _momentsMedia;
        private CapturesMediaSource _capturesMedia;
        private IMediaSource _activeMedia;
        private MediaTab _activeTab = MediaTab.Captures;
        private PeerInfo _self;
        private string _ip;
        private string _ipOverride;
        private NetworkReachability? _reachabilityOverride;
        private string _receiveDir;
        private string _lastError = "";
        private string _lastLoggedStatus = "";

        // gesture + feedback
        private BumpSettings _defaultSettings;
        private bool _createdDefaultSettings;
        private BumpSettings _settings;
        private HandBumpDetector _handDetector;
        private IBumpDetector _bump;
        private BumpFeedback _feedback;
        private DemoFriendOrb _orb;
        private float _lastBumpTime = float.NegativeInfinity;
        private bool _lastBumpShareOpen;
        private bool _bumpConsumed = true;
        private float _lastPeak;
        private float _noHandSeconds;

        // state
        private readonly List<PeerInfo> _peers = new List<PeerInfo>();
        private readonly List<Button> _peerButtons = new List<Button>();
        private readonly List<PhotoTile> _photoTiles = new List<PhotoTile>();
        private readonly List<MediaItem> _selectedItems = new List<MediaItem>();
        private readonly Dictionary<string, IMediaSource> _itemSources = new Dictionary<string, IMediaSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly HashSet<(IMediaSource source, string id)> _thumbLoading = new HashSet<(IMediaSource, string)>();
        private readonly Queue<IncomingOffer> _offerQueue = new Queue<IncomingOffer>();
        private PeerInfo _selectedPeer;
        private int _gridPage;
        private bool _gridDirty = true;
        private bool _sending;
        private bool _peersDirty = true;
        private IncomingOffer _current;
        private bool _accepting;
        private float _incomingDeadline;
        private string _flashText = "";
        private float _flashUntil;
        private int _versionPokes;

        // lifecycle / timing
        private bool _suspended;
        private bool _netBusy;
        private int _netGen;
        private int _retries;
        private float _nextHealthCheck;
        private float _tAwake, _tListening, _tAdvertising;
        private bool _loggedInteractive;
        private readonly StringBuilder _timing = new StringBuilder();

        // coach marks
        private bool _coachDone;
        private bool _coachSent;
        private GameObject _coachBanner;
        private Text _coachText;

        // share UI
        private Text _statusLine;
        private Button _devButton;
        private RectTransform _peerListRoot;
        private Text _demoToggleLabel;
        private static readonly Color GallerySurface = new Color32(20, 25, 42, 255);
        private static readonly Color GalleryControl = new Color32(42, 49, 72, 255);
        private static readonly Color GalleryTile = new Color32(30, 37, 57, 255);
        private static readonly Color GalleryAccent = new Color32(118, 230, 179, 255);
        private static readonly Color GalleryMuted = new Color32(177, 187, 209, 255);
        private static readonly Color GalleryInk = new Color32(17, 34, 32, 255);

        private Button _tabCapturesBtn, _tabMomentsBtn, _refreshCapturesBtn, _galleryActionBtn, _clearSelectionBtn;
        private Text _pageLabel;
        private Text _countBadgeLabel;
        private Button _prevPageBtn, _nextPageBtn;
        private Text _tabMessageLabel;
        private Button _sendButton;
        private Text _sendLabel;
        private RectTransform _sendFill, _sendFileFill;
        private Text _sendStatus;
        private Text _aboutText;
        private CanvasGroup _shareGroup;
        private CanvasGroup _shareContent;
        private Sprite _galleryCornerSprite;
        private GameObject _enlargePanel;
        private Texture2D _previewTexture;
        private int _previewVersion;
        private RawImage _enlargeViewer;
        private Text _enlargeCaption;
        private GameObject _devPanel;
        private Text _devText;
        private Text _speedTuneLabel, _curlTuneLabel, _decelTuneLabel, _tuneFooterLabel;

        // incoming UI (no image preview before Accept)
        private Text _incTitle, _incFrom, _incFile, _incInfo;
        private RectTransform _incFill, _incFileFill;
        private GameObject _incButtons;

        private static Font _font;

        private sealed class PhotoTile
        {
            public Button Button;
            public RawImage Thumb;
            public Text Name;
            public Text Placeholder;
            public GameObject Badge;
            public Text BadgeText;
            public MediaItem Item;
        }

        // ---- test/automation surface (same code paths the buttons use) ----
        public int PeerCount => _peers.Count;
        public int ReceivedCount => _momentsMedia?.Items.Count ?? 0;
        public bool IncomingVisible => _current != null;
        public bool IsSending => _sending;
        public string SendStatus => _sendStatus != null ? _sendStatus.text : "";
        public string AboutLine => _aboutText != null ? _aboutText.text : "";
        public string CoachLine => _coachBanner != null && _coachBanner.activeSelf && _coachText != null ? _coachText.text : "";
        public DemoFriendService Demo => _demo;
        public int PhotoCount => _activeMedia?.Items.Count ?? 0;
        public int SelectedCount => _selectedItems.Count;
        public MediaTab ActiveTab => _activeTab;
        public CapturesMediaSource Captures => _capturesMedia;
        public bool IsArmed => !_sending && _selectedPeer != null && _selectedItems.Count > 0 && _current == null;
        public string ArmedText => _sendLabel != null ? _sendLabel.text : "";
        public string CountBadgeText => _countBadgeLabel != null ? _countBadgeLabel.text : "";
        public string TabMessageText => _tabMessageLabel != null && _tabMessageLabel.gameObject.activeSelf ? _tabMessageLabel.text : "";
        public string IncomingSummaryText => _incFrom != null ? _incFrom.text : "";
        public string IncomingNamesText => _incFile != null ? _incFile.text : "";
        public string StatusLine => _statusLine != null ? _statusLine.text : "";
        public int ListenPort => _transferApi?.ListenPort ?? 0;
        public bool IsSuspended => _suspended;
        public bool CoachVisible => _coachBanner != null && _coachBanner.activeSelf;
        public string TimingLog => _timing.ToString();
        public float InteractiveDelaySeconds { get; private set; }
        public BumpSettings Settings => _settings;
        public string LastPrintedTuneLine { get; private set; } = "";
        public bool DevButtonVisible => _devButton != null && _devButton.gameObject.activeSelf;
        public string LastSavedClipPath { get; private set; } = "";

        private void Awake()
        {
            _tAwake = Time.realtimeSinceStartup;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (string.IsNullOrEmpty(displayName)) displayName = Application.isEditor ? "Editor" : SystemInfo.deviceModel;

            _receiveDir = Path.Combine(Application.persistentDataPath, "Received");
            _momentsMedia = new MomentsMediaSource(_receiveDir);
            _capturesMedia = new CapturesMediaSource();
            _activeMedia = _capturesMedia;

            _transfer = new LocalNetworkTransferService(_receiveDir);
            _meta = new MetaDiscoveryService();
            _demo = new DemoFriendService(_meta, _transfer, _receiveDir);
            _discovery = _demo;
            _transferApi = _demo;

            _discovery.PeerFound += OnPeerFound;
            _discovery.ErrorOccurred += e => _lastError = e;
            _transferApi.OfferReceived += OnOfferReceived;
            _transferApi.TokenChanged += () => ReAdvertise();
            _demo.DemoFriendVisibilityChanged += OnDemoVisibility;

            _defaultSettings = Resources.Load<BumpSettings>("BumpSettings");
            if (_defaultSettings == null) { _defaultSettings = ScriptableObject.CreateInstance<BumpSettings>(); _createdDefaultSettings = true; }
            _settings = Instantiate(_defaultSettings);
            _settings.LoadOverrides();
            _handDetector = gameObject.AddComponent<HandBumpDetector>();
            _handDetector.Init(_settings);
            _bump = _handDetector;
            _bump.Bumped += OnBump;
            _feedback = gameObject.AddComponent<BumpFeedback>();
            _orb = new GameObject("DemoFriendOrb").AddComponent<DemoFriendOrb>();
            _orb.Init(_settings);
            _orb.Bumped += e => _feedback.Bump(e.Position, soft: true);

            _coachDone = PlayerPrefs.GetInt(CoachDoneKey, 0) == 1;

            BuildShareUi();
            BuildIncomingUi();
            incomingRoot.gameObject.SetActive(false);
            Log($"UI built in {(Time.realtimeSinceStartup - _tAwake) * 1000f:0} ms (Awake at {_tAwake:0.00}s since app start)");
        }

        private void Start()
        {
            PlaceInFront(shareRoot, 0.6f, -0.03f);
            StartCoroutine(ReplaceAfterTrackingSettles());
            BeginNetworking();
            _ = RefreshCapturesAsync(requestPermission: false);
        }

        private IEnumerator ReplaceAfterTrackingSettles()
        {
            yield return new WaitForSeconds(1.0f);
            PlaceInFront(shareRoot, 0.6f, -0.03f);
        }

        private void Log(string message)
        {
            _timing.AppendLine(message);
            Debug.Log("[BumpXR][timing] " + message);
        }

        /// <summary>Editor / test injection hook for verifying lazy My Captures permission flow without Android JNI.</summary>
        public void SetCapturesBackendForTest(ICapturesBackend backend)
        {
            bool wasActive = _activeTab == MediaTab.Captures;
            _capturesMedia = new CapturesMediaSource(backend);
            if (wasActive)
            {
                _activeMedia = _capturesMedia;
                foreach (Texture2D texture in _thumbCache.Values) Destroy(texture);
                _thumbCache.Clear();
            }
            _gridDirty = true;
        }

        // ================= networking lifecycle =================

        private async void BeginNetworking()
        {
            try { await StartNetworkingAsync(); }
            catch (Exception e) { _lastError = "start: " + e.Message; Debug.LogWarning("[BumpXR] start networking failed: " + e); }
        }

        private async Task StartNetworkingAsync()
        {
            int gen = ++_netGen;
            _netBusy = true;
            try
            {
                _ip = NetworkUtil.GetWifiIPv4() ?? "(none)";
                try
                {
                    _transferApi.StartListening();
                    _tListening = Time.realtimeSinceStartup;
                    Log($"TCP listening on :{_transferApi.ListenPort} at {_tListening:0.00}s since app start");
                }
                catch (Exception e)
                {
                    _lastError = "listen: " + e.Message;
                    Debug.LogWarning("[BumpXR] TCP listen failed: " + e.Message);
                }

                _self = new PeerInfo { DisplayName = displayName, Token = _transferApi.LocalToken, Ip = _ip, Port = _transferApi.ListenPort };
                await _discovery.StartAdvertisingAsync(_self);
                if (gen != _netGen) return;
                _tAdvertising = Time.realtimeSinceStartup;
                Log($"advertising={_discovery.IsAdvertising} at {_tAdvertising:0.00}s since app start");
                await _discovery.StartDiscoveryAsync();
                if (gen != _netGen) return;
                Log($"discovering={_discovery.IsDiscovering} at {Time.realtimeSinceStartup:0.00}s since app start");
            }
            finally
            {
                if (gen == _netGen) _netBusy = false;
            }
        }

        private async void ReAdvertise()
        {
            if (_self == null || _suspended) return;
            _self.Token = _transferApi.LocalToken;
            await _discovery.StopAdvertisingAsync();
            if (_suspended) return;
            await _discovery.StartAdvertisingAsync(_self);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SuspendNetworking(); else ResumeNetworking();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) return;
            ResumeNetworking();
            if (_activeTab == MediaTab.Captures && _capturesMedia != null && _capturesMedia.State != CapturesState.NotOpened)
                _ = RefreshCapturesAsync(requestPermission: false);
        }

        public async void SuspendNetworking()
        {
            if (_suspended) return;
            _suspended = true;
            _netGen++;
            Log($"suspend at {Time.realtimeSinceStartup:0.00}s");
            _transferApi.StopListening();
            while (_offerQueue.Count > 0) { try { _offerQueue.Dequeue().Decline(); } catch { } }
            if (_current != null && !_accepting) { try { _current.Decline(); } catch { } ShowNextOffer(); }
            try { await _discovery.StopAdvertisingAsync(); } catch (Exception e) { _lastError = "stop adv: " + e.Message; }
            try { await _discovery.StopDiscoveryAsync(); } catch (Exception e) { _lastError = "stop disc: " + e.Message; }
        }

        public async void ResumeNetworking()
        {
            if (!_suspended) return;
            _suspended = false;
            _retries = 0;
            Log($"resume at {Time.realtimeSinceStartup:0.00}s");
            _peers.RemoveAll(p => !p.IsDemo);
            _peersDirty = true;
            try { await StartNetworkingAsync(); }
            catch (Exception e) { _lastError = "resume: " + e.Message; Debug.LogWarning("[BumpXR] resume failed: " + e); }
        }

        private async void RestartDiscovery()
        {
            int gen = _netGen;
            try
            {
                Log($"discovery health check: restarting (try {_retries}/{MaxDiscoveryRetries})");
                if (_discovery.IsAdvertising) await _discovery.StopAdvertisingAsync();
                if (_discovery.IsDiscovering) await _discovery.StopDiscoveryAsync();
                if (gen != _netGen || _suspended) return;
                await StartNetworkingAsync();
            }
            catch (Exception e) { _lastError = "restart: " + e.Message; }
        }

        public void RetryNetworking()
        {
            if (_netBusy) return;
            _suspended = false;
            _retries = 0;
            _lastError = "";
            _ipOverride = null;
            _reachabilityOverride = null;
            _transferApi.StopListening();
            RestartDiscovery();
        }

        private void OnDestroy()
        {
            if (_transferApi != null) _transferApi.OfferReceived -= OnOfferReceived;
            _transfer?.Dispose();
            _ = _discovery?.StopAdvertisingAsync();
            _ = _discovery?.StopDiscoveryAsync();
            if (_previewTexture != null) Destroy(_previewTexture);
            if (_galleryCornerSprite != null)
            {
                Destroy(_galleryCornerSprite.texture);
                Destroy(_galleryCornerSprite);
            }
            foreach (Texture2D t in _thumbCache.Values) if (t != null) Destroy(t);
            if (_orb != null) Destroy(_orb.gameObject);
            if (_settings != null) Destroy(_settings);
            if (_createdDefaultSettings && _defaultSettings != null) Destroy(_defaultSettings);
        }

        // ================= discovery / peers =================

        private void OnPeerFound(PeerInfo peer)
        {
            if (!peer.IsDemo) _peers.RemoveAll(p => !p.IsDemo && p.Ip == peer.Ip && p.Port == peer.Port);
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

            _orb.Show(visible);
            _peersDirty = true;
        }

        public void ToggleDemoFriend() => _demo.SetDemoFriendVisible(!_demo.DemoFriendVisible);

        public void SelectPeerByIndex(int index)
        {
            if (index < 0 || index >= _peers.Count) return;
            _selectedPeer = _peers[index];
            _peersDirty = true;
        }

        // ================= tabs & multi-select =================

        public async void OpenTab(MediaTab tab)
        {
            await OpenTabAsync(tab);
        }

        public async Task OpenTabAsync(MediaTab tab)
        {
            if (_activeTab != tab) _gridPage = 0;
            _activeTab = tab;
            _gridDirty = true;
            if (tab == MediaTab.Moments)
            {
                _momentsMedia.Refresh();
                _activeMedia = _momentsMedia;
            }
            else
            {
                _activeMedia = _capturesMedia;
                await RefreshCapturesAsync(requestPermission: false);
            }
        }

        public async Task RefreshCapturesAsync(bool requestPermission)
        {
            CapturesMediaSource source = _capturesMedia;
            Task<bool> load = source.EnsureLoadedAsync(forceRefresh: true, requestPermission: requestPermission);
            if (_activeTab == MediaTab.Captures) _gridDirty = true;
            await load;
            if (this == null || source != _capturesMedia) return;

            if (source.State == CapturesState.Denied || source.State == CapturesState.NeedsPermission)
            {
                _selectedItems.RemoveAll(item =>
                {
                    if (!_itemSources.TryGetValue(item.Id, out IMediaSource selectedSource) || selectedSource != source)
                        return false;
                    _itemSources.Remove(item.Id);
                    return true;
                });
            }
            if (_activeTab == MediaTab.Captures) _gridDirty = true;
        }

        public void ChangePage(int delta)
        {
            int count = _activeMedia?.Items.Count ?? 0;
            int maxPage = Mathf.Max(0, (count - 1) / GridPageSize);
            _gridPage = Mathf.Clamp(_gridPage + delta, 0, maxPage);
            _gridDirty = true;
        }

        /// <summary>Selects item at index in active media source (adds to selection if not already selected).</summary>
        public void SelectPhotoByIndex(int index)
        {
            IReadOnlyList<MediaItem> items = _activeMedia?.Items;
            if (items == null || index < 0 || index >= items.Count) return;
            if (!IsItemSelected(items[index])) ToggleItemSelection(items[index], _activeMedia);
        }

        /// <summary>Hands-only thumbnail poke handler: toggles check badge on/off up to MaxSelectedPhotos (10).</summary>
        public void TogglePhotoSlot(int slot)
        {
            int idx = _gridPage * GridPageSize + slot;
            IReadOnlyList<MediaItem> items = _activeMedia?.Items;
            if (items == null || idx < 0 || idx >= items.Count) return;
            ToggleItemSelection(items[idx], _activeMedia);
        }

        public void SelectAllPhotos()
        {
            IReadOnlyList<MediaItem> items = _activeMedia?.Items;
            if (items == null || items.Count == 0) return;
            for (int i = 0; i < items.Count && _selectedItems.Count < MaxSelectedPhotos; i++)
            {
                MediaItem item = items[i];
                if (!IsItemSelected(item))
                {
                    _selectedItems.Add(item);
                    _itemSources[item.Id] = _activeMedia;
                }
            }
        }

        public void ClearSelection()
        {
            _selectedItems.Clear();
            _itemSources.Clear();
        }

        /// <summary>Adds a received photo to the outgoing selection.</summary>
        public void SelectReceivedAsSource(string path)
        {
            MediaItem item = _momentsMedia.FindOrAddByPath(path);
            if (item == null) return;
            if (!IsItemSelected(item)) ToggleItemSelection(item, _momentsMedia);
        }

        private bool IsItemSelected(MediaItem item) =>
            item != null && _selectedItems.Any(s => s.Id == item.Id);

        private void ToggleItemSelection(MediaItem item, IMediaSource source)
        {
            if (item == null || source == null) return;
            int existing = _selectedItems.FindIndex(s => s.Id == item.Id);
            if (existing >= 0)
            {
                _selectedItems.RemoveAt(existing);
                _itemSources.Remove(item.Id);
                return;
            }

            if (_selectedItems.Count >= MaxSelectedPhotos)
            {
                _sendStatus.text = $"Maximum {MaxSelectedPhotos} photos per bump.";
                return;
            }

            _selectedItems.Add(item);
            _itemSources[item.Id] = source;
        }

        // ================= bump =================

        public void SimulateBump() => _handDetector.SimulateBump();

        private void OnBump(BumpEvent e)
        {
            _feedback.Bump(e.Position);
            _lastPeak = e.PeakSpeed;

            if (_current != null && !_accepting)
            {
                _lastBumpTime = Time.unscaledTime;
                _lastBumpShareOpen = false;
                _bumpConsumed = true;
                if (_current.SenderName == DemoFriendService.FriendName) _orb.MeetFist(e.Position);
                AcceptIncoming();
                return;
            }

            _lastBumpTime = Time.unscaledTime;
            _lastBumpShareOpen = true;
            _bumpConsumed = false;

            // Bump = send: no manual friend selection needed. Prefer a real nearby headset over the Demo Friend.
            if (_selectedPeer == null && !_sending && _current == null && _selectedItems.Count > 0 && _peers.Count > 0)
            {
                _selectedPeer = _peers.LastOrDefault(p => !p.IsDemo) ?? _peers[_peers.Count - 1];
                _peersDirty = true;
            }

            if (IsArmed)
            {
                if (_selectedPeer.IsDemo) _orb.MeetFist(e.Position);
                Send();
            }
            else if (!_sending)
            {
                _sendStatus.text = _selectedItems.Count == 0 ? "Pick a photo, then bump your friend." : "No friend nearby yet - tap Demo Friend or wait for a headset.";
            }
        }

        // ================= sending =================

        public async void Send()
        {
            if (!IsArmed) return;
            _sending = true;
            PeerInfo peer = _selectedPeer;
            List<MediaItem> batch = _selectedItems.Take(MaxSelectedPhotos).ToList();
            SetFill(_sendFill, 0f);
            SetFill(_sendFileFill, 0f);
            _sendStatus.text = batch.Count == 1
                ? $"Sending {batch[0].FileName} to {peer.DisplayName}..."
                : $"Sending {batch.Count} photos to {peer.DisplayName}...";

            try
            {
                var outgoing = new List<OutgoingFile>(batch.Count);
                for (int i = 0; i < batch.Count; i++)
                {
                    MediaItem item = batch[i];
                    IMediaSource source = _itemSources[item.Id];
                    outgoing.Add(new OutgoingFile(item.FileName, item.MimeType, () => source.ReadBytes(item)));
                }

                var progress = new Progress<BatchProgress>(bp =>
                {
                    SetFill(_sendFill, bp.OverallProgress);
                    SetFill(_sendFileFill, bp.FileProgress);
                    if (bp.FileCount > 1)
                        _sendStatus.text = $"Sending {bp.FileIndex + 1}/{bp.FileCount}: {bp.FileName} to {peer.DisplayName}...";
                });

                TransferResult r = await _transferApi.SendBatchAsync(peer, displayName, outgoing, progress);
                _sendStatus.text = r.Success
                    ? (batch.Count == 1 ? $"Sent to {peer.DisplayName}!" : $"Sent {batch.Count} photos to {peer.DisplayName}!")
                    : $"Not sent: {r}";
                if (r.Success)
                {
                    _coachSent = true;
                    Flash("Sent!");
                    _feedback.TransferComplete(shareRoot.position);
                }
                else
                {
                    SetFill(_sendFill, 0f);
                    SetFill(_sendFileFill, 0f);
                }
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
                _orb.StopOffering();
                return;
            }

            ClosePreview();
            _current = _offerQueue.Dequeue();
            _accepting = false;
            _incomingDeadline = Time.unscaledTime + AutoDeclineSeconds;
            int count = _current.Count;
            _incTitle.text = count > 1 ? $"Incoming photos ({count})" : "Incoming photo";
            _incFrom.text = count == 1
                ? $"{_current.SenderName} wants to send 1 photo, {FormatSize(_current.TotalSize)}"
                : $"{_current.SenderName} wants to send {count} photos, {FormatSize(_current.TotalSize)}";
            _incFile.text = FormatSanitizedOfferNames(_current);
            _incInfo.text = $"Bump to accept  -  {AutoDeclineSeconds:0}s";
            SetFill(_incFill, 0f);
            SetFill(_incFileFill, 0f);
            _incButtons.SetActive(true);
            incomingRoot.gameObject.SetActive(true);
            PlaceInFront(incomingRoot, 0.45f, -0.02f);
            var incCanvas = incomingRoot.GetComponent<Canvas>();
            if (incCanvas != null) { incCanvas.overrideSorting = true; incCanvas.sortingOrder = 500; }
            incomingRoot.SetAsLastSibling();
            _shareGroup.blocksRaycasts = false;
            _shareGroup.alpha = 0f;
            if (_current.SenderName == DemoFriendService.FriendName) _orb.Offer();

            if (!_bumpConsumed && BumpSettings.IsSimultaneous(_lastBumpTime, _lastBumpShareOpen, Time.unscaledTime, _settings.simultaneousBumpWindow))
            {
                _bumpConsumed = true;
                AcceptIncoming();
            }
        }

        private static string FormatSanitizedOfferNames(IncomingOffer offer)
        {
            if (offer.Items == null || offer.Items.Count == 0)
                return ReceivedStore.SanitizeFileName(offer.FileName, offer.MimeType);
            const int maxListed = 5;
            IEnumerable<string> shown = offer.Items.Take(maxListed).Select(i => i.SafeName);
            string joined = string.Join(", ", shown);
            return offer.Items.Count > maxListed ? $"{joined} (+{offer.Items.Count - maxListed} more)" : joined;
        }

        public void DeclineIncoming()
        {
            if (_current == null || _accepting) return;
            _current.Decline();
            _sendStatus.text = "Declined from " + _current.SenderName;
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
                var progress = new Progress<BatchProgress>(bp =>
                {
                    SetFill(_incFill, bp.OverallProgress);
                    SetFill(_incFileFill, bp.FileProgress);
                    _incInfo.text = bp.FileCount > 1
                        ? $"Receiving {bp.FileIndex + 1}/{bp.FileCount}: {bp.FileName}"
                        : $"Receiving {bp.FileName}...";
                });

                IReadOnlyList<string> paths = await offer.AcceptBatchAsync(progress);
                if (paths != null && paths.Count > 0)
                {
                    _momentsMedia.Refresh();
                    if (_activeTab == MediaTab.Moments) _gridDirty = true;
                    string newest = paths[paths.Count - 1];
                    _sendStatus.text = paths.Count == 1
                        ? $"Received {Path.GetFileName(newest)} from {offer.SenderName}"
                        : $"Received {paths.Count} photos from {offer.SenderName}";
                    Flash("Received!");
                    _feedback.TransferComplete(shareRoot.position);
                }
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

        private void Flash(string text)
        {
            _flashText = text;
            _flashUntil = Time.unscaledTime + StatusFlashSeconds;
        }

        // ================= update =================

        private void Update()
        {
            if (!_loggedInteractive)
            {
                _loggedInteractive = true;
                InteractiveDelaySeconds = Time.realtimeSinceStartup - _tAwake;
                Log($"panel interactive at {Time.realtimeSinceStartup:0.00}s since app start ({InteractiveDelaySeconds * 1000f:0} ms after Awake; target < 5 s)");
            }

            if (!_suspended && _current == null)
            {
                if (_handDetector != null && _handDetector.TrackedHandCount > 0) _noHandSeconds = 0f;
                else _noHandSeconds += Time.unscaledDeltaTime;
            }

            if (_current != null && !_accepting)
            {
                float left = _incomingDeadline - Time.unscaledTime;
                if (left <= 0f) { DeclineIncoming(); }
                else _incInfo.text = $"Bump to accept  -  {Mathf.CeilToInt(left)}s";
            }

            if (_peersDirty) RefreshPeers();
            if (_gridDirty) RefreshPhotoGrid();

            UpdateTabAndSelectionVisuals();

            bool armed = IsArmed;
            _sendButton.interactable = armed;
            if (armed)
            {
                int n = _selectedItems.Count;
                _sendLabel.text = $"{n} {(n == 1 ? "photo" : "photos")} to {_selectedPeer.DisplayName}";
            }
            else
            {
                _sendLabel.text = _sending ? "Sending..." : "Send";
            }

            UpdateStatusLine(armed);
            UpdateCoach();
            if (_devPanel.activeSelf) UpdateDevOverlay();
            if (_settings != null && !_suspended && !_netBusy && Time.unscaledTime >= _nextHealthCheck)
            {
                _nextHealthCheck = Time.unscaledTime + HealthCheckSeconds;
                if (_retries < MaxDiscoveryRetries && Time.realtimeSinceStartup - _tAwake > HealthCheckSeconds &&
                    (!_discovery.IsAdvertising || !_discovery.IsDiscovering))
                {
                    _retries++;
                    RestartDiscovery();
                }
            }

            _demoToggleLabel.text = _demo.DemoFriendVisible ? "Demo Friend: ON" : "Demo Friend: OFF";
        }

        private void UpdateTabAndSelectionVisuals()
        {
            StyleTab(_tabCapturesBtn, _activeTab == MediaTab.Captures);
            StyleTab(_tabMomentsBtn, _activeTab == MediaTab.Moments);

            _countBadgeLabel.text = $"{_selectedItems.Count}/{MaxSelectedPhotos} selected";
            _clearSelectionBtn.interactable = _selectedItems.Count > 0;

            for (int i = 0; i < _photoTiles.Count; i++)
            {
                PhotoTile tile = _photoTiles[i];
                if (!tile.Button.gameObject.activeSelf || tile.Item == null) continue;
                int order = _selectedItems.FindIndex(s => s.Id == tile.Item.Id);
                bool sel = order >= 0;
                tile.Button.image.color = sel ? GalleryAccent : GalleryTile;
                tile.Name.color = sel ? GalleryInk : Color.white;
                tile.Badge.SetActive(sel);
                if (sel) tile.BadgeText.text = (order + 1).ToString();
            }

        }

        private static void StyleTab(Button btn, bool active)
        {
            btn.image.color = active ? GalleryAccent : GalleryControl;
            btn.GetComponentInChildren<Text>().color = active ? GalleryInk : Color.white;
        }

        private bool NoHandPromptActive =>
            !_suspended && _current == null && _noHandSeconds >= NoHandPromptSeconds &&
            (_handDetector == null || _handDetector.TrackedHandCount == 0);

        public static string DiagnoseFailure(string ip, NetworkReachability reachability, string discoveryError)
        {
            if (string.IsNullOrEmpty(ip) || ip == "(none)")
                return "No Wi-Fi — connect Wi-Fi or use Demo Friend";
            string err = discoveryError ?? "";
            if (err.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                err.IndexOf("NoDiscoveryMethod", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Bluetooth off — turn on or use Demo Friend";
            if (reachability == NetworkReachability.NotReachable ||
                err.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                err.IndexOf("Internet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                err.IndexOf("Cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                return "No internet — connect or use Demo Friend";
            if (!string.IsNullOrEmpty(err))
                return "Enable Spatial Services, or use Demo Friend";
            return null;
        }

        public void SimulateColdStartState(string ipOverride, NetworkReachability? reachabilityOverride, string discoveryError, float noHandsSeconds = 0f)
        {
            _ipOverride = ipOverride;
            _reachabilityOverride = reachabilityOverride;
            _lastError = discoveryError ?? "";
            _noHandSeconds = noHandsSeconds;
            _netBusy = false;
            UpdateStatusLine(IsArmed);
            UpdateCoach();
        }

        private void UpdateStatusLine(bool armed)
        {
            string fail = _netBusy ? null : DiagnoseFailure(_ipOverride ?? _ip, _reachabilityOverride ?? Application.internetReachability, _lastError);
            bool hasRealPeer = _peers.Any(p => !p.IsDemo);
            string text;
            Color color;
            if (_current != null) { text = _accepting ? "Receiving..." : "Incoming: bump to accept"; color = new Color(1f, 0.85f, 0.3f); }
            else if (_sending) { text = "Sending..."; color = new Color(0.3f, 0.9f, 1f); }
            else if (Time.unscaledTime < _flashUntil) { text = _flashText; color = new Color(0.4f, 1f, 0.55f); }
            else if (armed) { text = "Armed: bump to send"; color = new Color(1f, 0.85f, 0.3f); }
            else if (hasRealPeer) { text = "Friend nearby"; color = new Color(0.4f, 1f, 0.8f); }
            else if (fail != null && _selectedPeer == null) { text = fail; color = new Color(1f, 0.78f, 0.35f); }
            else if (NoHandPromptActive && _selectedPeer == null) { text = HandsPromptMessage; color = new Color(1f, 0.78f, 0.35f); }
            else if (_peers.Count > 0) { text = "Friend nearby"; color = new Color(0.4f, 1f, 0.8f); }
            else { text = "Searching..."; color = new Color(0.75f, 0.85f, 1f); }

            _statusLine.fontSize = text.Length > 34 ? 20 : (text.Length > 22 ? 24 : 32);
            _statusLine.text = text;
            _statusLine.color = color;
            if (text != _lastLoggedStatus)
            {
                _lastLoggedStatus = text;
                Debug.Log("[BumpXR][status] " + text);
            }
        }

        private void UpdateCoach()
        {
            if (NoHandPromptActive)
            {
                _coachBanner.SetActive(true);
                _coachText.fontSize = 26;
                _coachText.text = HandsPromptMessage;
                return;
            }

            _coachText.fontSize = 34;
            if (_coachDone) { if (_coachBanner.activeSelf) _coachBanner.SetActive(false); return; }

            string step = null;
            if (_selectedItems.Count == 0) step = "Pick a photo";
            else if (_selectedPeer == null) step = "Pick a friend";
            else if (!_coachSent) step = "Bump to send";

            if (step == null)
            {
                _coachDone = true;
                PlayerPrefs.SetInt(CoachDoneKey, 1);
                PlayerPrefs.Save();
                _coachBanner.SetActive(false);
                return;
            }

            _coachBanner.SetActive(true);
            _coachText.text = step;
        }

        // ================= Dev unlock, runtime tuning & clip export =================

        public void PokeVersionText()
        {
            _versionPokes++;
            if (_versionPokes >= VersionPokesToRevealDev && _devButton != null)
                _devButton.gameObject.SetActive(true);
        }

        public string SaveBumpClip(string label, string directoryOverride = null)
        {
            LastSavedClipPath = _handDetector.SaveClip(label, directoryOverride);
            UpdateTuneLabels("Clip: " + LastSavedClipPath);
            return LastSavedClipPath;
        }

        public void AdjustSpeed(float delta)
        {
            _settings.speedThreshold = Mathf.Clamp(Mathf.Round((_settings.speedThreshold + delta) * 100f) / 100f, 0.30f, 2.50f);
            _settings.SaveOverrides();
            UpdateTuneLabels("Saved to PlayerPrefs");
        }

        public void AdjustCurl(float delta)
        {
            _settings.fistCurlDegrees = Mathf.Clamp(Mathf.Round(_settings.fistCurlDegrees + delta), 140f, 240f);
            _settings.SaveOverrides();
            UpdateTuneLabels("Saved to PlayerPrefs");
        }

        public void AdjustDecel(float delta)
        {
            _settings.decelRatio = Mathf.Clamp(Mathf.Round((_settings.decelRatio + delta) * 100f) / 100f, 0.05f, 0.85f);
            _settings.SaveOverrides();
            UpdateTuneLabels("Saved to PlayerPrefs");
        }

        public void ResetTuning()
        {
            _settings.ResetToDefaults(_defaultSettings);
            UpdateTuneLabels("Reset to BumpSettings.asset defaults");
        }

        public void PrintTuning()
        {
            LastPrintedTuneLine = _settings.FormatTuneLine();
            Debug.Log(LastPrintedTuneLine);
            UpdateTuneLabels("Logged: " + LastPrintedTuneLine);
        }

        private void UpdateTuneLabels(string note = null)
        {
            if (_speedTuneLabel != null) _speedTuneLabel.text = $"Speed: {_settings.speedThreshold:0.00} m/s";
            if (_curlTuneLabel != null) _curlTuneLabel.text = $"Curl: {_settings.fistCurlDegrees:0} deg";
            if (_decelTuneLabel != null) _decelTuneLabel.text = $"Decel: {_settings.decelRatio:0.00}";
            if (note != null && _tuneFooterLabel != null) _tuneFooterLabel.text = note;
        }

        private void UpdateDevOverlay()
        {
            string err = string.IsNullOrEmpty(_lastError) ? "-" : (_lastError.Length > 55 ? _lastError.Substring(0, 55) : _lastError);
            float ago = Time.unscaledTime - _lastBumpTime;
            string dbg = string.IsNullOrEmpty(_handDetector.LastDebugLine) ? "BUMPDBG (none yet)" : _handDetector.LastDebugLine;
            _devText.text =
                $"Speed L {_bump.LeftSpeed:0.00}  R {_bump.RightSpeed:0.00} m/s   (need {_settings.speedThreshold:0.00})   ring {_handDetector.ClipSampleCount}\n" +
                $"Detector: {_bump.StateLabel}   cooldown {_bump.CooldownLeft:0.0}s   hands {_handDetector.TrackedHandCount}\n" +
                $"Last bump: {(float.IsNegativeInfinity(_lastBumpTime) ? "none" : $"{_lastPeak:0.00} m/s, {ago:0.0}s ago")}   Orb: {_orb.StateLabel}\n" +
                $"{dbg}\n" +
                $"Wi-Fi {(_ipOverride ?? _ip)}:{_transferApi.ListenPort}   Adv {_discovery.IsAdvertising}  Disc {_discovery.IsDiscovering}   Err: {err}";
            UpdateTuneLabels();
        }

        // ================= UI building =================

        private void BuildShareUi()
        {
            if (!shareRoot.TryGetComponent(out _shareGroup)) _shareGroup = shareRoot.gameObject.AddComponent<CanvasGroup>();
            RectTransform r = Rect("ShareContent", shareRoot, Vector2.zero, shareRoot.rect.size);
            _shareContent = r.gameObject.AddComponent<CanvasGroup>();

            // Header: title, status line, Dev toggle (hidden until 5 pokes on version text)
            Label(r, "BumpXR", new Vector2(20, 8), new Vector2(240, 50), 40, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _statusLine = Label(r, "Searching...", new Vector2(260, 8), new Vector2(520, 50), 32, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _devButton = MakeButton(r, "Dev", new Vector2(800, 12), new Vector2(80, 42), new Color(0.3f, 0.3f, 0.35f), 22, () => _devPanel.SetActive(!_devPanel.activeSelf));
            _devButton.gameObject.SetActive(false);

            // Coach banner
            RectTransform banner = Rect("Coach", r, new Vector2(20, 62), new Vector2(860, 48));
            banner.gameObject.AddComponent<Image>().color = new Color(1f, 0.8f, 0.2f, 0.95f);
            _coachText = Label(banner, "", Vector2.zero, new Vector2(860, 48), 34, TextAnchor.MiddleCenter, Color.black, FontStyle.Bold);
            _coachBanner = banner.gameObject;
            _coachBanner.SetActive(!_coachDone);

            Label(r, "Nearby", new Vector2(20, 118), new Vector2(128, 36), 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            MakeButton(r, "Retry", new Vector2(154, 114), new Vector2(76, 42), GalleryControl, 19, RetryNetworking);
            _peerListRoot = Rect("Peers", r, new Vector2(20, 164), new Vector2(210, 250));
            Button demoBtn = MakeButton(r, "Demo Friend", new Vector2(20, 420), new Vector2(210, 46), GalleryControl, 21, ToggleDemoFriend);
            _demoToggleLabel = demoBtn.GetComponentInChildren<Text>();

            BuildGallery(r);

            _sendButton = MakeButton(r, "Send", new Vector2(20, 474), new Vector2(210, 48), GalleryAccent, 24, Send);
            _sendLabel = _sendButton.GetComponentInChildren<Text>();
            _sendLabel.color = GalleryInk;
            _sendLabel.resizeTextForBestFit = true;
            _sendLabel.resizeTextMinSize = 15;
            _sendLabel.resizeTextMaxSize = 24;
            _sendLabel.rectTransform.offsetMin = new Vector2(8, 0);
            _sendLabel.rectTransform.offsetMax = new Vector2(-8, 0);

            RectTransform bar = Rect("ProgressBack", r, new Vector2(20, 526), new Vector2(210, 5));
            bar.gameObject.AddComponent<Image>().color = GalleryTile;
            _sendFill = Rect("ProgressFill", bar, Vector2.zero, Vector2.zero);
            _sendFill.gameObject.AddComponent<Image>().color = GalleryAccent;
            SetFill(_sendFill, 0f);

            RectTransform fileBar = Rect("FileProgressBack", r, new Vector2(20, 533), new Vector2(210, 3));
            fileBar.gameObject.AddComponent<Image>().color = GalleryTile;
            _sendFileFill = Rect("FileProgressFill", fileBar, Vector2.zero, Vector2.zero);
            _sendFileFill.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.85f, 1f);
            SetFill(_sendFileFill, 0f);

            _sendStatus = Label(r, "Select photos and a friend, then bump or press Send.", new Vector2(20, 538), new Vector2(860, 28), 21, TextAnchor.MiddleLeft, Color.white);
            _sendStatus.supportRichText = false;
            Button verBtn = MakeButton(r, $"v{Application.version} — Photos stay on your devices. Sent over local Wi-Fi only after the receiver accepts.",
                new Vector2(20, 566), new Vector2(860, 28), new Color(0f, 0f, 0f, 0.01f), 16, PokeVersionText);
            _aboutText = verBtn.GetComponentInChildren<Text>();
            _aboutText.alignment = TextAnchor.MiddleLeft;
            _aboutText.fontStyle = FontStyle.Normal;
            _aboutText.color = new Color(0.72f, 0.78f, 0.88f);

            // Dev overlay: 3 existing +/- controls + Reset/Print/Sim bump + Save clip FALSE/TRUE + path line
            RectTransform dev = Rect("DevOverlay", r, new Vector2(280, 112), new Vector2(600, 452));
            dev.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.97f);
            _devText = Label(dev, "", new Vector2(12, 6), new Vector2(576, 186), 16, TextAnchor.UpperLeft, new Color(0.7f, 1f, 0.8f));

            Color stepBtn = new Color(0.22f, 0.34f, 0.52f);
            MakeButton(dev, "-", new Vector2(12, 198), new Vector2(64, 44), stepBtn, 26, () => AdjustSpeed(-0.05f));
            _speedTuneLabel = Label(dev, "", new Vector2(82, 198), new Vector2(186, 44), 19, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            MakeButton(dev, "+", new Vector2(274, 198), new Vector2(64, 44), stepBtn, 26, () => AdjustSpeed(0.05f));
            MakeButton(dev, "Reset", new Vector2(360, 198), new Vector2(228, 44), new Color(0.45f, 0.26f, 0.26f), 21, ResetTuning);

            MakeButton(dev, "-", new Vector2(12, 248), new Vector2(64, 44), stepBtn, 26, () => AdjustCurl(-5f));
            _curlTuneLabel = Label(dev, "", new Vector2(82, 248), new Vector2(186, 44), 19, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            MakeButton(dev, "+", new Vector2(274, 248), new Vector2(64, 44), stepBtn, 26, () => AdjustCurl(5f));
            MakeButton(dev, "Print", new Vector2(360, 248), new Vector2(228, 44), new Color(0.2f, 0.55f, 0.35f), 21, PrintTuning);

            MakeButton(dev, "-", new Vector2(12, 298), new Vector2(64, 44), stepBtn, 26, () => AdjustDecel(-0.05f));
            _decelTuneLabel = Label(dev, "", new Vector2(82, 298), new Vector2(186, 44), 19, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            MakeButton(dev, "+", new Vector2(274, 298), new Vector2(64, 44), stepBtn, 26, () => AdjustDecel(0.05f));
            MakeButton(dev, "Sim bump", new Vector2(360, 298), new Vector2(228, 44), new Color(0.2f, 0.5f, 0.8f), 21, SimulateBump);

            MakeButton(dev, "Save clip: FALSE", new Vector2(12, 348), new Vector2(282, 44), new Color(0.55f, 0.30f, 0.16f), 20, () => SaveBumpClip("FALSE"));
            MakeButton(dev, "Save clip: TRUE", new Vector2(306, 348), new Vector2(282, 44), new Color(0.18f, 0.52f, 0.38f), 20, () => SaveBumpClip("TRUE"));

            _tuneFooterLabel = Label(dev, "Poke +/- to tune live; Print logs BUMPTUNE; Save clip writes 3 s JSON.",
                new Vector2(12, 396), new Vector2(576, 50), 14, TextAnchor.MiddleLeft, new Color(0.78f, 0.86f, 0.96f));
            UpdateTuneLabels();
            _devPanel = dev.gameObject;
            _devPanel.SetActive(false);

            BuildPhotoPreview();
        }

        private void BuildGallery(RectTransform parent)
        {
            _galleryCornerSprite = CreateGalleryCornerSprite();
            RectTransform gallery = Rect("PhotoGallery", parent, new Vector2(248, 114), new Vector2(632, 424));
            Image background = gallery.gameObject.AddComponent<Image>();
            background.color = GallerySurface;
            background.raycastTarget = false;
            RoundGalleryImage(background);

            _tabCapturesBtn = MakeButton(gallery, "My Captures", new Vector2(16, 14), new Vector2(166, 44), GalleryAccent, 22, () => OpenTab(MediaTab.Captures));
            _tabMomentsBtn = MakeButton(gallery, "Moments", new Vector2(190, 14), new Vector2(134, 44), GalleryControl, 22, () => OpenTab(MediaTab.Moments));
            _refreshCapturesBtn = MakeButton(gallery, "Refresh", new Vector2(506, 14), new Vector2(110, 44), GalleryControl, 20,
                () => _ = RefreshCapturesAsync(requestPermission: false));

            for (int slot = 0; slot < GridPageSize; slot++)
            {
                int index = slot;
                var position = new Vector2(16 + (index % 3) * 204, 70 + (index / 3) * 144);
                Button button = MakeButton(gallery, "", position, new Vector2(192, 134), GalleryTile, 20, () => TogglePhotoSlot(index));
                RectTransform thumbRect = Rect("Thumbnail", button.transform, new Vector2(4, 4), new Vector2(184, 90));
                RawImage thumb = thumbRect.gameObject.AddComponent<RawImage>();
                thumb.raycastTarget = false;
                Text placeholder = Label(thumbRect, "", Vector2.zero, new Vector2(184, 90), 18, TextAnchor.MiddleCenter, GalleryMuted);

                RectTransform badge = Rect("SelectionOrder", button.transform, new Vector2(150, 9), new Vector2(32, 32));
                Image badgeImage = badge.gameObject.AddComponent<Image>();
                badgeImage.color = GalleryAccent;
                badgeImage.raycastTarget = false;
                RoundGalleryImage(badgeImage);
                Text badgeText = Label(badge, "", Vector2.zero, new Vector2(32, 32), 19, TextAnchor.MiddleCenter, GalleryInk, FontStyle.Bold);
                badge.gameObject.SetActive(false);

                Text name = Label(button.transform, "", new Vector2(8, 94), new Vector2(110, 40), 18, TextAnchor.MiddleLeft, Color.white);
                name.supportRichText = false;
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                MakeButton(button.transform, "View", new Vector2(122, 96), new Vector2(66, 34), GalleryControl, 18, () =>
                {
                    if (_photoTiles[index].Item is MediaItem item) _ = PreviewPhotoAsync(item, _activeMedia);
                });

                _photoTiles.Add(new PhotoTile { Button = button, Thumb = thumb, Name = name, Placeholder = placeholder, Badge = badge.gameObject, BadgeText = badgeText });
            }

            _tabMessageLabel = Label(gallery, "", new Vector2(70, 126), new Vector2(492, 86), 24, TextAnchor.MiddleCenter, GalleryMuted);
            _galleryActionBtn = MakeButton(gallery, "Allow access", new Vector2(214, 230), new Vector2(204, 48), GalleryAccent, 22,
                () => _ = RefreshCapturesAsync(requestPermission: true));
            _galleryActionBtn.GetComponentInChildren<Text>().color = GalleryInk;

            _countBadgeLabel = Label(gallery, "", new Vector2(16, 366), new Vector2(155, 44), 21, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _clearSelectionBtn = MakeButton(gallery, "Clear", new Vector2(174, 366), new Vector2(80, 44), GalleryControl, 20, ClearSelection);
            _prevPageBtn = MakeButton(gallery, "Back", new Vector2(354, 366), new Vector2(80, 44), GalleryControl, 20, () => ChangePage(-1));
            _pageLabel = Label(gallery, "", new Vector2(442, 366), new Vector2(84, 44), 20, TextAnchor.MiddleCenter, GalleryMuted);
            _nextPageBtn = MakeButton(gallery, "Next", new Vector2(536, 366), new Vector2(80, 44), GalleryControl, 20, () => ChangePage(1));

            foreach (Button button in gallery.GetComponentsInChildren<Button>(true))
                RoundGalleryImage(button.image);
        }

        private static Sprite CreateGalleryCornerSprite()
        {
            const int size = 20;
            const float radius = 8f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius));
                    float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius));
                    byte alpha = (byte)(255f * Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy)));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, Vector4.one * radius);
        }

        private void RoundGalleryImage(Image image)
        {
            image.sprite = _galleryCornerSprite;
            image.type = Image.Type.Sliced;
        }

        private void BuildPhotoPreview()
        {
            RectTransform panel = Rect("PhotoPreview", shareRoot, Vector2.zero, shareRoot.rect.size);
            panel.gameObject.AddComponent<Image>().color = new Color32(13, 17, 30, 255);
            _enlargeCaption = Label(panel, "", new Vector2(28, 16), new Vector2(720, 56), 24, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _enlargeCaption.supportRichText = false;
            MakeButton(panel, "Close", new Vector2(756, 16), new Vector2(116, 56), GalleryControl, 23, ClosePreview);
            RectTransform image = Rect("Preview", panel, new Vector2(28, 88), new Vector2(844, 484));
            _enlargeViewer = image.gameObject.AddComponent<RawImage>();
            _enlargeViewer.raycastTarget = false;
            _enlargePanel = panel.gameObject;
            _enlargePanel.SetActive(false);
        }

        private void BuildIncomingUi()
        {
            RectTransform r = incomingRoot;
            _incTitle = Label(r, "Incoming photo", new Vector2(20, 10), new Vector2(480, 44), 38, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            _incFrom = Label(r, "", new Vector2(20, 56), new Vector2(480, 42), 25, TextAnchor.MiddleLeft, new Color(0.6f, 0.9f, 1f), FontStyle.Bold);
            _incFile = Label(r, "", new Vector2(20, 100), new Vector2(480, 62), 20, TextAnchor.UpperLeft, Color.white);
            _incInfo = Label(r, "", new Vector2(20, 164), new Vector2(480, 42), 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.3f), FontStyle.Bold);

            RectTransform bar = Rect("IncProgressBack", r, new Vector2(20, 212), new Vector2(480, 14));
            bar.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f);
            _incFill = Rect("IncProgressFill", bar, Vector2.zero, Vector2.zero);
            _incFill.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.85f, 1f);

            RectTransform fileBar = Rect("IncFileProgressBack", r, new Vector2(20, 230), new Vector2(480, 10));
            fileBar.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f);
            _incFileFill = Rect("IncFileProgressFill", fileBar, Vector2.zero, Vector2.zero);
            _incFileFill.gameObject.AddComponent<Image>().color = new Color(0.4f, 1f, 0.6f);

            _incButtons = Rect("IncButtons", r, new Vector2(20, 252), new Vector2(480, 112)).gameObject;
            MakeButton((RectTransform)_incButtons.transform, "ACCEPT", new Vector2(0, 0), new Vector2(230, 108), new Color(0.1f, 0.65f, 0.3f), 40, AcceptIncoming);
            MakeButton((RectTransform)_incButtons.transform, "DECLINE", new Vector2(250, 0), new Vector2(230, 108), new Color(0.75f, 0.2f, 0.2f), 40, DeclineIncoming);
        }

        private void RefreshPhotoGrid()
        {
            _gridDirty = false;
            IReadOnlyList<MediaItem> items = _activeMedia.Items;
            bool captures = _activeTab == MediaTab.Captures;
            CapturesState state = _capturesMedia.State;
            bool busy = captures && (state == CapturesState.RequestingPermission || state == CapturesState.Loading);
            bool needsAccess = captures && (state == CapturesState.NeedsPermission || state == CapturesState.Denied);
            int maxPage = Mathf.Max(0, (items.Count - 1) / GridPageSize);
            _gridPage = Mathf.Clamp(_gridPage, 0, maxPage);
            bool hasPhotos = items.Count > 0;
            _prevPageBtn.gameObject.SetActive(hasPhotos);
            _nextPageBtn.gameObject.SetActive(hasPhotos);
            _prevPageBtn.interactable = _gridPage > 0;
            _nextPageBtn.interactable = _gridPage < maxPage;
            _pageLabel.text = hasPhotos ? $"{_gridPage + 1} / {maxPage + 1}" : "";
            _refreshCapturesBtn.gameObject.SetActive(captures && !needsAccess);
            _refreshCapturesBtn.interactable = !busy;
            _galleryActionBtn.gameObject.SetActive(needsAccess);
            _galleryActionBtn.GetComponentInChildren<Text>().text =
                state == CapturesState.Denied ? "Try again" : "Allow access";

            _tabMessageLabel.gameObject.SetActive(!hasPhotos);
            _tabMessageLabel.text = captures
                ? _capturesMedia.StatusMessage
                : "Photos you receive from a bump will appear here.";

            for (int slot = 0; slot < GridPageSize; slot++)
            {
                PhotoTile tile = _photoTiles[slot];
                int index = _gridPage * GridPageSize + slot;
                MediaItem item = index < items.Count ? items[index] : null;
                tile.Item = item;
                tile.Button.gameObject.SetActive(item != null);
                tile.Thumb.texture = null;
                if (item == null) continue;

                tile.Name.text = FitFileName(tile.Name, item.FileName);
                if (_thumbCache.TryGetValue(item.Id, out Texture2D cached))
                    ShowThumbnail(tile, cached);
                else
                {
                    tile.Thumb.color = GallerySurface;
                    tile.Placeholder.text = "Loading...";
                    _ = LoadTileThumbnailAsync(item, _activeMedia);
                }
            }

            // Bound the texture cache to one visible page, including received full-size JPEGs/PNGs.
            foreach (string id in _thumbCache.Keys.Where(id => !_photoTiles.Any(tile => tile.Item?.Id == id)).ToArray())
            {
                Destroy(_thumbCache[id]);
                _thumbCache.Remove(id);
            }
        }

        private async Task LoadTileThumbnailAsync(MediaItem item, IMediaSource source)
        {
            var key = (source, item.Id);
            if (!_thumbLoading.Add(key)) return;
            try
            {
                byte[] bytes = await source.ReadThumbnailBytesAsync(item);
                if (this == null || source != _activeMedia) return;
                PhotoTile tile = _photoTiles.Find(photo => photo.Item?.Id == item.Id);
                if (tile == null) return;
                Texture2D texture = DecodePhoto(bytes);
                if (texture != null) _thumbCache[item.Id] = texture;
                ShowThumbnail(tile, texture);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BumpXR] Thumbnail load failed: " + e.Message);
                if (this != null && source == _activeMedia)
                {
                    PhotoTile tile = _photoTiles.Find(photo => photo.Item?.Id == item.Id);
                    if (tile != null) ShowThumbnail(tile, null);
                }
            }
            finally
            {
                _thumbLoading.Remove(key);
            }
        }

        private static void ShowThumbnail(PhotoTile tile, Texture2D texture)
        {
            tile.Thumb.texture = texture;
            tile.Thumb.color = texture != null ? Color.white : GallerySurface;
            tile.Thumb.uvRect = new UnityEngine.Rect(0, 0, 1, 1);
            tile.Placeholder.text = texture != null ? "" : "No preview";

            if (texture == null) return;
            float imageAspect = (float)texture.width / texture.height;
            float tileAspect = 184f / 90f;
            float width = Mathf.Min(1f, tileAspect / imageAspect);
            float height = Mathf.Min(1f, imageAspect / tileAspect);
            tile.Thumb.uvRect = new UnityEngine.Rect((1f - width) / 2f, (1f - height) / 2f, width, height);
        }

        private static string FitFileName(Text label, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Photo";
            var generator = label.cachedTextGeneratorForLayout;
            var settings = label.GetGenerationSettings(Vector2.zero);
            float maxWidth = label.rectTransform.rect.width * label.pixelsPerUnit;
            if (generator.GetPreferredWidth(fileName, settings) <= maxWidth) return fileName;

            string shortened = fileName.Substring(0, Mathf.Min(14, fileName.Length));
            while (shortened.Length > 0 && generator.GetPreferredWidth(shortened + "...", settings) > maxWidth)
                shortened = shortened.Substring(0, shortened.Length - 1);
            return shortened + "...";
        }

        private static Texture2D DecodePhoto(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, bytes, markNonReadable: true)) return texture;
            Destroy(texture);
            return null;
        }

        private async Task PreviewPhotoAsync(MediaItem item, IMediaSource source)
        {
            int version = ++_previewVersion;
            _enlargeCaption.text = "Loading photo...";
            _enlargeViewer.color = Color.clear;
            _enlargePanel.SetActive(true);
            _shareContent.blocksRaycasts = false;
            try
            {
                byte[] bytes = source is CapturesMediaSource captures
                    ? await captures.ReadPreviewBytesAsync(item, 1024)
                    : await source.ReadThumbnailBytesAsync(item);
                if (this == null || version != _previewVersion) return;

                _previewTexture = DecodePhoto(bytes);
                if (_previewTexture == null)
                {
                    _enlargeCaption.text = "This photo couldn't be opened.";
                    return;
                }

                float scale = Mathf.Min(844f / _previewTexture.width, 484f / _previewTexture.height);
                Vector2 size = new Vector2(_previewTexture.width * scale, _previewTexture.height * scale);
                _enlargeViewer.rectTransform.sizeDelta = size;
                _enlargeViewer.rectTransform.anchoredPosition = new Vector2((900f - size.x) / 2f, -88f - (484f - size.y) / 2f);
                _enlargeViewer.texture = _previewTexture;
                _enlargeViewer.color = Color.white;
                _enlargeCaption.text = item.FileName;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BumpXR] Photo preview failed: " + e.Message);
                if (this != null && version == _previewVersion) _enlargeCaption.text = "This photo couldn't be opened.";
            }
        }

        private void ClosePreview()
        {
            _previewVersion++;
            _enlargePanel.SetActive(false);
            _shareContent.blocksRaycasts = true;
            _enlargeViewer.texture = null;
            if (_previewTexture != null) Destroy(_previewTexture);
            _previewTexture = null;
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
                Button b = MakeButton(_peerListRoot, p.DisplayName, new Vector2(0, i * 50), new Vector2(210, 44),
                    sel ? GalleryAccent : GalleryControl, 21, () => SelectPeerByIndex(index));
                if (sel) b.GetComponentInChildren<Text>().color = Color.black;
                _peerButtons.Add(b);
            }

            if (_peers.Count == 0)
            {
                var hint = Label(_peerListRoot, "Looking for nearby headsets...", Vector2.zero, new Vector2(210, 100), 21, TextAnchor.UpperLeft, GalleryMuted);
                _peerButtons.Add(hint.gameObject.AddComponent<Button>());
            }
        }

        // ================= helpers =================

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

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
    public enum MediaTab { Samples, Captures, Moments }

    /// <summary>
    /// BumpMain app: wires discovery + batch transfer + Demo Friend + Samples/My Captures/Moments behind IMediaSource
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
        private const int MaxMoments = 12;
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
        private SampleMediaSource _samplesMedia;
        private MomentsMediaSource _momentsMedia;
        private CapturesMediaSource _capturesMedia;
        private IMediaSource _activeMedia;
        private MediaTab _activeTab = MediaTab.Samples;
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
        private readonly List<string> _receivedPaths = new List<string>();
        private readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly HashSet<string> _thumbLoading = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<Button, string>> _momentButtons = new List<KeyValuePair<Button, string>>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly Queue<IncomingOffer> _offerQueue = new Queue<IncomingOffer>();
        private PeerInfo _selectedPeer;
        private int _gridPage;
        private bool _gridDirty = true;
        private bool _sending;
        private bool _peersDirty = true;
        private bool _receivedDirty = true;
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
        private Button _tabSamplesBtn, _tabCapturesBtn, _tabMomentsBtn;
        private Text _countBadgeLabel;
        private Button _prevPageBtn, _nextPageBtn;
        private Text _tabMessageLabel;
        private Button _sendButton;
        private Text _sendLabel;
        private RectTransform _sendFill, _sendFileFill;
        private Text _sendStatus;
        private Text _aboutText;
        private RawImage _viewer;
        private Text _viewerCaption;
        private RectTransform _momentsRoot;
        private CanvasGroup _shareGroup;
        private GameObject _enlargePanel;
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
            public GameObject Badge;
            public Text BadgeText;
            public MediaItem Item;
        }

        // ---- test/automation surface (same code paths the buttons use) ----
        public int PeerCount => _peers.Count;
        public int ReceivedCount => _receivedPaths.Count;
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
            _samplesMedia = new SampleMediaSource();
            _momentsMedia = new MomentsMediaSource(_receiveDir);
            _capturesMedia = new CapturesMediaSource();
            _activeMedia = _samplesMedia;

            _transfer = new LocalNetworkTransferService(_receiveDir);
            _meta = new MetaDiscoveryService();
            _demo = new DemoFriendService(_meta, _transfer, _samplesMedia, _receiveDir);
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
            if (wasActive) _activeMedia = _capturesMedia;
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
            if (focus) ResumeNetworking();
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
            _transfer?.Dispose();
            _ = _discovery?.StopAdvertisingAsync();
            _ = _discovery?.StopDiscoveryAsync();
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
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
            _activeTab = tab;
            _gridPage = 0;
            switch (tab)
            {
                case MediaTab.Samples:
                    _activeMedia = _samplesMedia;
                    _gridDirty = true;
                    break;
                case MediaTab.Moments:
                    _momentsMedia.Refresh();
                    _activeMedia = _momentsMedia;
                    _gridDirty = true;
                    break;
                case MediaTab.Captures:
                    _activeMedia = _capturesMedia;
                    _gridDirty = true;
                    await _capturesMedia.EnsureLoadedAsync();
                    if (_activeTab == MediaTab.Captures) _gridDirty = true;
                    break;
            }
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

        /// <summary>Re-send source: poking a Moment enlarges it and selects it via MomentsMediaSource.</summary>
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
            if (item == null) return;
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

        private byte[] ReadItemBytes(MediaItem item)
        {
            if (_itemSources.TryGetValue(item.Id, out IMediaSource src) && src != null)
                return src.ReadBytes(item);
            return _samplesMedia.ReadBytes(item);
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

            if (IsArmed)
            {
                if (_selectedPeer.IsDemo) _orb.MeetFist(e.Position);
                Send();
            }
            else if (!_sending)
            {
                _sendStatus.text = _selectedItems.Count == 0 ? "Pick a photo, then a friend, then bump." : "Pick a friend, then bump.";
            }
        }

        // ================= sending =================

        public async void Send()
        {
            if (_sending || _selectedPeer == null || _selectedItems.Count == 0 || _current != null) return;
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
                    outgoing.Add(new OutgoingFile(item.FileName, item.MimeType, () => ReadItemBytes(item)));
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
            _shareGroup.blocksRaycasts = false;
            _shareGroup.alpha = 0.35f;
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
                    for (int i = 0; i < paths.Count; i++) _receivedPaths.Add(paths[i]);
                    _momentsMedia.Refresh();
                    _receivedDirty = true;
                    if (_activeTab == MediaTab.Moments) _gridDirty = true;
                    string newest = paths[paths.Count - 1];
                    ShowReceived(newest);
                    _sendStatus.text = paths.Count == 1
                        ? $"Received {Path.GetFileName(newest)} from {offer.SenderName}"
                        : $"Received {paths.Count} photos from {offer.SenderName}";
                    Flash("Received!");
                    _feedback.TransferComplete(_viewer.transform.position);
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
            if (_receivedDirty) RefreshReceived();
            if (_gridDirty) RefreshPhotoGrid();

            UpdateTabAndSelectionVisuals();

            bool armed = IsArmed;
            _sendButton.interactable = armed;
            if (armed)
            {
                int n = _selectedItems.Count;
                _sendLabel.text = $"{n} {(n == 1 ? "photo" : "photos")} to {_selectedPeer.DisplayName}";
                _sendLabel.fontSize = _sendLabel.text.Length > 24 ? 22 : 26;
            }
            else
            {
                _sendLabel.text = "SEND";
                _sendLabel.fontSize = 36;
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
            StyleTab(_tabSamplesBtn, _activeTab == MediaTab.Samples);
            StyleTab(_tabCapturesBtn, _activeTab == MediaTab.Captures);
            StyleTab(_tabMomentsBtn, _activeTab == MediaTab.Moments);

            _countBadgeLabel.text = $"{_selectedItems.Count}/{MaxSelectedPhotos}";

            for (int i = 0; i < _photoTiles.Count; i++)
            {
                PhotoTile tile = _photoTiles[i];
                if (!tile.Button.gameObject.activeSelf || tile.Item == null) continue;
                int order = _selectedItems.FindIndex(s => s.Id == tile.Item.Id);
                bool sel = order >= 0;
                tile.Button.image.color = sel ? new Color(0.2f, 0.85f, 1f) : new Color(0.25f, 0.25f, 0.3f);
                tile.Badge.SetActive(sel);
                if (sel) tile.BadgeText.text = (order + 1).ToString();
            }

            foreach (KeyValuePair<Button, string> m in _momentButtons)
            {
                if (m.Key == null) continue;
                string id = "moment:" + Path.GetFullPath(m.Value);
                bool sel = _selectedItems.Any(s => s.Id == id);
                m.Key.image.color = sel ? new Color(0.2f, 0.85f, 1f) : new Color(0.25f, 0.25f, 0.3f);
            }
        }

        private static void StyleTab(Button btn, bool active)
        {
            if (btn == null) return;
            btn.image.color = active ? new Color(0.2f, 0.85f, 1f) : new Color(0.22f, 0.24f, 0.3f);
            var txt = btn.GetComponentInChildren<Text>();
            if (txt != null) txt.color = active ? Color.black : Color.white;
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
            RectTransform r = shareRoot;

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

            // Column A: Nearby + Retry + Demo Friend
            Label(r, "Nearby", new Vector2(20, 118), new Vector2(155, 36), 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            MakeButton(r, "Retry", new Vector2(180, 116), new Vector2(90, 38), new Color(0.22f, 0.35f, 0.5f), 20, RetryNetworking);
            _peerListRoot = Rect("Peers", r, new Vector2(20, 156), new Vector2(250, 5 * 62));
            Button demoBtn = MakeButton(r, "Demo Friend", new Vector2(20, 478), new Vector2(250, 56), new Color(0.35f, 0.25f, 0.55f), 24, ToggleDemoFriend);
            _demoToggleLabel = demoBtn.GetComponentInChildren<Text>();

            // Column B: Tabs (Samples | My Captures | Moments) + Multi-select controls + 3x2 paged grid
            _tabSamplesBtn = MakeButton(r, "Samples", new Vector2(286, 114), new Vector2(102, 36), new Color(0.2f, 0.85f, 1f), 19, () => OpenTab(MediaTab.Samples));
            _tabCapturesBtn = MakeButton(r, "My Captures", new Vector2(392, 114), new Vector2(132, 36), new Color(0.22f, 0.24f, 0.3f), 18, () => OpenTab(MediaTab.Captures));
            _tabMomentsBtn = MakeButton(r, "Moments", new Vector2(528, 114), new Vector2(96, 36), new Color(0.22f, 0.24f, 0.3f), 19, () => OpenTab(MediaTab.Moments));

            RectTransform countPill = Rect("CountBadge", r, new Vector2(286, 154), new Vector2(76, 32));
            countPill.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.20f, 0.30f);
            _countBadgeLabel = Label(countPill, "0/10", Vector2.zero, new Vector2(76, 32), 18, TextAnchor.MiddleCenter, new Color(0.4f, 0.95f, 1f), FontStyle.Bold);

            MakeButton(r, "Select all", new Vector2(366, 154), new Vector2(104, 32), new Color(0.20f, 0.36f, 0.50f), 17, SelectAllPhotos);
            MakeButton(r, "Clear", new Vector2(474, 154), new Vector2(72, 32), new Color(0.36f, 0.24f, 0.28f), 17, ClearSelection);
            _prevPageBtn = MakeButton(r, "<", new Vector2(552, 154), new Vector2(34, 32), new Color(0.24f, 0.26f, 0.34f), 18, () => ChangePage(-1));
            _nextPageBtn = MakeButton(r, ">", new Vector2(590, 154), new Vector2(34, 32), new Color(0.24f, 0.26f, 0.34f), 18, () => ChangePage(1));

            for (int i = 0; i < GridPageSize; i++)
            {
                int slot = i;
                var pos = new Vector2(286 + (i % 3) * 115, 192 + (i / 3) * 92);
                Button b = MakeButton(r, "", pos, new Vector2(108, 86), new Color(0.25f, 0.25f, 0.3f), 18, () => TogglePhotoSlot(slot));
                RawImage thumb = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                thumb.transform.SetParent(b.transform, false);
                var tr = (RectTransform)thumb.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(5, 5); tr.offsetMax = new Vector2(-5, -5);
                thumb.raycastTarget = false;

                RectTransform badge = Rect("CheckBadge", b.transform, new Vector2(76, 4), new Vector2(28, 28));
                badge.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.85f, 0.45f, 0.98f);
                Text badgeTxt = Label(badge, "1", Vector2.zero, new Vector2(28, 28), 16, TextAnchor.MiddleCenter, Color.black, FontStyle.Bold);
                badge.gameObject.SetActive(false);

                _photoTiles.Add(new PhotoTile { Button = b, Thumb = thumb, Badge = badge.gameObject, BadgeText = badgeTxt });
            }

            _tabMessageLabel = Label(r, "", new Vector2(286, 198), new Vector2(338, 172), 20, TextAnchor.MiddleCenter, new Color(0.95f, 0.88f, 0.65f), FontStyle.Bold);
            _tabMessageLabel.gameObject.SetActive(false);

            // Armed / Send button + overall & per-file progress + status + 5-poke version line
            _sendButton = MakeButton(r, "SEND", new Vector2(286, 384), new Vector2(338, 84), new Color(0.1f, 0.6f, 0.3f), 36, Send);
            _sendLabel = _sendButton.GetComponentInChildren<Text>();

            RectTransform bar = Rect("ProgressBack", r, new Vector2(286, 474), new Vector2(338, 12));
            bar.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f);
            _sendFill = Rect("ProgressFill", bar, Vector2.zero, Vector2.zero);
            _sendFill.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.85f, 1f);
            SetFill(_sendFill, 0f);

            RectTransform fileBar = Rect("FileProgressBack", r, new Vector2(286, 490), new Vector2(338, 8));
            fileBar.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f);
            _sendFileFill = Rect("FileProgressFill", fileBar, Vector2.zero, Vector2.zero);
            _sendFileFill.gameObject.AddComponent<Image>().color = new Color(0.4f, 1f, 0.6f);
            SetFill(_sendFileFill, 0f);

            _sendStatus = Label(r, "Pick photos and a friend, then bump (or press SEND).", new Vector2(20, 538), new Vector2(860, 28), 21, TextAnchor.MiddleLeft, Color.white);
            Button verBtn = MakeButton(r, $"v{Application.version} — Photos stay on your devices. Sent over local Wi-Fi only after the receiver accepts.",
                new Vector2(20, 566), new Vector2(860, 28), new Color(0f, 0f, 0f, 0.01f), 16, PokeVersionText);
            _aboutText = verBtn.GetComponentInChildren<Text>();
            _aboutText.alignment = TextAnchor.MiddleLeft;
            _aboutText.fontStyle = FontStyle.Normal;
            _aboutText.color = new Color(0.72f, 0.78f, 0.88f);

            // Column C: Recent Moments preview (last 12 received; poke to enlarge + add to selection)
            Label(r, "Moments", new Vector2(640, 118), new Vector2(240, 36), 28, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            RectTransform viewerBg = Rect("ViewerBg", r, new Vector2(640, 156), new Vector2(240, 150));
            viewerBg.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f);
            _viewer = new GameObject("Viewer", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _viewer.transform.SetParent(viewerBg, false);
            var vr = (RectTransform)_viewer.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = new Vector2(4, 4); vr.offsetMax = new Vector2(-4, -4);
            _viewer.color = new Color(1, 1, 1, 0);
            _viewerCaption = Label(r, "Poke a moment to enlarge", new Vector2(640, 310), new Vector2(240, 26), 18, TextAnchor.MiddleLeft, new Color(0.8f, 0.8f, 0.85f));
            _momentsRoot = Rect("Moments", r, new Vector2(640, 340), new Vector2(240, 180));

            LoadExistingReceived();

            // Enlarged Moment modal
            RectTransform enl = Rect("MomentEnlarged", r, new Vector2(150, 114), new Vector2(600, 430));
            enl.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.09f, 0.98f);
            _enlargeViewer = new GameObject("EnlargeImage", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _enlargeViewer.transform.SetParent(enl, false);
            var er = (RectTransform)_enlargeViewer.transform;
            er.anchorMin = er.anchorMax = er.pivot = new Vector2(0, 1);
            er.anchoredPosition = new Vector2(16, -12);
            er.sizeDelta = new Vector2(568, 344);
            _enlargeViewer.raycastTarget = false;
            _enlargeCaption = Label(enl, "", new Vector2(16, 366), new Vector2(400, 52), 22, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            MakeButton(enl, "Close", new Vector2(434, 364), new Vector2(150, 54), new Color(0.25f, 0.5f, 0.8f), 26, () => _enlargePanel.SetActive(false));
            _enlargePanel = enl.gameObject;
            _enlargePanel.SetActive(false);

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
            IReadOnlyList<MediaItem> items = _activeMedia?.Items ?? Array.Empty<MediaItem>();
            int maxPage = Mathf.Max(0, (items.Count - 1) / GridPageSize);
            _gridPage = Mathf.Clamp(_gridPage, 0, maxPage);
            _prevPageBtn.interactable = _gridPage > 0;
            _nextPageBtn.interactable = _gridPage < maxPage;

            string emptyOrErrorMsg = null;
            if (_activeTab == MediaTab.Captures && !string.IsNullOrEmpty(_capturesMedia.StatusMessage))
                emptyOrErrorMsg = _capturesMedia.StatusMessage;
            else if (_activeTab == MediaTab.Moments && items.Count == 0)
                emptyOrErrorMsg = "No received Moments yet — bump with Demo Friend or another headset first.";

            _tabMessageLabel.gameObject.SetActive(!string.IsNullOrEmpty(emptyOrErrorMsg) && items.Count == 0);
            if (_tabMessageLabel.gameObject.activeSelf) _tabMessageLabel.text = emptyOrErrorMsg;

            for (int slot = 0; slot < GridPageSize; slot++)
            {
                PhotoTile tile = _photoTiles[slot];
                int idx = _gridPage * GridPageSize + slot;
                if (idx >= items.Count)
                {
                    tile.Item = null;
                    tile.Button.gameObject.SetActive(false);
                    continue;
                }

                MediaItem item = items[idx];
                tile.Item = item;
                tile.Button.gameObject.SetActive(true);
                if (_thumbCache.TryGetValue(item.Id, out Texture2D cached) && cached != null)
                {
                    tile.Thumb.texture = cached;
                    tile.Thumb.color = Color.white;
                }
                else
                {
                    tile.Thumb.texture = null;
                    tile.Thumb.color = new Color(0.18f, 0.20f, 0.25f, 1f);
                    LoadTileThumbnailAsync(item, _activeMedia);
                }
            }
        }

        private async void LoadTileThumbnailAsync(MediaItem item, IMediaSource source)
        {
            if (item == null || source == null || !_thumbLoading.Add(item.Id)) return;
            try
            {
                byte[] bytes = await source.ReadThumbnailBytesAsync(item);
                if (this == null || bytes == null || bytes.Length == 0) return;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(tex, bytes))
                {
                    _thumbCache[item.Id] = tex;
                    for (int i = 0; i < _photoTiles.Count; i++)
                    {
                        if (_photoTiles[i].Item != null && _photoTiles[i].Item.Id == item.Id)
                        {
                            _photoTiles[i].Thumb.texture = tex;
                            _photoTiles[i].Thumb.color = Color.white;
                        }
                    }
                }
                else
                {
                    Destroy(tex);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BumpXR] Thumbnail decode failed: " + e.Message);
            }
            finally
            {
                _thumbLoading.Remove(item.Id);
            }
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
            for (int i = _momentsRoot.childCount - 1; i >= 0; i--) Destroy(_momentsRoot.GetChild(i).gameObject);
            _momentButtons.Clear();

            int shown = Mathf.Min(MaxMoments, _receivedPaths.Count);
            for (int slot = 0; slot < shown; slot++)
            {
                string path = _receivedPaths[_receivedPaths.Count - 1 - slot];
                var pos = new Vector2((slot % 4) * 60, (slot / 4) * 60);
                Button b = MakeButton(_momentsRoot, "", pos, new Vector2(56, 56), new Color(0.25f, 0.25f, 0.3f), 14,
                    () => { SelectReceivedAsSource(path); ShowReceived(path, enlarge: true); });
                RawImage ri = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                ri.transform.SetParent(b.transform, false);
                var tr = (RectTransform)ri.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(3, 3); tr.offsetMax = new Vector2(-3, -3);
                ri.raycastTarget = false;
                ri.texture = CachedPathThumb(path);
                _momentButtons.Add(new KeyValuePair<Button, string>(b, path));
            }
        }

        private void LoadExistingReceived()
        {
            if (!Directory.Exists(_receiveDir)) return;
            foreach (string f in Directory.GetFiles(_receiveDir).OrderBy(x => File.GetLastWriteTimeUtc(x)).ThenBy(y => y, StringComparer.Ordinal))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png") _receivedPaths.Add(f);
            }
        }

        private void ShowReceived(string path, bool enlarge = false)
        {
            try
            {
                Texture2D tex = CachedPathThumb(path);
                _viewer.texture = tex;
                _viewer.color = Color.white;
                _viewerCaption.text = Path.GetFileName(path);
                if (enlarge && _enlargePanel != null)
                {
                    _enlargeViewer.texture = tex;
                    _enlargeCaption.text = Path.GetFileName(path) + "  (added to selection)";
                    _enlargePanel.SetActive(true);
                }
            }
            catch (Exception e)
            {
                _viewerCaption.text = "Can't open: " + e.Message;
            }
        }

        // ================= helpers =================

        private Texture2D CachedPathThumb(string path)
        {
            string key = "file:" + Path.GetFullPath(path);
            if (_thumbCache.TryGetValue(key, out Texture2D tex) && tex != null) return tex;
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { ImageConversion.LoadImage(tex, File.ReadAllBytes(path)); } catch { }
            _thumbCache[key] = tex;
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

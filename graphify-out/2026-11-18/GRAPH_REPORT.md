# Graph Report - BumpXR  (2026-11-08)

## Corpus Check
- 26 files · ~24,177 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 2 file(s) not represented in the graph (top: .asmdef 2)

## Summary
- 663 nodes · 1582 edges · 29 communities (22 shown, 6 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 114 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `11fbd32f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TransferLoopbackTests.cs
- DemoFriendService
- BumpMainController
- CapturesMediaSource
- BumpXRBuildTools
- TransferLoopbackTests
- TransferResult
- BumpGestureTests
- IncomingOffer
- Handshake Stub
- .BuildShareUi
- BumpFeedback
- IDiscoveryService
- DemoFriendOrb
- LocalNetworkTransferService
- .RefreshReceived
- PeerInfo
- .ShowNextOffer
- DiscoveryDebugPanel
- MetaDiscoveryService
- .Build
- MediaTab
- BumpDetectorCore
- TransferStatus
- .PlaceInFront
- bumpxr_core
- bumpxr_transfer
- MediaItem

## God Nodes (most connected - your core abstractions)
1. `BumpMainController` - 144 edges
2. `BumpDetectorCore` - 37 edges
3. `LocalNetworkTransferService` - 36 edges
4. `BumpGestureTests` - 32 edges
5. `TransferLoopbackTests` - 30 edges
6. `MediaItem` - 28 edges
7. `DemoFriendService` - 27 edges
8. `PeerInfo` - 24 edges
9. `CapturesMediaSource` - 24 edges
10. `HandBumpDetector` - 23 edges

## Surprising Connections (you probably didn't know these)
- `DemoFriendOrb` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/DemoFriendOrb.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs
- `BumpGestureTests` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Tests/PlayMode/BumpGestureTests.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs
- `BumpMainController` --references--> `BumpSettings`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/BumpMainController.cs → Assets/BumpXR/Scripts/Core/BumpSettings.cs
- `BumpGestureTests` --references--> `BumpSettings`  [EXTRACTED]
  Assets/BumpXR/Tests/PlayMode/BumpGestureTests.cs → Assets/BumpXR/Scripts/Core/BumpSettings.cs
- `BumpMainController` --references--> `IBumpDetector`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/BumpMainController.cs → Assets/BumpXR/Scripts/Core/IBumpDetector.cs

## Import Cycles
- None detected.

## Communities (29 total, 6 thin omitted)

### Community 1 - "TransferLoopbackTests.cs"
Cohesion: 0.11
Nodes (31): BumpXR.Gestures, BumpXR.UI, BumpXR.Core, BumpXR.Discovery, BumpXR.EditorTools, BumpXR.Transfer, BumpXR.Tests, nunit_framework (+23 more)

### Community 2 - "DemoFriendService"
Cohesion: 0.14
Nodes (11): ITransferService, ListenPort, LocalToken, DemoFriendService, DemoFriendVisible, Friend, IsAdvertising, IsDiscovering (+3 more)

### Community 3 - "BumpMainController"
Cohesion: 0.05
Nodes (36): BumpMainController, AboutLine, ActiveTab, ArmedText, Captures, CoachLine, CoachVisible, CountBadgeText (+28 more)

### Community 4 - "CapturesMediaSource"
Cohesion: 0.06
Nodes (26): AndroidJavaObject, AndroidCapturesBackend, CapturesMediaSource, FullReadCount, Items, PermissionRequestCount, State, StatusMessage (+18 more)

### Community 5 - "BumpXRBuildTools"
Cohesion: 0.08
Nodes (20): BumpXRBuildTools, Canvas, Color, GameObject, Hand, Image, MonoBehaviour, RectTransform (+12 more)

### Community 6 - "TransferLoopbackTests"
Cohesion: 0.13
Nodes (19): OutgoingFile, Func, FakeCapturesBackend, RawWireHeader, RawWireItem, SyncProgress, TransferLoopbackTests, Action (+11 more)

### Community 7 - "TransferResult"
Cohesion: 0.18
Nodes (9): TransferResult, Success, StubTransferService, ListenPort, LocalToken, CancellationToken, IProgress, IReadOnlyList (+1 more)

### Community 8 - "BumpGestureTests"
Cohesion: 0.14
Nodes (13): BumpGestureTests, Func, IEnumerator, Quaternion, RectTransform, Test, UnityTest, Vector3 (+5 more)

### Community 9 - "IncomingOffer"
Cohesion: 0.29
Nodes (8): IncomingOffer, Count, TotalSize, OfferItemInfo, CancellationToken, IProgress, IReadOnlyList, Task

### Community 11 - ".BuildShareUi"
Cohesion: 0.21
Nodes (13): PhotoTile, Action, Color, GameObject, Image, RawImage, Text, Transform (+5 more)

### Community 12 - "BumpFeedback"
Cohesion: 0.20
Nodes (11): BumpFeedback, Collider, Color, GameObject, IEnumerator, Renderer, Vector3, AudioClip (+3 more)

### Community 13 - "IDiscoveryService"
Cohesion: 0.13
Nodes (5): IDiscoveryService, IsAdvertising, IsDiscovering, Task, Task

### Community 14 - "DemoFriendOrb"
Cohesion: 0.17
Nodes (10): DemoFriendOrb, CooldownLeft, LeftSpeed, RightSpeed, Shown, StateLabel, Collider, IEnumerator (+2 more)

### Community 15 - "LocalNetworkTransferService"
Cohesion: 0.11
Nodes (18): BatchProgress, Header, LocalNetworkTransferService, ListenPort, LocalToken, ManifestItem, PendingOffer, CancellationToken (+10 more)

### Community 17 - "PeerInfo"
Cohesion: 0.21
Nodes (8): PeerInfo, Guid, DemoIncomingOffer, CancellationToken, IProgress, IReadOnlyList, List, Task

### Community 19 - "DiscoveryDebugPanel"
Cohesion: 0.15
Nodes (8): NetworkUtil, DiscoveryDebugPanel, Dictionary, Font, Guid, IEnumerator, Text, MonoBehaviour

### Community 20 - "MetaDiscoveryService"
Cohesion: 0.27
Nodes (7): MetaDiscoveryService, IsAdvertising, IsDiscovering, Payload, Task, Data, Payload

### Community 21 - ".Build"
Cohesion: 0.29
Nodes (6): DiscoverySceneBuilder, Canvas, Font, GameObject, Image, Text

### Community 22 - "MediaTab"
Cohesion: 0.33
Nodes (4): MediaTab, Captures, Moments, Samples

### Community 23 - "BumpDetectorCore"
Cohesion: 0.05
Nodes (46): BumpClip, BumpClipHand, BumpClipRecorder, Count, BumpClipSample, BumpDetectorCore, CooldownLeft, LastDebugLine (+38 more)

### Community 24 - "TransferStatus"
Cohesion: 0.33
Nodes (6): TransferStatus, Completed, Declined, Failed, Rejected, TimedOut

### Community 31 - "MediaItem"
Cohesion: 0.11
Nodes (18): IMediaSource, Items, MediaItem, IReadOnlyList, Task, MomentsMediaSource, Items, Dictionary (+10 more)

## Knowledge Gaps
- **96 isolated node(s):** `Count`, `LeftSpeed`, `RightSpeed`, `State`, `CooldownLeft` (+91 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 210 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **6 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BumpMainController` connect `BumpMainController` to `.BumpMain_ColdStartFailures_AndRuntimeTuning_NeverDeadEnd`, `TransferLoopbackTests.cs`, `DemoFriendService`, `CapturesMediaSource`, `BumpXRBuildTools`, `TransferLoopbackTests`, `BumpGestureTests`, `IncomingOffer`, `.BuildShareUi`, `BumpFeedback`, `IDiscoveryService`, `DemoFriendOrb`, `LocalNetworkTransferService`, `.RefreshReceived`, `PeerInfo`, `.ShowNextOffer`, `DiscoveryDebugPanel`, `MetaDiscoveryService`, `MediaTab`, `BumpDetectorCore`, `.PlaceInFront`, `MediaItem`?**
  _High betweenness centrality (0.579) - this node is a cross-community bridge._
- **Why does `LocalNetworkTransferService` connect `LocalNetworkTransferService` to `TransferLoopbackTests.cs`, `DemoFriendService`, `BumpMainController`, `TransferLoopbackTests`, `DiscoveryDebugPanel`, `BumpDetectorCore`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **Why does `BumpDetectorCore` connect `BumpDetectorCore` to `BumpGestureTests`, `TransferLoopbackTests.cs`, `DemoFriendOrb`?**
  _High betweenness centrality (0.093) - this node is a cross-community bridge._
- **What connects `Count`, `LeftSpeed`, `RightSpeed` to the rest of the system?**
  _96 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `TransferLoopbackTests.cs` be split into smaller, more focused modules?**
  _Cohesion score 0.10844155844155844 - nodes in this community are weakly interconnected._
- **Should `DemoFriendService` be split into smaller, more focused modules?**
  _Cohesion score 0.13970588235294118 - nodes in this community are weakly interconnected._
- **Should `BumpMainController` be split into smaller, more focused modules?**
  _Cohesion score 0.051418439716312055 - nodes in this community are weakly interconnected._
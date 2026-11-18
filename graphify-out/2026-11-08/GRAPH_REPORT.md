# Graph Report - BumpXR  (2026-11-08)

## Corpus Check
- 28 files · ~24,228 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 601 nodes · 1565 edges · 34 communities (16 shown, 18 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 162 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `11fbd32f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Source Files & Assemblies
- DemoFriendService
- BumpMainController
- MediaItem
- HandBumpDetector
- TransferLoopbackTests
- DemoFriendService
- BumpGestureTests
- DemoFriendService.SendBatchAsync
- Handshake Stub
- DemoFriendOrb
- BumpMainController.StartNetworkingAsync
- LocalNetworkTransferService
- IDiscoveryService
- DiscoveryDebugPanel
- MetaDiscoveryService
- BumpXRBuildTools.Dump
- BumpDetectorCore
- HandBumpDetector
- Vector3
- BumpMainController.Awake
- MediaItem
- BumpClip

## God Nodes (most connected - your core abstractions)
1. `BumpMainController` - 143 edges
2. `BumpDetectorCore` - 38 edges
3. `LocalNetworkTransferService` - 36 edges
4. `BumpGestureTests` - 32 edges
5. `TransferLoopbackTests` - 30 edges
6. `DemoFriendService` - 27 edges
7. `CapturesMediaSource` - 24 edges
8. `HandBumpDetector` - 23 edges
9. `BumpXRBuildTools` - 20 edges
10. `DemoFriendOrb` - 20 edges

## Surprising Connections (you probably didn't know these)
- `FakeCapturesBackend` --implements--> `ICapturesBackend`  [EXTRACTED]
  Assets/BumpXR/Tests/PlayMode/TransferLoopbackTests.cs → Assets/BumpXR/Scripts/Discovery/CapturesMediaSource.cs
- `BumpMainController` --references--> `CapturesMediaSource`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/BumpMainController.cs → Assets/BumpXR/Scripts/Discovery/CapturesMediaSource.cs
- `BumpMainController` --references--> `MomentsMediaSource`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/BumpMainController.cs → Assets/BumpXR/Scripts/Discovery/MomentsMediaSource.cs
- `BumpMainController` --references--> `IncomingOffer`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/BumpMainController.cs → Assets/BumpXR/Scripts/Core/ITransferService.cs
- `DemoFriendOrb` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/DemoFriendOrb.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs

## Import Cycles
- None detected.

## Communities (34 total, 18 thin omitted)

### Community 1 - "Source Files & Assemblies"
Cohesion: 0.10
Nodes (8): DiscoverySceneBuilder, BumpXR.Gestures, BumpXR.UI, BumpXR.Core, BumpXR.Discovery, BumpXR.EditorTools, BumpXR.Transfer, BumpXR.Tests

### Community 2 - "DemoFriendService"
Cohesion: 0.15
Nodes (7): DemoFriendService, DemoFriendVisible, Friend, IsAdvertising, IsDiscovering, ListenPort, LocalToken

### Community 3 - "BumpMainController"
Cohesion: 0.05
Nodes (30): BumpMainController, AboutLine, ActiveTab, ArmedText, Captures, CoachLine, CoachVisible, CountBadgeText (+22 more)

### Community 4 - "MediaItem"
Cohesion: 0.06
Nodes (23): AndroidCapturesBackend, CapturesMediaSource, FullReadCount, Items, PermissionRequestCount, State, StatusMessage, CapturesState (+15 more)

### Community 6 - "TransferLoopbackTests"
Cohesion: 0.21
Nodes (6): OutgoingFile, FakeCapturesBackend, RawWireHeader, RawWireItem, SyncProgress, TransferLoopbackTests

### Community 7 - "DemoFriendService"
Cohesion: 0.16
Nodes (6): ITransferService, ListenPort, LocalToken, StubTransferService, ListenPort, LocalToken

### Community 9 - "DemoFriendService.SendBatchAsync"
Cohesion: 0.10
Nodes (16): BatchProgress, IncomingOffer, Count, TotalSize, OfferItemInfo, TransferResult, Success, TransferStatus (+8 more)

### Community 12 - "DemoFriendOrb"
Cohesion: 0.07
Nodes (12): IBumpDetector, CooldownLeft, LeftSpeed, RightSpeed, StateLabel, BumpFeedback, DemoFriendOrb, CooldownLeft (+4 more)

### Community 15 - "LocalNetworkTransferService"
Cohesion: 0.14
Nodes (5): Header, LocalNetworkTransferService, ListenPort, LocalToken, ManifestItem

### Community 18 - "IDiscoveryService"
Cohesion: 0.24
Nodes (3): IDiscoveryService, IsAdvertising, IsDiscovering

### Community 20 - "MetaDiscoveryService"
Cohesion: 0.26
Nodes (4): MetaDiscoveryService, IsAdvertising, IsDiscovering, Payload

### Community 23 - "BumpDetectorCore"
Cohesion: 0.14
Nodes (10): BumpDetectorCore, CooldownLeft, LastDebugLine, LastRejectReason, LeftSpeed, RightSpeed, State, HeadSample (+2 more)

### Community 24 - "HandBumpDetector"
Cohesion: 0.12
Nodes (8): HandBumpDetector, ClipSampleCount, CooldownLeft, LastDebugLine, LeftSpeed, RightSpeed, StateLabel, TrackedHandCount

### Community 27 - "Vector3"
Cohesion: 0.28
Nodes (3): BumpClipHand, BumpClipSample, BumpEvent

### Community 31 - "MediaItem"
Cohesion: 0.31
Nodes (5): IMediaSource, Items, MediaItem, SampleMediaSource, Items

### Community 32 - "BumpClip"
Cohesion: 0.48
Nodes (3): BumpClip, BumpClipRecorder, Count

## Knowledge Gaps
- **94 isolated node(s):** `NotOpened`, `RequestingPermission`, `Loading`, `Ready`, `Empty` (+89 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 156 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **18 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BumpMainController` connect `BumpMainController` to `BumpMainController.BuildShareUi`, `Source Files & Assemblies`, `DemoFriendService`, `MediaItem`, `HandBumpDetector`, `TransferLoopbackTests`, `DemoFriendService`, `BumpGestureTests`, `DemoFriendService.SendBatchAsync`, `BumpMainController.MakeButton`, `DemoFriendOrb`, `BumpMainController.StartNetworkingAsync`, `LocalNetworkTransferService`, `BumpMainController.RefreshReceived`, `IDiscoveryService`, `DiscoveryDebugPanel`, `BumpXRBuildTools.Dump`, `BumpMainController.Update`, `HandBumpDetector`, `BumpMainController.Awake`, `MediaItem`, `RectTransform`?**
  _High betweenness centrality (0.461) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BumpDetectorCore` (e.g. with `HandBumpDetector.Init()` and `DemoFriendOrb.Init()`) actually correct?**
  _`BumpDetectorCore` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `NotOpened`, `RequestingPermission`, `Loading` to the rest of the system?**
  _94 weakly-connected nodes found - possible missing edges._
- **Should `Source Files & Assemblies` be split into smaller, more focused modules?**
  _Cohesion score 0.09818819403857393 - nodes in this community are weakly interconnected._
- **Why does `LocalNetworkTransferService` connect `LocalNetworkTransferService` to `Source Files & Assemblies`, `BumpMainController`, `TransferLoopbackTests`, `DemoFriendService`, `DemoFriendService.SendBatchAsync`, `IDiscoveryService`, `DiscoveryDebugPanel`, `BumpMainController.Awake`?**
  _High betweenness centrality (0.109) - this node is a cross-community bridge._
- **Are the 3 inferred relationships involving `LocalNetworkTransferService` (e.g. with `BumpMainController.Awake()` and `DiscoveryDebugPanel.OnEnable()`) actually correct?**
  _`LocalNetworkTransferService` has 3 INFERRED edges - model-reasoned connections that need verification._
- **Should `BumpMainController` be split into smaller, more focused modules?**
  _Cohesion score 0.05365853658536585 - nodes in this community are weakly interconnected._
# Graph Report - BumpXR  (2026-11-18)

## Corpus Check
- 31 files · ~25,842 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 738 nodes · 1656 edges · 127 communities (17 shown, 110 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 164 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `1f27f1ae`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- TransferLoopbackTests.cs
- DemoFriendService
- BumpMainController
- Task
- BumpXRBuildTools
- TransferLoopbackTests
- DemoFriendService.SendBatchAsync
- BumpGestureTests
- IncomingOffer
- Handshake Stub
- .BuildShareUi
- BumpFeedback
- DemoFriendOrb
- LocalNetworkTransferService
- HandBumpDetector
- DiscoveryDebugPanel
- ITransferService
- BumpSettings
- CapturesState
- BumpDetectorCore
- TransferStatus
- LocalNetworkTransferService.ReceiveBatchBodyAsync
- BuddyExpression
- IBumpDetector

## God Nodes (most connected - your core abstractions)
1. `BumpMainController` - 144 edges
2. `BumpDetectorCore` - 41 edges
3. `LocalNetworkTransferService` - 36 edges
4. `BumpGestureTests` - 32 edges
5. `TransferLoopbackTests` - 30 edges
6. `MediaItem` - 28 edges
7. `HandBumpDetector` - 26 edges
8. `CapturesMediaSource` - 25 edges
9. `DemoFriendService` - 25 edges
10. `BumpXR.Core` - 22 edges

## Surprising Connections (you probably didn't know these)
- `PendingOffer` --calls--> `OfferItemInfo`  [INFERRED]
  Assets/BumpXR/Scripts/Transfer/LocalNetworkTransferService.cs → Assets/BumpXR/Scripts/Core/ITransferService.cs
- `HandBumpDetector` --references--> `BumpClipRecorder`  [EXTRACTED]
  Assets/BumpXR/Scripts/Gestures/HandBumpDetector.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs
- `BumpDetectorCore` --references--> `BumpSettings`  [EXTRACTED]
  Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs → Assets/BumpXR/Scripts/Core/BumpSettings.cs
- `HandBumpDetector` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Scripts/Gestures/HandBumpDetector.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs
- `DemoFriendOrb` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/DemoFriendOrb.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs

## Import Cycles
- None detected.

## Communities (127 total, 110 thin omitted)

### Community 1 - "TransferLoopbackTests.cs"
Cohesion: 0.09
Nodes (13): BuddyProfile, HairStyle, Bun, Cap, Curly, Short, BumpXR.Gestures, BumpXR.UI (+5 more)

### Community 2 - "DemoFriendService"
Cohesion: 0.18
Nodes (7): DemoFriendService, DemoFriendVisible, Friend, IsAdvertising, IsDiscovering, ListenPort, LocalToken

### Community 3 - "BumpMainController"
Cohesion: 0.05
Nodes (30): BumpMainController, AboutLine, ActiveTab, ArmedText, Captures, CoachLine, CoachVisible, CountBadgeText (+22 more)

### Community 4 - "Task"
Cohesion: 0.07
Nodes (21): IMediaSource, Items, MediaItem, AndroidCapturesBackend, CapturesMediaSource, FullReadCount, Items, PermissionRequestCount (+13 more)

### Community 6 - "TransferLoopbackTests"
Cohesion: 0.23
Nodes (5): OutgoingFile, RawWireHeader, RawWireItem, SyncProgress, TransferLoopbackTests

### Community 7 - "DemoFriendService.SendBatchAsync"
Cohesion: 0.28
Nodes (4): BatchProgress, TransferResult, Success, PendingOffer

### Community 9 - "IncomingOffer"
Cohesion: 0.25
Nodes (5): IncomingOffer, Count, TotalSize, OfferItemInfo, DemoIncomingOffer

### Community 14 - "DemoFriendOrb"
Cohesion: 0.20
Nodes (6): DemoFriendOrb, CooldownLeft, LeftSpeed, RightSpeed, Shown, StateLabel

### Community 15 - "LocalNetworkTransferService"
Cohesion: 0.15
Nodes (5): Header, LocalNetworkTransferService, ListenPort, LocalToken, ManifestItem

### Community 16 - "HandBumpDetector"
Cohesion: 0.12
Nodes (9): HandBumpDetector, ClipSampleCount, CooldownLeft, LastDebugLine, LeftSpeed, RightSpeed, StateLabel, Targets (+1 more)

### Community 19 - "DiscoveryDebugPanel"
Cohesion: 0.06
Nodes (11): DiscoverySceneBuilder, IDiscoveryService, IsAdvertising, IsDiscovering, PeerInfo, MetaDiscoveryService, IsAdvertising, IsDiscovering (+3 more)

### Community 20 - "ITransferService"
Cohesion: 0.12
Nodes (6): ITransferService, ListenPort, LocalToken, StubTransferService, ListenPort, LocalToken

### Community 22 - "CapturesState"
Cohesion: 0.22
Nodes (9): CapturesState, Denied, Empty, Error, Loading, NeedsPermission, NotOpened, Ready (+1 more)

### Community 23 - "BumpDetectorCore"
Cohesion: 0.07
Nodes (23): BumpClip, BumpClipHand, BumpClipRecorder, Count, BumpClipSample, BumpDetectorCore, CooldownLeft, LastDebugLine (+15 more)

### Community 24 - "TransferStatus"
Cohesion: 0.33
Nodes (6): TransferStatus, Completed, Declined, Failed, Rejected, TimedOut

### Community 27 - "BuddyExpression"
Cohesion: 0.40
Nodes (5): BuddyExpression, Neutral, Smile, Think, Wow

### Community 28 - "IBumpDetector"
Cohesion: 0.40
Nodes (5): IBumpDetector, CooldownLeft, LeftSpeed, RightSpeed, StateLabel

## Knowledge Gaps
- **108 isolated node(s):** `Short`, `Bun`, `Cap`, `Curly`, `Neutral` (+103 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 274 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **110 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BumpMainController` connect `BumpMainController` to `.BumpMain_ColdStartFailures_AndRuntimeTuning_NeverDeadEnd`, `TransferLoopbackTests.cs`, `DemoFriendService`, `Task`, `BumpXRBuildTools`, `TransferLoopbackTests`, `DemoFriendService.SendBatchAsync`, `BumpGestureTests`, `IncomingOffer`, `.BuildShareUi`, `BumpFeedback`, `BumpMainController.Update`, `DemoFriendOrb`, `LocalNetworkTransferService`, `HandBumpDetector`, `.ShowNextOffer`, `DiscoveryDebugPanel`, `ITransferService`, `BumpSettings`, `.PlaceInFront`, `IBumpDetector`?**
  _High betweenness centrality (0.359) - this node is a cross-community bridge._
- **Are the 4 inferred relationships involving `BumpDetectorCore` (e.g. with `HandBumpDetector.Init()` and `DemoFriendOrb.Init()`) actually correct?**
  _`BumpDetectorCore` has 4 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Short`, `Bun`, `Cap` to the rest of the system?**
  _108 weakly-connected nodes found - possible missing edges._
- **Should `TransferLoopbackTests.cs` be split into smaller, more focused modules?**
  _Cohesion score 0.08680555555555555 - nodes in this community are weakly interconnected._
- **Why does `LocalNetworkTransferService` connect `LocalNetworkTransferService` to `TransferLoopbackTests.cs`, `BumpMainController`, `TransferLoopbackTests`, `DemoFriendService.SendBatchAsync`, `DiscoveryDebugPanel`, `ITransferService`, `BumpSettings`, `LocalNetworkTransferService.ReceiveBatchBodyAsync`?**
  _High betweenness centrality (0.103) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `LocalNetworkTransferService` (e.g. with `BumpMainController.Awake()` and `TransferLoopbackTests.SetUp()`) actually correct?**
  _`LocalNetworkTransferService` has 2 INFERRED edges - model-reasoned connections that need verification._
- **Should `BumpMainController` be split into smaller, more focused modules?**
  _Cohesion score 0.05121951219512195 - nodes in this community are weakly interconnected._
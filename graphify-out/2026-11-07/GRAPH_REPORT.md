# Graph Report - BumpXR  (2026-11-07)

## Corpus Check
- 26 files · ~13,747 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 407 nodes · 947 edges · 17 communities (8 shown, 9 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 83 edges (avg confidence: 0.83)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `11fbd32f`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Source Files & Assemblies
- LocalNetworkTransferService
- BumpMainController
- SampleMediaSource
- Editor Scene & APK Builders
- PlayMode Transfer Tests
- DemoFriendService
- BumpGestureTests
- Transfer Status Outcomes
- Handshake Stub
- BumpDetectorCore
- DemoFriendOrb

## God Nodes (most connected - your core abstractions)
1. `BumpMainController` - 94 edges
2. `LocalNetworkTransferService` - 30 edges
3. `DemoFriendService` - 24 edges
4. `DemoFriendOrb` - 22 edges
5. `BumpDetectorCore` - 21 edges
6. `BumpGestureTests` - 20 edges
7. `HandBumpDetector` - 19 edges
8. `BumpXR.Core` - 18 edges
9. `PeerInfo` - 17 edges
10. `BumpFeedback` - 16 edges

## Surprising Connections (you probably didn't know these)
- `DemoFriendOrb` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/DemoFriendOrb.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs
- `BumpGestureTests` --references--> `BumpDetectorCore`  [EXTRACTED]
  Assets/BumpXR/Tests/PlayMode/BumpGestureTests.cs → Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs
- `BumpMainController` --references--> `BumpSettings`  [EXTRACTED]
  Assets/BumpXR/Scripts/UI/BumpMainController.cs → Assets/BumpXR/Scripts/Core/BumpSettings.cs
- `BumpGestureTests` --references--> `BumpSettings`  [EXTRACTED]
  Assets/BumpXR/Tests/PlayMode/BumpGestureTests.cs → Assets/BumpXR/Scripts/Core/BumpSettings.cs
- `HandBumpDetector` --implements--> `IBumpDetector`  [EXTRACTED]
  Assets/BumpXR/Scripts/Gestures/HandBumpDetector.cs → Assets/BumpXR/Scripts/Core/IBumpDetector.cs

## Import Cycles
- None detected.

## Communities (17 total, 9 thin omitted)

### Community 1 - "Source Files & Assemblies"
Cohesion: 0.09
Nodes (8): DiscoverySceneBuilder, BumpXR.Gestures, BumpXR.UI, BumpXR.Core, BumpXR.Discovery, BumpXR.EditorTools, BumpXR.Transfer, BumpXR.Tests

### Community 2 - "LocalNetworkTransferService"
Cohesion: 0.05
Nodes (18): IDiscoveryService, IsAdvertising, IsDiscovering, IncomingOffer, TransferResult, Success, PeerInfo, MetaDiscoveryService (+10 more)

### Community 3 - "BumpMainController"
Cohesion: 0.08
Nodes (14): BumpMainController, CoachVisible, Demo, IncomingVisible, IsArmed, IsSending, IsSuspended, ListenPort (+6 more)

### Community 4 - "SampleMediaSource"
Cohesion: 0.13
Nodes (7): IMediaSource, Items, MediaItem, DemoIncomingOffer, SampleMediaSource, Items, ReceivedStore

### Community 7 - "DemoFriendService"
Cohesion: 0.10
Nodes (13): ITransferService, ListenPort, LocalToken, DemoFriendService, DemoFriendVisible, Friend, IsAdvertising, IsDiscovering (+5 more)

### Community 9 - "Transfer Status Outcomes"
Cohesion: 0.33
Nodes (6): TransferStatus, Completed, Declined, Failed, Rejected, TimedOut

### Community 11 - "BumpDetectorCore"
Cohesion: 0.08
Nodes (15): BumpDetectorCore, CooldownLeft, LeftSpeed, RightSpeed, State, Sample, Track, BumpSettings (+7 more)

### Community 12 - "DemoFriendOrb"
Cohesion: 0.09
Nodes (12): IBumpDetector, CooldownLeft, LeftSpeed, RightSpeed, StateLabel, BumpFeedback, DemoFriendOrb, CooldownLeft (+4 more)

## Knowledge Gaps
- **55 isolated node(s):** `LeftSpeed`, `RightSpeed`, `State`, `CooldownLeft`, `StateLabel` (+50 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 110 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `BumpMainController` connect `BumpMainController` to `BumpMainController.Awake`, `Source Files & Assemblies`, `LocalNetworkTransferService`, `Editor Scene & APK Builders`, `PlayMode Transfer Tests`, `DemoFriendService`, `BumpGestureTests`, `BumpDetectorCore`, `DemoFriendOrb`, `BumpMainController.StartNetworkingAsync`, `BumpMainController.ShowNextOffer`, `BumpMainController.AcceptIncoming`, `BumpMainController.RefreshReceived`?**
  _High betweenness centrality (0.431) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `LocalNetworkTransferService` (e.g. with `DiscoveryDebugPanel.OnEnable()` and `TransferLoopbackTests.SetUp()`) actually correct?**
  _`LocalNetworkTransferService` has 2 INFERRED edges - model-reasoned connections that need verification._
- **What connects `LeftSpeed`, `RightSpeed`, `State` to the rest of the system?**
  _55 weakly-connected nodes found - possible missing edges._
- **Should `Source Files & Assemblies` be split into smaller, more focused modules?**
  _Cohesion score 0.09216255442670537 - nodes in this community are weakly interconnected._
- **Why does `LocalNetworkTransferService` connect `LocalNetworkTransferService` to `Source Files & Assemblies`, `PlayMode Transfer Tests`, `DemoFriendService`?**
  _High betweenness centrality (0.102) - this node is a cross-community bridge._
- **Should `LocalNetworkTransferService` be split into smaller, more focused modules?**
  _Cohesion score 0.052393857271906055 - nodes in this community are weakly interconnected._
- **Why does `DiscoveryDebugPanel` connect `LocalNetworkTransferService` to `BumpMainController.Awake`, `Source Files & Assemblies`, `Editor Scene & APK Builders`?**
  _High betweenness centrality (0.091) - this node is a cross-community bridge._
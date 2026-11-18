# BumpXR — Agent Documentation

> Auto-generated project knowledge base (`hz-unity-project-analyzer`).
> Last updated: 2026-11-08

## Quick Context
BumpXR ("AirDrop for XR") lets two nearby Meta Quest users share 1–10 photos (`image/jpeg`, `image/png`) with a bare-hand fist bump over Mixed Reality passthrough, while remaining 100% solo-playable via an interactive Demo Friend fist orb. Built on Unity `6000.3.12f1` (URP `17.3.0`) and Meta XR All-in-One SDK `207.0.1` targeting Quest 2, Quest 3, and Quest 3S on Android ARM64 IL2CPP (`com.hussienx.bumpxr`, `v0.1.1`, `versionCode 2`).

## Document Map
- [_config.md](_config.md) — Analyzer scan metadata for incremental updates
- Systems
  - [Discovery](systems/discovery.md) — `IDiscoveryService`, `MetaDiscoveryService`, `DemoFriendService`, `PeerInfo`, `IMediaSource` (`SampleMediaSource`, `CapturesMediaSource`, `MomentsMediaSource`)
  - [Transfer](systems/transfer.md) — `ITransferService`, `LocalNetworkTransferService` (batch `1..10`, `20 MB` item / `100 MB` batch caps, atomic cancel cleanup), `ReceivedStore`, `NetworkUtil`
  - [Gestures](systems/gestures.md) — `IBumpDetector`, `BumpSettings`, `BumpDetectorCore` (5 false-bump gates + `BumpClipRecorder` + `BUMPDBG`), `HandBumpDetector`, `DemoFriendOrb`, `BumpFeedback`
  - [UI](systems/ui.md) — `BumpMainController` (Samples / My Captures / Moments tabs, multi-select `1..10`, batch IncomingCard without preview, 5-poke hidden Dev + clip export), `DiscoveryDebugPanel`

## Scenes & Assemblies
- **Main runtime scene:** `Assets/BumpXR/Scenes/BumpMain.unity` (index 0; `OVRCameraRig` + `OVRPassthroughLayer` + hand visuals + Share panel at `0.60 m` + Incoming card at `0.45 m`).
- **Diagnostic scene:** `Assets/BumpXR/Scenes/DiscoveryTest.unity` (index 1; `DiscoveryDebugPanel`).
- **Assemblies:** `BumpXR.Runtime` (`Oculus.VR`, `Oculus.Interaction`, `UnityEngine.UI`), `BumpXR.Tests.PlayMode` (`TransferLoopbackTests` & `BumpGestureTests`), plus Editor builders under `Assets/BumpXR/Editor/`.

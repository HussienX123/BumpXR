# Discovery & Media Sources System

> Last updated: 2026-11-08

## Purpose
Discovers nearby Quest headsets running BumpXR (`{name, token, ip, port}`, `<= 1024` bytes) behind `IDiscoveryService`, provides a solo-playable synthetic peer (`DemoFriendService`), and exposes shareable image catalogs (`Samples`, `My Captures`, `Moments`) behind `IMediaSource`.

## Key Scripts & Roles
- `Assets/BumpXR/Scripts/Core/IDiscoveryService.cs` & `PeerInfo.cs` — Interface and peer record (`SessionId`, `DisplayName`, `Token`, `Ip`, `Port`, `IsDemo`).
- `Assets/BumpXR/Scripts/Discovery/MetaDiscoveryService.cs` — Production Colocation Discovery wrapper (`OVRColocationSession`).
- `Assets/BumpXR/Scripts/Discovery/DemoFriendService.cs` — Solo decorator implementing `IDiscoveryService` + `ITransferService`; accepts batches (`1..10`) with sequential `BatchProgress` and replies with `1–3` sample photos (`1` for single-photo sends, `2–3` for batch sends).
- `Assets/BumpXR/Scripts/Core/IMediaSource.cs` (`IMediaSource`, `MediaItem`) — Unified contract (`Items`, `ReadBytes`, `ReadThumbnailBytesAsync`).
- `Assets/BumpXR/Scripts/Discovery/SampleMediaSource.cs` — 6 bundled JPEG/PNG assets in `Resources/SamplePhotos/`.
- `Assets/BumpXR/Scripts/Discovery/MomentsMediaSource.cs` — App-sandbox photos in `Application.persistentDataPath/Received/` sorted newest first (zero permissions).
- `Assets/BumpXR/Scripts/Discovery/CapturesMediaSource.cs` — Device screenshots/photos via Android `MediaStore` + `ContentResolver` (`loadThumbnail` off main thread, `openInputStream` only on send, newest `60`, zero file paths). Requests `android.permission.READ_MEDIA_IMAGES` lazily ONLY when `"My Captures"` is opened (`unityplayer.SkipPermissionsDialog = "true"`).

## Invariants
- First launch and the Demo Friend loop trigger **zero** permission prompts; denying `READ_MEDIA_IMAGES` displays a single explanatory line while `Samples` and `Moments` continue working.
- **Related docs:** [Transfer](transfer.md), [UI](ui.md)

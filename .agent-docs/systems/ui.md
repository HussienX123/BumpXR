# UI System

> Last updated: 2026-11-08

## Purpose
Provides seated, bare-hand-operable world-space uGUI panels over Insight Passthrough for source-tab browsing (`Samples`, `My Captures`, `Moments`), hands-only multi-select (`1..10`), bump/poke batch sending, incoming batch consent without image preview, and hidden developer diagnostics.

## Key Scripts & Scenes
- `Assets/BumpXR/Scripts/UI/BumpMainController.cs` — Coordinator in `BumpMain.unity`.
  - **Source tabs & multi-select:** `Samples`, `My Captures` (lazy `READ_MEDIA_IMAGES` request only on tab open), and `Moments`; poke thumbnails to toggle numbered check badges, `Select all`, `Clear`, `0/10` count badge, `<`/`>` paging, and armed button reading `"N photos to <friend>"`.
  - **Batch IncomingCard:** One card per batch (`"<Sender> wants to send N photos, X.X MB"`) listing sanitized filenames only (**zero** `RawImage` preview before Accept), sequential overall + per-file progress bars, and single `ACCEPT`/`DECLINE` or bump.
  - **Hidden Dev overlay:** `Dev` button starts hidden and unlocks after **5 pokes on the version text**; preserves the 3 `+/-` tuning controls (`Speed`, `Curl`, `Decel`) + `Reset`/`Print`/`Sim bump` and adds `Save clip: FALSE` / `Save clip: TRUE` showing the saved `BumpClips/` path.
- `Assets/BumpXR/Scripts/UI/DiscoveryDebugPanel.cs` — Diagnostic panel in `DiscoveryTest.unity`.
- `Assets/BumpXR/Editor/BumpXRBuildTools.cs` — Editor builders (`Phase3Setup`, `Phase4Setup`, `Phase5ASetup`, `BuildReleaseApk`, `BuildReleaseApkV011`).

## Spatial & Ergonomic Invariants
- **Seated reach & Glasses FOV:** `ShareCanvas` (`0.60 m`) and `IncomingCard` (`0.45 m`) remain strictly inside `+/-25 deg H` and `+/-22 deg V`.
- **Related docs:** [Discovery](discovery.md), [Transfer](transfer.md), [Gestures](gestures.md)

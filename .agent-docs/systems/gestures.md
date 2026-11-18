# Gestures System

> Last updated: 2026-11-08

## Purpose
Recognizes bare-hand fist bumps using Meta Interaction SDK finger-curl and wrist-pose APIs while suppressing head-turn, nod, shake, dropout-jump, and walking false positives via 5 configurable gates in `BumpDetectorCore`, backed by a 3 s clip recorder for offline replay.

## Key Scripts & Assets
- `Assets/BumpXR/Scripts/Core/IBumpDetector.cs` (`IBumpDetector`, `BumpEvent`) — Gesture interface consumed by UI.
- `Assets/BumpXR/Scripts/Core/BumpSettings.cs` & `Assets/BumpXR/Resources/BumpSettings.asset` — Tuned and baked thresholds (`speedThreshold = 1.90 m/s`, `fistCurlDegrees = 200`, `decelRatio = 0.25`) plus false-bump gate thresholds (`headMotionWindow = 0.25 s`, `maxHeadLinearSpeed = 0.35 m/s`, `maxHeadAngularSpeed = 65 deg/s`, `minTrackedDuration = 0.35 s`, `maxFrameJumpMeters = 0.25 m`, `maxApproachAngleDegrees = 25 deg`, `minApproachTravel = 0.20 m`, `minReachDistance = 0.25 m`, `maxReachDistance = 0.65 m`, `minFistHoldDuration = 0.15 s`).
- `Assets/BumpXR/Scripts/Core/BumpDetectorCore.cs` (`BumpDetectorCore`, `BumpClipRecorder`, `BumpClip`) — SDK-free kinematics engine enforcing all 5 gates, single-line `BUMPDBG ACCEPT/REJECT` candidate logging, 3 s dual-hand/head ring buffer, and `ReplayClip` JSON replay.
- `Assets/BumpXR/Scripts/Gestures/HandBumpDetector.cs` — Queries `Hand.GetRootPose`, `FingerShapes.GetCurlValue`, and `Camera.main` head pose every `LateUpdate`; exports labeled JSON clips (`SaveClip("FALSE"|"TRUE")`) to `Application.persistentDataPath/BumpClips/`.
- `Assets/BumpXR/Scripts/UI/DemoFriendOrb.cs` & `BumpFeedback.cs` — Solo fist orb and procedural spatial audio/VFX.

## Related Docs
- [UI](ui.md), [Transfer](transfer.md)

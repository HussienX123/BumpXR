# Discovery Layer Rules
- Keep all callers strictly behind `IDiscoveryService` (`MetaDiscoveryService` is the only class touching `OVRColocationSession`).
- Assert serialized advertisement metadata <= `OVRColocationSession.Data.MaxMetadataSize` (`1024` bytes) and always unregister `ColocationSessionDiscovered` on stop.
- Maintain `DemoFriendService` solo-mode parity: `"Demo Friend"` auto-appears after 10 s with no real peers (plus manual toggle), accepts batches (`1..10`), and replies with `1–3` sample photos through `OfferReceived`.
- Keep all shareable media (`SampleMediaSource`, `CapturesMediaSource`, `MomentsMediaSource`) behind `IMediaSource`; request `READ_MEDIA_IMAGES` lazily ONLY when `"My Captures"` is opened and read via `ContentResolver` streams (never file paths).

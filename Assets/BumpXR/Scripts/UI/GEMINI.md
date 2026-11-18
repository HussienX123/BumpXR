# UI Layer Rules (Hands-First, Seated, Glasses-Ready)
- Must be 100% operable with bare hands via Meta Interaction SDK (`PokeInteractable`, `RayInteractable`, `PointableCanvas`, `PointableCanvasModule`); never require controllers.
- Bare hands have no haptics: pair interactions with clear visual + audio feedback.
- Seated ergonomics: place panels within arm's reach (`0.45 m`–`0.70 m`), no room-scale locomotion.
- Keep primary UI centered within `+/-25 deg` horizontal and `+/-22 deg` vertical for VR Glasses (`~70x66 deg` FOV) compatibility.
- Keep UI decoupled from Core/Discovery/Transfer: consume only `IDiscoveryService`, `ITransferService`, and `IMediaSource` interfaces.


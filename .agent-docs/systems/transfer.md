# Transfer System

> Last updated: 2026-11-08

## Purpose
Streams single photos or batches (`1..10` JPEG/PNG images) over local Wi-Fi TCP (`System.Net.Sockets.TcpListener` / `TcpClient`) with one-time token authentication, explicit receiver consent, sequential overall + per-file progress, and atomic cleanup.

## Key Scripts & Roles
- `Assets/BumpXR/Scripts/Core/ITransferService.cs` (`ITransferService`, `IncomingOffer`, `OutgoingFile`, `OfferItemInfo`, `BatchProgress`, `TransferResult`) — Transport contract (`SendAsync`, `SendBatchAsync`, `AcceptBatchAsync`, `AcceptAsync`, `Decline`).
- `Assets/BumpXR/Scripts/Transfer/LocalNetworkTransferService.cs` — Length-prefixed JSON `Header` (`items: [{name, size, type, sha256}]`) + sequential raw byte streams. Enforces constant-time token check, all-or-nothing batch offer validation, magic-byte inspection, per-file SHA-256 verification, and atomic deletion of partial + earlier batch files on mid-stream cancel or error.
- `Assets/BumpXR/Scripts/Transfer/ReceivedStore.cs` — MIME allowlist (`image/jpeg`, `image/png`), magic-byte checks, `SanitizeFileName` (`[A-Za-z0-9_-]`), and `FileMode.CreateNew` writes under `Application.persistentDataPath/Received/`.
- `Assets/BumpXR/Scripts/Transfer/NetworkUtil.cs` & `StubTransferService.cs` — Wi-Fi IPv4 helper and inert legacy placeholder.

## Safety Invariants (Never Weaken)
- **Batch & size caps:** `MaxFileBytes = 20 MB`, `MaxBatchBytes = 100 MB`, `MaxBatchItems = 10`; any disallowed MIME or oversize item rejects the **entire** batch before `OfferReceived` fires, with zero bytes written.
- **Unsolicited-content safety:** Incoming offers expose **sanitized names only** (`OfferItemInfo.SafeName`) and **no image preview** before receiver acceptance.
- **Atomic storage:** Zero bytes written before accept; never overwrite; partial or cancelled batch files deleted immediately.
- **Related docs:** [Discovery](discovery.md), [UI](ui.md)

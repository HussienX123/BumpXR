# BumpXR (Unity 6.3, Meta XR All-in-One SDK 207.0.1)
"AirDrop for XR": two nearby Quest users send a photo with a hand bump. Entry: Meta VR Start Developer Competition 2026 (New Experience, Productivity). Deadline Nov 18, 12:00 PM Pacific.

## MUST rules
- Hands-first: fully usable with bare hands, no controllers. Bare hands have no haptics: use audio + visual feedback.
- Seated: everything within arm's reach. Primary UI within +/-25 deg horizontal, +/-22 deg vertical (Glasses FOV ~70x66).
- Solo-playable: the whole loop works with the Demo Friend (judges may have one headset).
- Never guess Meta API names: search docs with `metavr` first.
- No new packages, signing/keystore/package-name changes, or deletions without asking me.

## Architecture (4 layers behind interfaces)
1 Discovery: Meta Colocation Discovery behind IDiscoveryService (MetaDiscoveryService; DemoFriendService fakes a peer).
2 Handshake: metadata {name, one-time token, IPv4, port}, max 1024 bytes.
3 Transfer: every device listens on TCP; sender connects, presents the receiver's token, sends a batch manifest (`1..10` items); bytes stream sequentially only after accept. LocalNetworkTransferService.
4 UI: world-space, poke/pinch via Interaction SDK. Scenes: BumpMain, DiscoveryTest.
Transfer safety (never weaken): bad token rejected; image/jpeg + image/png only; 20 MB per file / 100 MB per batch / max 10 items; one disallowed or oversize item rejects the WHOLE batch with nothing written; sanitized names only; no preview before accept; save only under persistentDataPath/Received/; never overwrite; partial or cancelled batch files deleted; nothing written before accept; READ_MEDIA_IMAGES requested lazily ONLY on opening My Captures.

## How to work (token discipline)
- Navigate first: read graphify-out/GRAPH_REPORT.md and .agent-docs/index.md; use graphify query/explain/path before grep; read files by line range.
- Never read Library/, Temp/, Builds/, Packages cache, Assets/Oculus.
- Tail logs, do not dump them. Keep tool output short.
- Unity: with the Editor open use `unity status`, `unity recompile`, `unity test`. Never run batch mode against an open project, never close the Editor, never use `eval`.
- Meta VR CLI: `metavr` for docs search, device install/logs/screenshots, `device fov-sim`, `vrc-local`. Never run store or publish commands.

## Working agreements
- Only make changes directly requested. No extra features, abstractions, or files.
- After each numbered step output "✅ [what was done]". Name the SDK class and docs page for every Meta API used.
- After 2 failed attempts at the same error, stop and ask. If the Editor exits unexpectedly, stop and report the last 50 lines of Editor.log.
- End of every phase: update docs/PROGRESS.md and refresh the graph and .agent-docs.

@docs/PROGRESS.md

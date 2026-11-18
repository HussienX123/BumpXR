# Transfer Layer Rules (Never Weaken)
- Constant-time one-time token verification; reject invalid tokens (`bad_token`) before creating an offer; rotate token after each completed transfer.
- Accept ONLY `image/jpeg` and `image/png` (`1..10` items per batch): enforce both MIME allowlist and leading magic-byte signature (`FF D8 FF` / `89 50 4E 47 0D 0A 1A 0A`).
- Enforce `20 MB` per file (`MaxFileBytes`), `100 MB` per batch (`MaxBatchBytes`), max `10` items (`MaxBatchItems`), and per-file SHA-256. Any single invalid/oversize item rejects the WHOLE batch with nothing written.
- Write ZERO bytes to disk and show ZERO image preview before explicit receiver accept (`AcceptBatchAsync` / `AcceptAsync`). Atomically delete partial and earlier batch files on cancel/abort/mismatch.
- Sanitize filenames (`[A-Za-z0-9_-]`, forced extension from MIME) on both incoming offer display and disk writes; save ONLY under `Application.persistentDataPath/Received/` with `FileMode.CreateNew` (never overwrite).
- Request `READ_MEDIA_IMAGES` lazily ONLY when opening `"My Captures"` (`CapturesMediaSource` via `ContentResolver` streams, never file paths); never write outside app sandbox.

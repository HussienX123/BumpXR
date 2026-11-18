using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BumpXR.Core;
using UnityEngine;

namespace BumpXR.Transfer
{
    /// <summary>
    /// TCP transfer supporting single-file and batch (1..10 image/jpeg or image/png) manifests.
    /// Every device listens on an ephemeral port; the sender connects, presents the receiver's one-time token and
    /// a batch manifest; bytes flow sequentially ONLY after the receiver explicitly accepts.
    /// Wire format: [4-byte big-endian length][UTF-8 JSON header], then (offer accepted) concatenated raw file streams.
    /// </summary>
    public sealed class LocalNetworkTransferService : ITransferService, IDisposable
    {
        public const long MaxFileBytes = 20L * 1024 * 1024;
        public const long MaxBatchBytes = 100L * 1024 * 1024;
        public const int MaxBatchItems = 10;
        private const int MaxHeaderBytes = 16384;
        private const int ChunkSize = 64 * 1024;

        [Serializable]
        private sealed class ManifestItem
        {
            public string name;
            public long size;
            public string type;
            public string sha256;
        }

        [Serializable]
        private sealed class Header
        {
            public string type;       // offer | accept | decline | reject | done | error
            public string token;
            public string senderName;
            public string fileName;
            public string mimeType;
            public long size;
            public string sha256;
            public ManifestItem[] items;
            public string reason;
        }

        private readonly string _receiveDir;
        private TcpListener _listener;
        private bool _listening;

        /// <summary>Max wait for any single read/write/connect (stall detection).</summary>
        public int IoTimeoutMs = 10000;

        /// <summary>Max wait for the receiver to accept or decline.</summary>
        public int DecisionTimeoutMs = 35000;

        public string LocalToken { get; private set; }
        public int ListenPort { get; private set; }

        public event Action<IncomingOffer> OfferReceived;
        public event Action TokenChanged;

        /// <param name="receiveDir">Defaults to Application.persistentDataPath/Received. Must be called on the main thread.</param>
        public LocalNetworkTransferService(string receiveDir = null)
        {
            _receiveDir = receiveDir ?? Path.Combine(Application.persistentDataPath, "Received");
            LocalToken = NewToken();
        }

        // ---------- listening (receiver) ----------

        public void StartListening()
        {
            if (_listening) return;
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _listening = true;
            AcceptLoop(_listener);
        }

        public void StopListening()
        {
            _listening = false;
            try { _listener?.Stop(); } catch { /* already stopped */ }
            _listener = null;
        }

        public void Dispose() => StopListening();

        private async void AcceptLoop(TcpListener listener)
        {
            while (_listening)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch { break; }

                HandleClientAsync(client);
            }
        }

        private async void HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await HandleClientCoreAsync(client);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BumpXR.Transfer] incoming connection ended: {e.Message}");
                }
            }
        }

        private async Task HandleClientCoreAsync(TcpClient client)
        {
            var stream = client.GetStream();
            Header offer = await ReadHeaderAsync(stream, IoTimeoutMs);

            string problem = ValidateOffer(offer, out ManifestItem[] manifest, out long totalBytes);
            if (problem != null)
            {
                await WriteHeaderAsync(stream, new Header { type = "reject", reason = problem }, IoTimeoutMs);
                return;
            }

            var pending = new PendingOffer(offer.senderName, manifest, totalBytes);
            OfferReceived?.Invoke(pending);

            Task winner = await Task.WhenAny(pending.Decision.Task, Task.Delay(DecisionTimeoutMs));
            bool accepted = winner == pending.Decision.Task && pending.Decision.Task.Result;
            if (!accepted)
            {
                pending.Decision.TrySetResult(false);
                await WriteHeaderAsync(stream, new Header { type = "decline" }, IoTimeoutMs);
                pending.Completion.TrySetException(new OperationCanceledException("Offer declined or timed out."));
                return;
            }

            // Accepted: only now may anything touch the disk.
            IReadOnlyList<string> savedPaths = null;
            try
            {
                await WriteHeaderAsync(stream, new Header { type = "accept" }, IoTimeoutMs);
                savedPaths = await ReceiveBatchBodyAsync(stream, manifest, totalBytes, pending.Progress, pending.CancelToken);
                await WriteHeaderAsync(stream, new Header { type = "done" }, IoTimeoutMs);
            }
            catch (Exception e)
            {
                pending.Completion.TrySetException(e);
                try { await WriteHeaderAsync(stream, new Header { type = "error", reason = e.Message }, 2000); } catch { }
                return;
            }

            pending.Completion.TrySetResult(savedPaths);
            RotateToken();
        }

        /// <summary>
        /// All-or-nothing offer validation: bad token, empty/>10 items, any disallowed MIME, any item >20 MB,
        /// total >100 MB, or bad SHA-256 rejects the WHOLE batch before OfferReceived fires or anything is written.
        /// </summary>
        private string ValidateOffer(Header h, out ManifestItem[] manifest, out long totalBytes)
        {
            manifest = null;
            totalBytes = 0;
            if (h == null || h.type != "offer") return "bad_request";
            if (!ConstantTimeEquals(h.token, LocalToken)) return "bad_token";

            if (h.items != null && h.items.Length > 0)
            {
                manifest = h.items;
            }
            else if (!string.IsNullOrEmpty(h.mimeType) || h.size > 0 || !string.IsNullOrEmpty(h.sha256))
            {
                manifest = new[]
                {
                    new ManifestItem { name = h.fileName, size = h.size, type = h.mimeType, sha256 = h.sha256 }
                };
            }
            else
            {
                return "empty_batch";
            }

            if (manifest.Length > MaxBatchItems) return "too_many_items";

            for (int i = 0; i < manifest.Length; i++)
            {
                ManifestItem m = manifest[i];
                if (m == null) return "bad_item";
                if (!ReceivedStore.IsAllowedMime(m.type)) return "type_not_allowed";
                if (m.size <= 0 || m.size > MaxFileBytes) return "size_not_allowed";
                if (string.IsNullOrEmpty(m.sha256) || m.sha256.Length != 64 || !IsHex64(m.sha256)) return "bad_hash";
                if (totalBytes > MaxBatchBytes - m.size) return "batch_size_not_allowed";
                totalBytes += m.size;
            }

            return null;
        }

        private async Task<IReadOnlyList<string>> ReceiveBatchBodyAsync(
            NetworkStream stream,
            ManifestItem[] manifest,
            long totalBytes,
            IProgress<BatchProgress> progress,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[ChunkSize];
            var savedPaths = new List<string>(manifest.Length);
            long batchReceived = 0;
            FileStream currentFile = null;
            string currentPath = null;

            try
            {
                for (int idx = 0; idx < manifest.Length; idx++)
                {
                    ManifestItem item = manifest[idx];
                    string safeDisplay = ReceivedStore.SanitizeFileName(item.name, item.type);
                    long remaining = item.size;
                    long fileReceived = 0;
                    currentFile = null;
                    currentPath = null;

                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        while (remaining > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int want = (int)Math.Min(buffer.Length, remaining);
                            int n = await ReadSomeAsync(stream, buffer, want, IoTimeoutMs);
                            if (n <= 0) throw new IOException("Connection closed before the file finished.");
                            cancellationToken.ThrowIfCancellationRequested();

                            if (currentFile == null)
                            {
                                if (!ReceivedStore.MatchesMagic(item.type, buffer, n))
                                    throw new IOException("File content does not match the declared image type.");
                                currentFile = ReceivedStore.CreateNew(_receiveDir, item.name, item.type, out currentPath);
                            }

                            currentFile.Write(buffer, 0, n);
                            hash.AppendData(buffer, 0, n);
                            remaining -= n;
                            fileReceived += n;
                            batchReceived += n;
                            progress?.Report(new BatchProgress(
                                idx,
                                manifest.Length,
                                safeDisplay,
                                (float)fileReceived / item.size,
                                (float)batchReceived / totalBytes));
                        }

                        currentFile.Dispose();
                        currentFile = null;

                        string actual = ToHex(hash.GetHashAndReset());
                        if (!string.Equals(actual, item.sha256, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("SHA-256 mismatch.");

                        savedPaths.Add(currentPath);
                        currentPath = null;
                    }
                }

                return savedPaths;
            }
            catch
            {
                currentFile?.Dispose();
                if (currentPath != null) SafeDelete(currentPath);
                for (int i = 0; i < savedPaths.Count; i++) SafeDelete(savedPaths[i]);
                throw;
            }
        }

        private static void SafeDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort cleanup of our own partial/batch file */ }
        }

        private void RotateToken()
        {
            LocalToken = NewToken();
            TokenChanged?.Invoke();
        }

        // ---------- sending ----------

        public Task<TransferResult> SendAsync(PeerInfo peer, string senderName, string fileName, string mimeType,
            byte[] data, IProgress<float> progress)
        {
            IProgress<BatchProgress> mapped = progress == null
                ? null
                : new Progress<BatchProgress>(bp => progress.Report(bp.OverallProgress));
            return SendBatchAsync(peer, senderName, new[] { new OutgoingFile(fileName, mimeType, data) }, mapped);
        }

        public async Task<TransferResult> SendBatchAsync(
            PeerInfo peer,
            string senderName,
            IReadOnlyList<OutgoingFile> files,
            IProgress<BatchProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (peer == null || string.IsNullOrEmpty(peer.Ip) || peer.Port <= 0)
                return new TransferResult(TransferStatus.Failed, "peer has no address");
            if (files == null || files.Count == 0)
                return new TransferResult(TransferStatus.Rejected, "empty_batch");
            if (files.Count > MaxBatchItems)
                return new TransferResult(TransferStatus.Rejected, "too_many_items");

            var payloads = new byte[files.Count][];
            var manifest = new ManifestItem[files.Count];
            long totalBytes = 0;

            using (var sha256 = SHA256.Create())
            {
                for (int i = 0; i < files.Count; i++)
                {
                    OutgoingFile f = files[i];
                    if (!ReceivedStore.IsAllowedMime(f.MimeType))
                        return new TransferResult(TransferStatus.Rejected, "type_not_allowed");

                    byte[] bytes;
                    try { bytes = f.ResolveBytes(); }
                    catch (Exception e) { return new TransferResult(TransferStatus.Failed, e.Message); }

                    if (bytes == null || bytes.Length == 0 || bytes.Length > MaxFileBytes)
                        return new TransferResult(TransferStatus.Rejected, "size_not_allowed");
                    if (totalBytes > MaxBatchBytes - bytes.Length)
                        return new TransferResult(TransferStatus.Rejected, "batch_size_not_allowed");

                    totalBytes += bytes.Length;
                    payloads[i] = bytes;
                    manifest[i] = new ManifestItem
                    {
                        name = f.FileName,
                        size = bytes.Length,
                        type = f.MimeType,
                        sha256 = ToHex(sha256.ComputeHash(bytes))
                    };
                }
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var client = new TcpClient())
                using (cancellationToken.Register(() => { try { client.Close(); } catch { } }))
                {
                    Task connect = client.ConnectAsync(peer.Ip, peer.Port);
                    if (await Task.WhenAny(connect, Task.Delay(IoTimeoutMs, cancellationToken)) != connect)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return new TransferResult(TransferStatus.TimedOut, "connect timed out");
                    }
                    await connect;

                    var stream = client.GetStream();
                    ManifestItem first = manifest[0];
                    await WriteHeaderAsync(stream, new Header
                    {
                        type = "offer",
                        token = peer.Token,
                        senderName = senderName,
                        fileName = first.name,
                        mimeType = first.type,
                        size = totalBytes,
                        sha256 = first.sha256,
                        items = manifest
                    }, IoTimeoutMs);

                    Header reply = await ReadHeaderAsync(stream, DecisionTimeoutMs + IoTimeoutMs);
                    if (reply.type == "reject") return new TransferResult(TransferStatus.Rejected, reply.reason);
                    if (reply.type == "decline") return new TransferResult(TransferStatus.Declined);
                    if (reply.type != "accept") return new TransferResult(TransferStatus.Failed, "unexpected reply " + reply.type);

                    long batchSent = 0;
                    for (int idx = 0; idx < payloads.Length; idx++)
                    {
                        byte[] data = payloads[idx];
                        string safeName = ReceivedStore.SanitizeFileName(manifest[idx].name, manifest[idx].type);
                        int sent = 0;
                        while (sent < data.Length)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int n = Math.Min(ChunkSize, data.Length - sent);
                            await WriteAsync(stream, data, sent, n, IoTimeoutMs);
                            sent += n;
                            batchSent += n;
                            progress?.Report(new BatchProgress(
                                idx,
                                payloads.Length,
                                safeName,
                                (float)sent / data.Length,
                                (float)batchSent / totalBytes));
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    Header done = await ReadHeaderAsync(stream, IoTimeoutMs);
                    return done.type == "done"
                        ? new TransferResult(TransferStatus.Completed)
                        : new TransferResult(TransferStatus.Failed, done.reason ?? done.type);
                }
            }
            catch (OperationCanceledException)
            {
                return new TransferResult(TransferStatus.Failed, "cancelled");
            }
            catch (TimeoutException e)
            {
                return new TransferResult(TransferStatus.TimedOut, e.Message);
            }
            catch (Exception e)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new TransferResult(TransferStatus.Failed, "cancelled");
                return new TransferResult(TransferStatus.Failed, e.Message);
            }
        }

        // ---------- framing helpers ----------

        private static async Task WriteHeaderAsync(NetworkStream s, Header h, int timeoutMs)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(h));
            var frame = new byte[4 + body.Length];
            frame[0] = (byte)(body.Length >> 24);
            frame[1] = (byte)(body.Length >> 16);
            frame[2] = (byte)(body.Length >> 8);
            frame[3] = (byte)body.Length;
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            await WriteAsync(s, frame, 0, frame.Length, timeoutMs);
        }

        private static async Task<Header> ReadHeaderAsync(NetworkStream s, int timeoutMs)
        {
            var lenBuf = new byte[4];
            await ReadExactAsync(s, lenBuf, 4, timeoutMs);
            int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (len <= 0 || len > MaxHeaderBytes) throw new IOException("Invalid header length.");

            var body = new byte[len];
            await ReadExactAsync(s, body, len, timeoutMs);
            var h = JsonUtility.FromJson<Header>(Encoding.UTF8.GetString(body));
            if (h == null) throw new IOException("Unreadable header.");
            return h;
        }

        private static async Task ReadExactAsync(NetworkStream s, byte[] buf, int count, int timeoutMs)
        {
            int got = 0;
            while (got < count)
            {
                Task<int> read = s.ReadAsync(buf, got, count - got);
                if (await Task.WhenAny(read, Task.Delay(timeoutMs)) != read) throw new TimeoutException("read stalled");
                int n = await read;
                if (n <= 0) throw new IOException("Connection closed.");
                got += n;
            }
        }

        private static async Task<int> ReadSomeAsync(NetworkStream s, byte[] buf, int max, int timeoutMs)
        {
            Task<int> read = s.ReadAsync(buf, 0, max);
            if (await Task.WhenAny(read, Task.Delay(timeoutMs)) != read) throw new TimeoutException("read stalled");
            return await read;
        }

        private static async Task WriteAsync(NetworkStream s, byte[] buf, int offset, int count, int timeoutMs)
        {
            Task write = s.WriteAsync(buf, offset, count);
            if (await Task.WhenAny(write, Task.Delay(timeoutMs)) != write) throw new TimeoutException("write stalled");
            await write;
        }

        // ---------- misc ----------

        private static string NewToken()
        {
            var bytes = new byte[12];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return ToHex(bytes);
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static bool IsHex64(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private sealed class PendingOffer : IncomingOffer
        {
            public readonly TaskCompletionSource<bool> Decision = new TaskCompletionSource<bool>();
            public readonly TaskCompletionSource<IReadOnlyList<string>> Completion = new TaskCompletionSource<IReadOnlyList<string>>();
            public IProgress<BatchProgress> Progress;
            public CancellationToken CancelToken;

            public PendingOffer(string senderName, ManifestItem[] manifest, long totalBytes)
            {
                SenderName = senderName;
                var infos = new OfferItemInfo[manifest.Length];
                for (int i = 0; i < manifest.Length; i++)
                {
                    string safe = ReceivedStore.SanitizeFileName(manifest[i].name, manifest[i].type);
                    infos[i] = new OfferItemInfo(safe, manifest[i].type, manifest[i].size);
                }
                Items = infos;
                FileName = infos[0].SafeName;
                MimeType = infos[0].MimeType;
                Size = totalBytes;
            }

            public override Task<IReadOnlyList<string>> AcceptBatchAsync(
                IProgress<BatchProgress> progress,
                CancellationToken cancellationToken = default)
            {
                Progress = progress;
                CancelToken = cancellationToken;
                Decision.TrySetResult(true);
                return Completion.Task;
            }

            public override void Decline() => Decision.TrySetResult(false);
        }
    }
}

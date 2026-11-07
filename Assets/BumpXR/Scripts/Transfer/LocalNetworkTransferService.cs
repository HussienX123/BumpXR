using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BumpXR.Core;
using UnityEngine;

namespace BumpXR.Transfer
{
    /// <summary>
    /// TCP transfer. Every device listens on an ephemeral port; the sender connects, presents the receiver's token and
    /// an offer; bytes flow only after the receiver accepts.
    /// Wire format, both directions: [4-byte big-endian length][UTF-8 JSON header], then (offer accepted) raw file bytes.
    /// Continuations run on the caller's SynchronizationContext (Unity main thread when started from it).
    /// </summary>
    public sealed class LocalNetworkTransferService : ITransferService, IDisposable
    {
        public const long MaxFileBytes = 20L * 1024 * 1024;
        private const int MaxHeaderBytes = 4096;
        private const int ChunkSize = 64 * 1024;

        [Serializable]
        private sealed class Header
        {
            public string type;       // offer | accept | decline | reject | done | error
            public string token;
            public string senderName;
            public string fileName;
            public string mimeType;
            public long size;
            public string sha256;     // hex of the file bytes
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
                catch { break; } // listener stopped

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

            string problem = ValidateOffer(offer);
            if (problem != null)
            {
                await WriteHeaderAsync(stream, new Header { type = "reject", reason = problem }, IoTimeoutMs);
                return;
            }

            var pending = new PendingOffer(offer);
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
            string savedPath = null;
            try
            {
                await WriteHeaderAsync(stream, new Header { type = "accept" }, IoTimeoutMs);
                savedPath = await ReceiveBodyAsync(stream, offer, pending.Progress);
                await WriteHeaderAsync(stream, new Header { type = "done" }, IoTimeoutMs);
            }
            catch (Exception e)
            {
                pending.Completion.TrySetException(e);
                try { await WriteHeaderAsync(stream, new Header { type = "error", reason = e.Message }, 2000); } catch { }
                return;
            }

            pending.Completion.TrySetResult(savedPath);
            RotateToken();
        }

        private string ValidateOffer(Header h)
        {
            if (h == null || h.type != "offer") return "bad_request";
            if (!ConstantTimeEquals(h.token, LocalToken)) return "bad_token";
            if (!ReceivedStore.IsAllowedMime(h.mimeType)) return "type_not_allowed";
            if (h.size <= 0 || h.size > MaxFileBytes) return "size_not_allowed";
            if (string.IsNullOrEmpty(h.sha256) || h.sha256.Length != 64) return "bad_hash";
            return null;
        }

        private async Task<string> ReceiveBodyAsync(NetworkStream stream, Header offer, IProgress<float> progress)
        {
            var buffer = new byte[ChunkSize];
            long remaining = offer.size;
            long received = 0;
            FileStream file = null;
            string path = null;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                try
                {
                    while (remaining > 0)
                    {
                        int want = (int)Math.Min(buffer.Length, remaining);
                        int n = await ReadSomeAsync(stream, buffer, want, IoTimeoutMs);
                        if (n <= 0) throw new IOException("Connection closed before the file finished.");

                        if (file == null)
                        {
                            if (!ReceivedStore.MatchesMagic(offer.mimeType, buffer, n))
                                throw new IOException("File content does not match the declared image type.");
                            file = ReceivedStore.CreateNew(_receiveDir, offer.fileName, offer.mimeType, out path);
                        }

                        file.Write(buffer, 0, n);
                        hash.AppendData(buffer, 0, n);
                        remaining -= n;
                        received += n;
                        progress?.Report((float)received / offer.size);
                    }

                    file.Dispose();
                    file = null;

                    string actual = ToHex(hash.GetHashAndReset());
                    if (!string.Equals(actual, offer.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("SHA-256 mismatch.");
                    return path;
                }
                catch
                {
                    file?.Dispose();
                    if (path != null) { try { File.Delete(path); } catch { /* best effort cleanup of our own partial file */ } }
                    throw;
                }
            }
        }

        private void RotateToken()
        {
            LocalToken = NewToken();
            TokenChanged?.Invoke();
        }

        // ---------- sending ----------

        public async Task<TransferResult> SendAsync(PeerInfo peer, string senderName, string fileName, string mimeType,
            byte[] data, IProgress<float> progress)
        {
            if (peer == null || string.IsNullOrEmpty(peer.Ip) || peer.Port <= 0)
                return new TransferResult(TransferStatus.Failed, "peer has no address");
            if (!ReceivedStore.IsAllowedMime(mimeType))
                return new TransferResult(TransferStatus.Rejected, "type_not_allowed");
            if (data == null || data.Length == 0 || data.Length > MaxFileBytes)
                return new TransferResult(TransferStatus.Rejected, "size_not_allowed");

            try
            {
                using (var client = new TcpClient())
                {
                    Task connect = client.ConnectAsync(peer.Ip, peer.Port);
                    if (await Task.WhenAny(connect, Task.Delay(IoTimeoutMs)) != connect)
                        return new TransferResult(TransferStatus.TimedOut, "connect timed out");
                    await connect;

                    var stream = client.GetStream();
                    string sha;
                    using (var sha256 = SHA256.Create()) sha = ToHex(sha256.ComputeHash(data));

                    await WriteHeaderAsync(stream, new Header
                    {
                        type = "offer", token = peer.Token, senderName = senderName,
                        fileName = fileName, mimeType = mimeType, size = data.Length, sha256 = sha
                    }, IoTimeoutMs);

                    Header reply = await ReadHeaderAsync(stream, DecisionTimeoutMs + IoTimeoutMs);
                    if (reply.type == "reject") return new TransferResult(TransferStatus.Rejected, reply.reason);
                    if (reply.type == "decline") return new TransferResult(TransferStatus.Declined);
                    if (reply.type != "accept") return new TransferResult(TransferStatus.Failed, "unexpected reply " + reply.type);

                    int sent = 0;
                    while (sent < data.Length)
                    {
                        int n = Math.Min(ChunkSize, data.Length - sent);
                        await WriteAsync(stream, data, sent, n, IoTimeoutMs);
                        sent += n;
                        progress?.Report((float)sent / data.Length);
                    }

                    Header done = await ReadHeaderAsync(stream, IoTimeoutMs);
                    return done.type == "done"
                        ? new TransferResult(TransferStatus.Completed)
                        : new TransferResult(TransferStatus.Failed, done.reason ?? done.type);
                }
            }
            catch (TimeoutException e)
            {
                return new TransferResult(TransferStatus.TimedOut, e.Message);
            }
            catch (Exception e)
            {
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
            public readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>();
            public IProgress<float> Progress;

            public PendingOffer(Header h)
            {
                SenderName = h.senderName;
                FileName = h.fileName;
                MimeType = h.mimeType;
                Size = h.size;
            }

            public override Task<string> AcceptAsync(IProgress<float> progress)
            {
                Progress = progress;
                Decision.TrySetResult(true);
                return Completion.Task;
            }

            public override void Decline() => Decision.TrySetResult(false);
        }
    }
}


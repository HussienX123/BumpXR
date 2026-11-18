using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BumpXR.Core;
using BumpXR.Discovery;
using BumpXR.Transfer;
using BumpXR.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BumpXR.Tests
{
    /// <summary>Sender and receiver in one process over 127.0.0.1.</summary>
    public class TransferLoopbackTests
    {
        private string _dir;
        private LocalNetworkTransferService _receiver;
        private LocalNetworkTransferService _sender;
        private PeerInfo _peer;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "BumpXRTest_" + Guid.NewGuid().ToString("N"));
            _receiver = new LocalNetworkTransferService(_dir) { IoTimeoutMs = 3000, DecisionTimeoutMs = 3000 };
            _sender = new LocalNetworkTransferService(Path.Combine(_dir, "unused")) { IoTimeoutMs = 3000, DecisionTimeoutMs = 3000 };
            _receiver.StartListening();
            _peer = new PeerInfo { DisplayName = "rx", Ip = "127.0.0.1", Port = _receiver.ListenPort, Token = _receiver.LocalToken };
        }

        [TearDown]
        public void TearDown()
        {
            _receiver.Dispose();
            _sender.Dispose();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static byte[] FakeJpeg(int size)
        {
            var data = new byte[size];
            new System.Random(1234).NextBytes(data);
            data[0] = 0xFF; data[1] = 0xD8; data[2] = 0xFF; data[3] = 0xE0;
            return data;
        }

        private static string Sha(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)); }

        private static IEnumerator Wait<T>(Task<T> task, int seconds = 15)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (!task.IsCompleted && DateTime.UtcNow < end) yield return null;
            Assert.IsTrue(task.IsCompleted, "task timed out");
        }

        private bool NothingWritten() => !Directory.Exists(_dir) || Directory.GetFileSystemEntries(_dir).Length == 0;

        [UnityTest]
        public IEnumerator AcceptedTransfer_BytesMatchSha256()
        {
            byte[] payload = FakeJpeg(300 * 1024);
            Task<string> saved = null;
            bool filesExistBeforeAccept = true;
            _receiver.OfferReceived += o =>
            {
                filesExistBeforeAccept = !NothingWritten();
                saved = o.AcceptAsync(null);
            };

            var send = _sender.SendAsync(_peer, "tx", "holiday.jpg", "image/jpeg", payload, null);
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Completed, send.Result.Status, send.Result.ToString());
            yield return Wait(saved);

            Assert.IsFalse(filesExistBeforeAccept, "something was written before Accept");
            Assert.AreEqual(Sha(payload), Sha(File.ReadAllBytes(saved.Result)));
            Assert.IsTrue(Path.GetFullPath(saved.Result).StartsWith(Path.GetFullPath(_dir)));
            Assert.AreNotEqual(_peer.Token, _receiver.LocalToken, "token should rotate after a completed transfer");
        }

        [UnityTest]
        public IEnumerator DeclinedTransfer_WritesNothing()
        {
            bool filesExistAtOffer = true;
            _receiver.OfferReceived += o => { filesExistAtOffer = !NothingWritten(); o.Decline(); };

            var send = _sender.SendAsync(_peer, "tx", "x.jpg", "image/jpeg", FakeJpeg(50 * 1024), null);
            yield return Wait(send);

            Assert.AreEqual(TransferStatus.Declined, send.Result.Status);
            Assert.IsFalse(filesExistAtOffer);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator BadToken_IsRejected_NoOffer_WritesNothing()
        {
            int offers = 0;
            _receiver.OfferReceived += o => { offers++; o.AcceptAsync(null); };
            _peer.Token = "not-the-token";

            var send = _sender.SendAsync(_peer, "tx", "x.jpg", "image/jpeg", FakeJpeg(50 * 1024), null);
            yield return Wait(send);

            Assert.AreEqual(TransferStatus.Rejected, send.Result.Status);
            Assert.AreEqual("bad_token", send.Result.Message);
            Assert.AreEqual(0, offers);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator DisallowedType_IsRejected_WritesNothing()
        {
            _receiver.OfferReceived += o => o.AcceptAsync(null);
            var send = _sender.SendAsync(_peer, "tx", "x.exe", "application/x-msdownload", FakeJpeg(1024), null);
            yield return Wait(send);

            Assert.AreEqual(TransferStatus.Rejected, send.Result.Status);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator PathTraversalFileName_IsSanitized_AndNeverOverwrites()
        {
            Task<string> first = null, second = null;
            int count = 0;
            _receiver.OfferReceived += o => { if (count++ == 0) first = o.AcceptAsync(null); else second = o.AcceptAsync(null); };

            var send1 = _sender.SendAsync(_peer, "tx", "..\\..\\evil name.jpg", "image/jpeg", FakeJpeg(2048), null);
            yield return Wait(send1);
            Assert.AreEqual(TransferStatus.Completed, send1.Result.Status, send1.Result.ToString());
            yield return Wait(first);

            // The token rotated after the first transfer; read the new one like a fresh advertisement would.
            _peer.Token = _receiver.LocalToken;
            var send2 = _sender.SendAsync(_peer, "tx", "..\\..\\evil name.jpg", "image/jpeg", FakeJpeg(2048), null);
            yield return Wait(send2);
            Assert.AreEqual(TransferStatus.Completed, send2.Result.Status, send2.Result.ToString());
            yield return Wait(second);

            Assert.AreNotEqual(first.Result, second.Result);
            Assert.IsTrue(Directory.GetFiles(_dir).Length == 2);
            foreach (string f in new[] { first.Result, second.Result })
                Assert.AreEqual(Path.GetFullPath(_dir), Path.GetDirectoryName(Path.GetFullPath(f)));
            Assert.IsTrue(Directory.GetFiles(_dir).All(f => !Path.GetFileName(f).Contains("..")));
        }

        [Test]
        public void SanitizeFileName_Cases()
        {
            Assert.AreEqual("evil_name.jpg", ReceivedStore.SanitizeFileName("../../evil name.exe", "image/jpeg"));
            Assert.AreEqual("photo.png", ReceivedStore.SanitizeFileName("...", "image/png"));
        }

        [UnityTest]
        public IEnumerator BumpMain_DemoFriend_FullLoop_SendsAndReceivesPhoto()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("BumpMain");
                yield return null;
                yield return null;

                var ctrl = UnityEngine.Object.FindFirstObjectByType<BumpXR.UI.BumpMainController>();
                Assert.IsNotNull(ctrl, "BumpMainController not found in BumpMain scene");
                Assert.AreEqual(6, ctrl.PhotoCount, "Expected 6 sample photos");

                int beforeReceived = ctrl.ReceivedCount;
                ctrl.ToggleDemoFriend();
                Assert.IsTrue(ctrl.Demo.DemoFriendVisible, "Demo Friend should be visible after toggle");
                Assert.GreaterOrEqual(ctrl.PeerCount, 1, "Demo Friend should appear in peers list");

                ctrl.SelectPeerByIndex(0);
                ctrl.SelectPhotoByIndex(0);
                ctrl.Send();

                // Wait for Demo Friend to finish receiving our photo and send one back (~4.5s)
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                while (!ctrl.IncomingVisible && DateTime.UtcNow < deadline) yield return null;
                Assert.IsTrue(ctrl.IncomingVisible, "Incoming card from Demo Friend did not appear");

                ctrl.AcceptIncoming();
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (ctrl.ReceivedCount <= beforeReceived && DateTime.UtcNow < deadline) yield return null;
                Assert.AreEqual(beforeReceived + 1, ctrl.ReceivedCount, "Received photo count should increment after Accept");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        // ---- Phase 5A: Batch transfer safety, atomic cancel cleanup, and lazy My Captures / Multi-select ----

        private static byte[] FakePng(int size, int seed = 5678)
        {
            var data = new byte[Math.Max(16, size)];
            new System.Random(seed).NextBytes(data);
            byte[] magic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Buffer.BlockCopy(magic, 0, data, 0, magic.Length);
            return data;
        }

        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;
            public SyncProgress(Action<T> handler) => _handler = handler;
            public void Report(T value) => _handler(value);
        }

        [Serializable]
        private sealed class RawWireItem
        {
            public string name;
            public long size;
            public string type;
            public string sha256;
        }

        [Serializable]
        private sealed class RawWireHeader
        {
            public string type;
            public string token;
            public string senderName;
            public RawWireItem[] items;
            public string reason;
        }

        private static async Task<RawWireHeader> SendRawOfferAsync(int port, RawWireHeader offer)
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            NetworkStream s = client.GetStream();
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(offer));
            var frame = new byte[4 + body.Length];
            frame[0] = (byte)(body.Length >> 24);
            frame[1] = (byte)(body.Length >> 16);
            frame[2] = (byte)(body.Length >> 8);
            frame[3] = (byte)body.Length;
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            await s.WriteAsync(frame, 0, frame.Length);

            var lenBuf = new byte[4];
            int got = 0;
            while (got < 4) got += await s.ReadAsync(lenBuf, got, 4 - got);
            int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            var replyBuf = new byte[len];
            got = 0;
            while (got < len) got += await s.ReadAsync(replyBuf, got, len - got);
            return JsonUtility.FromJson<RawWireHeader>(Encoding.UTF8.GetString(replyBuf));
        }

        [UnityTest]
        public IEnumerator BatchAccepted_AllFilesMatchSha256_AndReportsSequentialProgress()
        {
            byte[] p0 = FakeJpeg(140 * 1024);
            byte[] p1 = FakePng(95 * 1024, 99);
            byte[] p2 = FakeJpeg(180 * 1024);
            var outgoing = new[]
            {
                new OutgoingFile("one.jpg", "image/jpeg", p0),
                new OutgoingFile("two.png", "image/png", p1),
                new OutgoingFile("three.jpg", "image/jpeg", p2)
            };

            Task<IReadOnlyList<string>> savedTask = null;
            var rxProgress = new List<BatchProgress>();
            int offerCount = 0;
            long offerSize = 0;
            bool emptyBeforeAccept = false;

            _receiver.OfferReceived += o =>
            {
                offerCount = o.Count;
                offerSize = o.TotalSize;
                emptyBeforeAccept = NothingWritten();
                savedTask = o.AcceptBatchAsync(new SyncProgress<BatchProgress>(bp => rxProgress.Add(bp)));
            };

            var txProgress = new List<BatchProgress>();
            Task<TransferResult> send = _sender.SendBatchAsync(_peer, "Layla", outgoing, new SyncProgress<BatchProgress>(bp => txProgress.Add(bp)));
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Completed, send.Result.Status, send.Result.ToString());
            yield return Wait(savedTask);

            Assert.IsTrue(emptyBeforeAccept, "Zero bytes may touch disk before AcceptBatchAsync.");
            Assert.AreEqual(3, offerCount);
            Assert.AreEqual(p0.Length + p1.Length + p2.Length, offerSize);
            Assert.AreEqual(3, savedTask.Result.Count);
            Assert.AreEqual(Sha(p0), Sha(File.ReadAllBytes(savedTask.Result[0])));
            Assert.AreEqual(Sha(p1), Sha(File.ReadAllBytes(savedTask.Result[1])));
            Assert.AreEqual(Sha(p2), Sha(File.ReadAllBytes(savedTask.Result[2])));
            Assert.IsTrue(txProgress.Any(p => p.FileIndex == 2 && p.FileProgress >= 0.99f && p.OverallProgress >= 0.99f));
            Assert.IsTrue(rxProgress.Any(p => p.FileIndex == 2 && p.FileProgress >= 0.99f && p.OverallProgress >= 0.99f));
        }

        [UnityTest]
        public IEnumerator BatchDeclined_WritesNothing()
        {
            _receiver.OfferReceived += o => o.Decline();
            var outgoing = new[]
            {
                new OutgoingFile("a.jpg", "image/jpeg", FakeJpeg(32 * 1024)),
                new OutgoingFile("b.png", "image/png", FakePng(32 * 1024))
            };

            Task<TransferResult> send = _sender.SendBatchAsync(_peer, "Layla", outgoing);
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Declined, send.Result.Status);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator BatchMixedDisallowedType_RejectsWholeBatch_WritesNothing()
        {
            int offers = 0;
            _receiver.OfferReceived += o => { offers++; o.AcceptBatchAsync(null); };

            var mixed = new[]
            {
                new OutgoingFile("good.jpg", "image/jpeg", FakeJpeg(8192)),
                new OutgoingFile("doc.pdf", "application/pdf", FakeJpeg(8192)),
                new OutgoingFile("good2.png", "image/png", FakePng(8192))
            };
            Task<TransferResult> send = _sender.SendBatchAsync(_peer, "Layla", mixed);
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Rejected, send.Result.Status);

            string dummyHash = new string('a', 64);
            Task<RawWireHeader> wire = SendRawOfferAsync(_receiver.ListenPort, new RawWireHeader
            {
                type = "offer",
                token = _receiver.LocalToken,
                senderName = "Layla",
                items = new[]
                {
                    new RawWireItem { name = "good.jpg", size = 4096, type = "image/jpeg", sha256 = dummyHash },
                    new RawWireItem { name = "evil.pdf", size = 4096, type = "application/pdf", sha256 = dummyHash }
                }
            });
            yield return Wait(wire);
            Assert.AreEqual("reject", wire.Result.type);
            Assert.AreEqual("type_not_allowed", wire.Result.reason);
            Assert.AreEqual(0, offers);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator BatchOversizeItemAndOversizeTotal_RejectsWholeBatch_WritesNothing()
        {
            int offers = 0;
            _receiver.OfferReceived += o => { offers++; o.AcceptBatchAsync(null); };
            string dummyHash = new string('b', 64);

            // 1) Single item > 20 MB mixed with a valid item -> whole batch rejected on wire
            Task<RawWireHeader> overItemWire = SendRawOfferAsync(_receiver.ListenPort, new RawWireHeader
            {
                type = "offer",
                token = _receiver.LocalToken,
                senderName = "Layla",
                items = new[]
                {
                    new RawWireItem { name = "ok.jpg", size = 4096, type = "image/jpeg", sha256 = dummyHash },
                    new RawWireItem { name = "huge.jpg", size = LocalNetworkTransferService.MaxFileBytes + 1, type = "image/jpeg", sha256 = dummyHash }
                }
            });
            yield return Wait(overItemWire);
            Assert.AreEqual("reject", overItemWire.Result.type);
            Assert.AreEqual("size_not_allowed", overItemWire.Result.reason);

            // 2) Six 18 MB items = 108 MB (> 100 MB batch cap, each <= 20 MB) -> whole batch rejected on wire
            long eighteenMb = 18L * 1024 * 1024;
            Task<RawWireHeader> overBatchWire = SendRawOfferAsync(_receiver.ListenPort, new RawWireHeader
            {
                type = "offer",
                token = _receiver.LocalToken,
                senderName = "Layla",
                items = Enumerable.Range(0, 6).Select(i => new RawWireItem
                {
                    name = $"p{i}.jpg", size = eighteenMb, type = "image/jpeg", sha256 = dummyHash
                }).ToArray()
            });
            yield return Wait(overBatchWire);
            Assert.AreEqual("reject", overBatchWire.Result.type);
            Assert.AreEqual("batch_size_not_allowed", overBatchWire.Result.reason);
            Assert.AreEqual(0, offers);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator BatchOverTenItems_Rejected_WritesNothing()
        {
            int offers = 0;
            _receiver.OfferReceived += o => { offers++; o.AcceptBatchAsync(null); };

            byte[] tiny = FakeJpeg(2048);
            OutgoingFile[] eleven = Enumerable.Range(0, 11)
                .Select(i => new OutgoingFile($"img_{i}.jpg", "image/jpeg", tiny))
                .ToArray();

            Task<TransferResult> send = _sender.SendBatchAsync(_peer, "Layla", eleven);
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Rejected, send.Result.Status);

            string dummyHash = new string('c', 64);
            Task<RawWireHeader> wire = SendRawOfferAsync(_receiver.ListenPort, new RawWireHeader
            {
                type = "offer",
                token = _receiver.LocalToken,
                senderName = "Layla",
                items = Enumerable.Range(0, 11).Select(i => new RawWireItem
                {
                    name = $"img_{i}.jpg", size = 2048, type = "image/jpeg", sha256 = dummyHash
                }).ToArray()
            });
            yield return Wait(wire);
            Assert.AreEqual("reject", wire.Result.type);
            Assert.AreEqual("too_many_items", wire.Result.reason);
            Assert.AreEqual(0, offers);
            Assert.IsTrue(NothingWritten());
        }

        [UnityTest]
        public IEnumerator BatchMidStreamCancel_LeavesNoPartialOrCompletedBatchFiles()
        {
            Task<IReadOnlyList<string>> rxTask = null;
            _receiver.OfferReceived += o => rxTask = o.AcceptBatchAsync(null);

            using var cts = new CancellationTokenSource();
            var outgoing = new[]
            {
                new OutgoingFile("completed_first.jpg", "image/jpeg", FakeJpeg(64 * 1024)),
                new OutgoingFile("aborted_second.jpg", "image/jpeg", FakeJpeg(256 * 1024))
            };

            // Cancel synchronously right after the first 64 KB chunk of file #2 is written over the socket.
            var cancelMidSecond = new SyncProgress<BatchProgress>(bp =>
            {
                if (bp.FileIndex == 1 && bp.FileProgress > 0f && bp.FileProgress < 1f)
                    cts.Cancel();
            });

            Task<TransferResult> send = _sender.SendBatchAsync(_peer, "Layla", outgoing, cancelMidSecond, cts.Token);
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Failed, send.Result.Status);

            DateTime end = DateTime.UtcNow.AddSeconds(5);
            while (rxTask == null || (!rxTask.IsCompleted && DateTime.UtcNow < end)) yield return null;
            Assert.IsNotNull(rxTask);
            Assert.IsTrue(rxTask.IsFaulted || rxTask.IsCanceled, "Receiver batch task must fault/cancel on mid-stream abort.");
            Assert.IsTrue(!Directory.Exists(_dir) || Directory.GetFiles(_dir).Length == 0,
                "Atomic cleanup must delete both the partial file and any earlier files from the aborted batch.");
        }

        [UnityTest]
        public IEnumerator BatchPathTraversal_SanitizedUniqueNames_NeverOverwrites()
        {
            Task<IReadOnlyList<string>> savedTask = null;
            IncomingOffer capturedOffer = null;
            _receiver.OfferReceived += o =>
            {
                capturedOffer = o;
                savedTask = o.AcceptBatchAsync(null);
            };

            byte[] a = FakeJpeg(4096);
            byte[] b = FakeJpeg(6144);
            byte[] c = FakePng(4096);
            var outgoing = new[]
            {
                new OutgoingFile("../../evil name.exe", "image/jpeg", a),
                new OutgoingFile("..\\..\\evil name.exe", "image/jpeg", b),
                new OutgoingFile("../sub/../../dot..png", "image/png", c)
            };

            Task<TransferResult> send = _sender.SendBatchAsync(_peer, "Layla", outgoing);
            yield return Wait(send);
            Assert.AreEqual(TransferStatus.Completed, send.Result.Status);
            yield return Wait(savedTask);

            Assert.IsTrue(capturedOffer.Items.All(i => !i.SafeName.Contains("..") && !i.SafeName.Contains("/") && !i.SafeName.Contains("\\")));
            Assert.AreEqual(3, savedTask.Result.Count);
            Assert.AreEqual(3, savedTask.Result.Distinct().Count(), "Duplicate sanitized names in one batch must never overwrite.");
            foreach (string f in savedTask.Result)
            {
                Assert.AreEqual(Path.GetFullPath(_dir), Path.GetDirectoryName(Path.GetFullPath(f)));
                Assert.IsFalse(Path.GetFileName(f).Contains(".."));
            }
            Assert.AreEqual(Sha(a), Sha(File.ReadAllBytes(savedTask.Result[0])));
            Assert.AreEqual(Sha(b), Sha(File.ReadAllBytes(savedTask.Result[1])));
            Assert.AreEqual(Sha(c), Sha(File.ReadAllBytes(savedTask.Result[2])));
        }

        private sealed class FakeCapturesBackend : ICapturesBackend
        {
            public bool Permitted;
            public int TotalAvailable = 65;
            private readonly byte[] _thumb = FakeJpeg(1024);
            private readonly byte[] _full = FakeJpeg(16384);

            public bool HasPermission() => Permitted;
            public Task<bool> RequestPermissionAsync() => Task.FromResult(Permitted);

            public Task<List<MediaItem>> QueryRecentImagesAsync(int maxCount)
            {
                var list = new List<MediaItem>();
                for (int i = 0; i < TotalAvailable; i++)
                {
                    list.Add(new MediaItem
                    {
                        Id = $"content://media/external/images/media/{1000 - i}",
                        FileName = $"quest_shot_{i:00}.jpg",
                        MimeType = "image/jpeg",
                        Size = _full.Length
                    });
                }
                return Task.FromResult(list);
            }

            public Task<byte[]> LoadThumbnailBytesAsync(string contentUri, int maxSidePx) => Task.FromResult(_thumb);
            public byte[] ReadContentStreamBytes(string contentUri, long maxBytes) => _full;
        }

        [UnityTest]
        public IEnumerator BumpMain_LazyMyCapturesPermission_MultiSelectCap_AndBatchDemoFriend()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("BumpMain");
                yield return null;
                yield return null;

                var ctrl = UnityEngine.Object.FindFirstObjectByType<BumpMainController>();
                Assert.IsNotNull(ctrl);

                // Inject a denied backend and confirm ZERO permission requests occur before opening "My Captures".
                var deniedBackend = new FakeCapturesBackend { Permitted = false };
                ctrl.SetCapturesBackendForTest(deniedBackend);
                Assert.AreEqual(0, ctrl.Captures.PermissionRequestCount, "First launch must trigger zero permission requests.");

                ctrl.ToggleDemoFriend();
                ctrl.SelectPeerByIndex(0);
                Assert.AreEqual(0, ctrl.Captures.PermissionRequestCount, "Demo Friend path must trigger zero permission requests.");

                // Opening "My Captures" lazily requests permission; when denied, shows one explanatory line and keeps Samples working.
                Task openDenied = ctrl.OpenTabAsync(MediaTab.Captures);
                while (!openDenied.IsCompleted) yield return null;
                yield return null;
                Assert.AreEqual(1, ctrl.Captures.PermissionRequestCount);
                Assert.AreEqual(CapturesMediaSource.DeniedMessage, ctrl.TabMessageText);
                Assert.IsFalse(ctrl.TabMessageText.Contains("\n"), "Denied message must be a single explanatory line.");

                Task backToSamples = ctrl.OpenTabAsync(MediaTab.Samples);
                while (!backToSamples.IsCompleted) yield return null;
                yield return null;
                Assert.AreEqual(6, ctrl.PhotoCount, "Samples tab must remain usable after Captures denial.");

                // Grant permission with 65 items: capped at 60, Select all caps at 10, thumbnails never call full-res ReadBytes.
                var grantedBackend = new FakeCapturesBackend { Permitted = true, TotalAvailable = 65 };
                ctrl.SetCapturesBackendForTest(grantedBackend);
                Task openGranted = ctrl.OpenTabAsync(MediaTab.Captures);
                while (!openGranted.IsCompleted) yield return null;
                yield return null;
                yield return null;
                Assert.AreEqual(60, ctrl.PhotoCount, "My Captures must cap at newest 60 images.");
                Assert.AreEqual(0, ctrl.Captures.FullReadCount, "Browsing thumbnails must never read full-resolution bytes.");

                ctrl.SelectAllPhotos();
                yield return null;
                Assert.AreEqual(10, ctrl.SelectedCount, "Multi-select must cap at 10 items.");
                Assert.AreEqual("10/10", ctrl.CountBadgeText);
                Assert.AreEqual("10 photos to Demo Friend", ctrl.ArmedText);

                ctrl.ClearSelection();
                yield return null;
                Assert.AreEqual(0, ctrl.SelectedCount);
                Assert.AreEqual("0/10", ctrl.CountBadgeText);

                // Select 2 items in Samples, verify armed state reads "2 photos to Demo Friend", send batch, and verify batch incoming card.
                Task openSamples2 = ctrl.OpenTabAsync(MediaTab.Samples);
                while (!openSamples2.IsCompleted) yield return null;
                ctrl.TogglePhotoSlot(0);
                ctrl.TogglePhotoSlot(1);
                yield return null;
                Assert.AreEqual("2 photos to Demo Friend", ctrl.ArmedText);

                int beforeReceived = ctrl.ReceivedCount;
                ctrl.Send();
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!ctrl.IncomingVisible && DateTime.UtcNow < deadline) yield return null;
                Assert.IsTrue(ctrl.IncomingVisible, "Demo Friend batch reply card did not appear.");

                var card = Resources.FindObjectsOfTypeAll<RectTransform>().First(r => r.name == "IncomingCard" && r.gameObject.scene.IsValid());
                Assert.AreEqual(0, card.GetComponentsInChildren<RawImage>(true).Length, "Incoming card must have NO image preview before Accept.");
                StringAssert.Contains("Demo Friend wants to send", ctrl.IncomingSummaryText);

                ctrl.AcceptIncoming();
                deadline = DateTime.UtcNow.AddSeconds(12);
                while (ctrl.IncomingVisible && DateTime.UtcNow < deadline) yield return null;
                int gained = ctrl.ReceivedCount - beforeReceived;
                Assert.GreaterOrEqual(gained, 2, "Demo Friend multi-photo reply should send 2-3 photos.");
                Assert.LessOrEqual(gained, 3);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }
    }
}

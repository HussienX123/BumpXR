using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BumpXR.Core;
using BumpXR.Transfer;
using NUnit.Framework;
using UnityEngine.TestTools;

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
            new Random(1234).NextBytes(data);
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
    }
}

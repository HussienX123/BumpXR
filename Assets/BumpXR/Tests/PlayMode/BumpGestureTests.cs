using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using BumpXR.Core;
using BumpXR.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BumpXR.Tests
{
    /// <summary>Synthetic hand trajectories (72 Hz) against BumpDetectorCore + BumpMain bump loop with the Demo Friend.</summary>
    public class BumpGestureTests
    {
        private const float Dt = 1f / 72f;
        private BumpSettings _s;
        private BumpDetectorCore _core;
        private int _bumps;

        [SetUp]
        public void SetUp()
        {
            _s = ScriptableObject.CreateInstance<BumpSettings>();
            _core = new BumpDetectorCore(_s);
            _bumps = 0;
            _core.Bumped += _ => _bumps++;
        }

        private void Feed(float seconds, Func<float, Vector3> pos, Func<float, bool> fist, float startTime = 0f)
        {
            for (float t = startTime; t < startTime + seconds; t += Dt)
                _core.Update(1, false, t, pos(t), true, fist(t), Vector3.forward);
        }

        // z(t) moves forward at `v` m/s for 0.25 s ending at time tb, then stops dead.
        private static float Approach(float t, float tb, float v) => v * Mathf.Clamp(t - (tb - 0.25f), 0f, 0.25f);

        [Test]
        public void FastApproachThenStop_WithFist_IsBump()
        {
            Feed(1.0f, t => new Vector3(0f, 1f, Approach(t, 0.6f, 2.0f)), t => true);
            Assert.AreEqual(1, _bumps);
        }

        [Test]
        public void SlowMotion_IsNotBump()
        {
            Feed(3.0f, t => new Vector3(0f, 1f, Mathf.Min(0.3f * t, 0.5f)), t => true); // 0.3 m/s then a gentle halt
            Assert.AreEqual(0, _bumps);
        }

        [Test]
        public void OpenHand_FastStop_IsNotBump()
        {
            Feed(1.0f, t => new Vector3(0f, 1f, Approach(t, 0.6f, 2.0f)), t => false);
            Assert.AreEqual(0, _bumps);
        }

        [Test]
        public void StrayWave_IsNotBump()
        {
            // 3 Hz sideways wave, ~2.8 m/s peak lateral speed, fist closed, abrupt end: almost no head-forward component.
            Feed(1.5f, t => new Vector3(t < 1.2f ? 0.15f * Mathf.Sin(2f * Mathf.PI * 3f * t) : 0f, 1f, 0f), t => true);
            Assert.AreEqual(0, _bumps);
        }

        [Test]
        public void PunchAndRetract_IsNotBump()
        {
            // forward 2 m/s for 0.25 s, then immediately straight back at 2 m/s
            Feed(1.0f, t => new Vector3(0f, 1f, t < 0.35f ? 2f * Mathf.Clamp(t - 0.1f, 0f, 0.25f) : 0.5f - 2f * Mathf.Min(t - 0.35f, 0.25f)), t => true);
            Assert.AreEqual(0, _bumps);
        }

        [Test]
        public void Cooldown_IsRespected()
        {
            // bumps ending at 0.6 s, 1.2 s (inside the 1.5 s cooldown) and 2.4 s (after it)
            Func<float, Vector3> pos = t => new Vector3(0f, 1f, Approach(t, 0.6f, 2f) + Approach(t, 1.2f, 2f) + Approach(t, 2.4f, 2f));
            Feed(1.8f, pos, t => true);
            Assert.AreEqual(1, _bumps, "second bump inside the cooldown must be ignored");
            Feed(1.2f, pos, t => true, 1.8f);
            Assert.AreEqual(2, _bumps, "bump after the cooldown must count");
        }

        [Test]
        public void ForceBump_HonoursCooldown()
        {
            Assert.IsTrue(_core.ForceBump(10f, Vector3.zero));
            Assert.IsFalse(_core.ForceBump(10.5f, Vector3.zero));
            Assert.IsTrue(_core.ForceBump(11.6f, Vector3.zero));
        }

        [Test]
        public void SimultaneousBump_WindowRules()
        {
            Assert.IsTrue(BumpSettings.IsSimultaneous(5f, true, 5.9f, 1f));
            Assert.IsFalse(BumpSettings.IsSimultaneous(5f, true, 6.2f, 1f), "bump too old");
            Assert.IsFalse(BumpSettings.IsSimultaneous(5f, false, 5.5f, 1f), "bump was not made with the Share panel open");
            Assert.IsFalse(BumpSettings.IsSimultaneous(5f, true, 4.9f, 1f), "bump after the card");
        }

        [Test]
        public void UntrackedSecondHand_DoesNotMaskTrackedHandState()
        {
            _core.Update(1, false, 0f, Vector3.zero, true, true, Vector3.forward);
            _core.Update(1, false, Dt, new Vector3(0f, 0f, 0.04f), true, true, Vector3.forward);
            _core.Update(2, true, Dt, Vector3.zero, false, false, Vector3.forward); // left hand resting out of view
            Assert.AreNotEqual("No hands", _core.State);
        }

        // ---- BumpMain scene (Editor, Demo Friend, no hands: bumps come through the Dev-overlay path) ----

        private static bool PortAccepts(int port)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    IAsyncResult r = c.BeginConnect("127.0.0.1", port, null, null);
                    return r.AsyncWaitHandle.WaitOne(500) && c.Connected;
                }
            }
            catch (SocketException) { return false; }
        }

        private static void AssertInsideGlassesBox(RectTransform rt, string what)
        {
            Transform cam = Camera.main.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            foreach (Vector3 c in corners)
            {
                Vector3 l = cam.InverseTransformPoint(c);
                float h = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
                float v = Mathf.Atan2(l.y, l.z) * Mathf.Rad2Deg;
                Assert.LessOrEqual(Mathf.Abs(h), 25f, $"{what} horizontal {h:0.0} deg");
                Assert.LessOrEqual(Mathf.Abs(v), 22f, $"{what} vertical {v:0.0} deg");
            }
        }

        [UnityTest]
        public IEnumerator BumpMain_DemoFriend_BumpLoop_SendsThenAcceptsByBump()
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

                ctrl.ToggleDemoFriend();
                Assert.IsFalse(ctrl.IsArmed);
                ctrl.SelectPeerByIndex(0);
                ctrl.SelectPhotoByIndex(0);
                yield return null;
                Assert.IsTrue(ctrl.IsArmed, "photo + peer selected must arm");
                yield return null;
                StringAssert.Contains("Armed", ctrl.StatusLine);
                AssertInsideGlassesBox((RectTransform)GameObject.Find("ShareCanvas").transform, "Share panel");

                int before = ctrl.ReceivedCount;
                ctrl.SimulateBump(); // armed: the bump sends
                yield return null;
                Assert.IsTrue(ctrl.IsSending, "armed bump should start the send");

                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!ctrl.IncomingVisible && DateTime.UtcNow < deadline) yield return null;
                Assert.IsTrue(ctrl.IncomingVisible, "Demo Friend card did not appear");
                yield return null;
                var card = Resources.FindObjectsOfTypeAll<RectTransform>().First(r => r.name == "IncomingCard" && r.gameObject.scene.IsValid());
                AssertInsideGlassesBox(card, "Incoming card");

                yield return new WaitForSecondsRealtime(1.6f); // past the bump cooldown
                ctrl.SimulateBump(); // receiver: a bump accepts the visible card
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (ctrl.ReceivedCount <= before && DateTime.UtcNow < deadline) yield return null;
                Assert.AreEqual(before + 1, ctrl.ReceivedCount, "bump should accept the incoming card");
                Assert.IsFalse(ctrl.IncomingVisible);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        [UnityTest]
        public IEnumerator BumpMain_NeverAcceptsWithoutBumpOrPoke()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("BumpMain");
                yield return null;
                yield return null;
                var ctrl = UnityEngine.Object.FindFirstObjectByType<BumpMainController>();
                ctrl.ToggleDemoFriend();
                ctrl.SelectPeerByIndex(0);
                ctrl.SelectPhotoByIndex(1);
                int before = ctrl.ReceivedCount;
                ctrl.Send(); // poke fallback sends; no bump happens afterwards
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!ctrl.IncomingVisible && DateTime.UtcNow < deadline) yield return null;
                Assert.IsTrue(ctrl.IncomingVisible);
                yield return new WaitForSecondsRealtime(3f);
                Assert.IsTrue(ctrl.IncomingVisible, "card must stay until bump/poke/decline");
                Assert.AreEqual(before, ctrl.ReceivedCount);
                ctrl.DeclineIncoming();
                Assert.IsFalse(ctrl.IncomingVisible);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        [UnityTest]
        public IEnumerator BumpMain_PauseResume_RestartsListenerCleanly()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("BumpMain");
                yield return null;
                yield return null;
                var ctrl = UnityEngine.Object.FindFirstObjectByType<BumpMainController>();
                Assert.Greater(ctrl.ListenPort, 0);
                Assert.IsTrue(PortAccepts(ctrl.ListenPort), "listener should accept before pause");

                ctrl.SuspendNetworking();
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.IsTrue(ctrl.IsSuspended);
                Assert.IsFalse(PortAccepts(ctrl.ListenPort), "listener must be closed while suspended");

                ctrl.SuspendNetworking(); // double pause is harmless
                ctrl.ResumeNetworking();
                ctrl.ResumeNetworking(); // double resume is harmless
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.IsFalse(ctrl.IsSuspended);
                Assert.Greater(ctrl.ListenPort, 0);
                Assert.IsTrue(PortAccepts(ctrl.ListenPort), "listener must accept again after resume");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        [Test]
        public void BumpSettings_PersistsToPlayerPrefs_AndResetRestoresAssetDefaults()
        {
            BumpSettings asset = Resources.Load<BumpSettings>("BumpSettings");
            Assert.IsNotNull(asset, "Resources/BumpSettings.asset must exist");
            BumpSettings work = UnityEngine.Object.Instantiate(asset);
            BumpSettings reloaded = UnityEngine.Object.Instantiate(asset);
            try
            {
                work.ResetToDefaults(asset);
                work.speedThreshold = 1.15f;
                work.fistCurlDegrees = 215f;
                work.decelRatio = 0.40f;
                work.SaveOverrides();

                reloaded.LoadOverrides();
                Assert.AreEqual(1.15f, reloaded.speedThreshold, 0.001f);
                Assert.AreEqual(215f, reloaded.fistCurlDegrees, 0.001f);
                Assert.AreEqual(0.40f, reloaded.decelRatio, 0.001f);
                Assert.AreEqual("BUMPTUNE speed=1.15 curl=215 decel=0.40", reloaded.FormatTuneLine());

                reloaded.ResetToDefaults(asset);
                Assert.AreEqual(asset.speedThreshold, reloaded.speedThreshold, 0.001f);
                Assert.AreEqual(asset.fistCurlDegrees, reloaded.fistCurlDegrees, 0.001f);
                Assert.AreEqual(asset.decelRatio, reloaded.decelRatio, 0.001f);
                Assert.IsFalse(PlayerPrefs.HasKey(BumpSettings.PrefSpeedKey));
                Assert.IsFalse(PlayerPrefs.HasKey(BumpSettings.PrefCurlKey));
                Assert.IsFalse(PlayerPrefs.HasKey(BumpSettings.PrefDecelKey));
            }
            finally
            {
                work.ResetToDefaults(asset);
                UnityEngine.Object.DestroyImmediate(work);
                UnityEngine.Object.DestroyImmediate(reloaded);
            }
        }

        [UnityTest]
        public IEnumerator BumpMain_ColdStartFailures_AndRuntimeTuning_NeverDeadEnd()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            BumpMainController ctrl = null;
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("BumpMain");
                yield return null;
                yield return null;
                ctrl = UnityEngine.Object.FindFirstObjectByType<BumpMainController>();
                Assert.IsNotNull(ctrl);

                // Startup timing (< 5 s target) + About line
                Assert.Less(ctrl.InteractiveDelaySeconds, 5f, "Share panel must be interactive in under 5 s");
                StringAssert.Contains("panel interactive", ctrl.TimingLog);
                StringAssert.Contains(Application.version, ctrl.AboutLine);
                StringAssert.Contains("Photos stay on your devices. Sent over local Wi-Fi only after the receiver accepts.", ctrl.AboutLine);

                // 1) Colocation permission / Enhanced Spatial Services off
                ctrl.SimulateColdStartState("192.168.1.10", NetworkReachability.ReachableViaLocalAreaNetwork, "StartDiscovery failed: PermissionDenied");
                Assert.IsFalse(ctrl.StatusLine.Contains("\n"));
                StringAssert.Contains("Spatial Services", ctrl.StatusLine);

                // 2) No Wi-Fi
                ctrl.SimulateColdStartState("(none)", NetworkReachability.NotReachable, "");
                Assert.IsFalse(ctrl.StatusLine.Contains("\n"));
                StringAssert.Contains("No Wi-Fi", ctrl.StatusLine);

                // 3) No internet
                ctrl.SimulateColdStartState("192.168.1.10", NetworkReachability.NotReachable, "StartAdvertisement failed: NetworkFailed");
                Assert.IsFalse(ctrl.StatusLine.Contains("\n"));
                StringAssert.Contains("No internet", ctrl.StatusLine);

                // 4) Bluetooth off
                ctrl.SimulateColdStartState("192.168.1.10", NetworkReachability.ReachableViaLocalAreaNetwork, "StartDiscovery failed: BluetoothDisabled");
                Assert.IsFalse(ctrl.StatusLine.Contains("\n"));
                StringAssert.Contains("Bluetooth off", ctrl.StatusLine);

                // 5) No hand tracked for 10 s while Share panel is open
                ctrl.SimulateColdStartState("192.168.1.10", NetworkReachability.ReachableViaLocalAreaNetwork, "", noHandsSeconds: 10.5f);
                Assert.AreEqual(BumpMainController.HandsPromptMessage, ctrl.CoachLine);
                Assert.AreEqual(BumpMainController.HandsPromptMessage, ctrl.StatusLine);

                // Demo Friend path still works even when Wi-Fi & Colocation are down
                ctrl.SimulateColdStartState("(none)", NetworkReachability.NotReachable, "StartDiscovery failed: PermissionDenied");
                ctrl.ToggleDemoFriend();
                ctrl.SelectPeerByIndex(0);
                ctrl.SelectPhotoByIndex(0);
                yield return null;
                Assert.IsTrue(ctrl.IsArmed, "Demo Friend must still arm under cold-start failures");
                Assert.AreEqual("Armed: bump to send", ctrl.StatusLine);

                // Runtime Dev overlay tuning +/- + Print + Reset
                ctrl.ResetTuning();
                ctrl.AdjustSpeed(0.10f);
                ctrl.AdjustCurl(10f);
                ctrl.AdjustDecel(0.05f);
                LogAssert.Expect(LogType.Log, "BUMPTUNE speed=2.00 curl=210 decel=0.30");
                ctrl.PrintTuning();
                Assert.AreEqual("BUMPTUNE speed=2.00 curl=210 decel=0.30", ctrl.LastPrintedTuneLine);
                ctrl.ResetTuning();
                Assert.AreEqual(1.90f, ctrl.Settings.speedThreshold, 0.001f);
            }
            finally
            {
                if (ctrl != null) ctrl.ResetTuning();
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        // ---- Phase 5A: False-bump gates, clip recording/replay, and 5-poke Dev reveal ----

        private int FeedWithHead(
            float seconds,
            Func<float, (Vector3 headPos, Quaternion headRot, Vector3 wristPos, bool tracked, bool fist)> frame)
        {
            var core = new BumpDetectorCore(_s) { LogCandidates = false };
            int count = 0;
            core.Bumped += _ => count++;
            for (float t = 0f; t <= seconds; t += Dt)
            {
                var f = frame(t);
                core.Update(1, false, t, f.wristPos, f.tracked, f.fist, f.headPos, f.headRot);
            }
            return count;
        }

        [Test]
        public void HeadPose_RealSeatedFistBump_DoesBump()
        {
            var headPos = new Vector3(0f, 1.2f, 0f);
            Quaternion headRot = Quaternion.identity;
            // Fist tracked steadily at 0.22 m for 0.45 s, pushes forward 0.24 m to 0.46 m over 0.11 s (~2.18 m/s, matching measured on-device bumps), stops cleanly.
            int bumps = FeedWithHead(1.1f, t =>
            {
                float z = 0.22f + 0.24f * Mathf.Clamp01((t - 0.45f) / 0.11f);
                Vector3 driftHead = headPos + new Vector3(0f, 0f, 0.01f * Mathf.Sin(2f * t)); // natural <0.03 m/s micro-drift
                return (driftHead, headRot, new Vector3(0.05f, 1.12f, z), true, true);
            });
            Assert.AreEqual(1, bumps, "A real seated fist bump in the reach zone must fire once.");
        }

        [Test]
        public void FastHeadYaw180DegPerSec_WithStillFist_IsNotBump()
        {
            var headPos = new Vector3(0f, 1.2f, 0f);
            // Case A: fist resting still in world space while head yaws at 180 deg/s then stops.
            int worldStillBumps = FeedWithHead(1.2f, t =>
            {
                float yaw = 180f * Mathf.Clamp(t - 0.4f, 0f, 0.4f);
                return (headPos, Quaternion.Euler(0f, yaw, 0f), new Vector3(0.08f, 1.1f, 0.42f), true, true);
            });
            Assert.AreEqual(0, worldStillBumps, "World-still fist during 180 deg/s head yaw must not bump.");

            // Case B: chair swivel / torso-fixed fist sweeping at 180 deg/s with the head then stopping abruptly.
            int bodyFixedBumps = FeedWithHead(1.2f, t =>
            {
                float yaw = 180f * Mathf.Clamp(t - 0.4f, 0f, 0.4f);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 wrist = headPos + rot * new Vector3(0.08f, -0.1f, 0.42f);
                return (headPos, rot, wrist, true, true);
            });
            Assert.AreEqual(0, bodyFixedBumps, "Torso-fixed fist during 180 deg/s yaw must be suppressed by head-motion/direction gates.");
        }

        [Test]
        public void HeadNod_IsNotBump()
        {
            var headPos = new Vector3(0f, 1.2f, 0f);
            int bumps = FeedWithHead(1.3f, t =>
            {
                bool nodding = t >= 0.4f && t <= 0.85f;
                float pitch = nodding ? 28f * Mathf.Sin(2f * Mathf.PI * 3.0f * (t - 0.4f)) : 0f;
                Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
                // Slight sympathetic arm bobbing during vigorous nod, stopping at t = 0.85 s
                Vector3 wrist = headPos + rot * new Vector3(0.05f, -0.12f, 0.40f);
                return (headPos, rot, wrist, true, true);
            });
            Assert.AreEqual(0, bumps, "Vigorous head nod must not trigger a false bump.");
        }

        [Test]
        public void HeadShake_WithHandsNearFace_IsNotBump()
        {
            var headPos = new Vector3(0f, 1.2f, 0f);
            int bumps = FeedWithHead(1.3f, t =>
            {
                bool shaking = t >= 0.35f && t <= 0.85f;
                float yaw = shaking ? 25f * Mathf.Sin(2f * Mathf.PI * 4f * (t - 0.35f)) : 0f;
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                // Hands held near face/visor (0.16 m < minReachDistance 0.25 m) pushing slightly forward then stopping
                float z = 0.12f + 0.06f * Mathf.Clamp01((t - 0.5f) / 0.12f);
                return (headPos, rot, new Vector3(0.06f, 1.18f, z), true, true);
            });
            Assert.AreEqual(0, bumps, "Head shake with hands near the face must be rejected by head-motion & reach-zone gates.");
        }

        [Test]
        public void TrackingDropoutThenReacquireJump_IsNotBump()
        {
            var headPos = new Vector3(0f, 1.2f, 0f);
            Quaternion headRot = Quaternion.identity;

            // Subcase A: brief dropout then immediate fast snap-in (< 300 ms continuous tracking before stop)
            int dropoutSnap = FeedWithHead(1.2f, t =>
            {
                if (t < 0.4f) return (headPos, headRot, new Vector3(0f, 1.1f, 0.22f), true, true);
                if (t < 0.55f) return (headPos, headRot, Vector3.zero, false, false); // tracking lost
                // Re-acquired at 0.55 s and stops at 0.70 s (only 150 ms < 300 ms minTrackedDuration)
                float z = 0.24f + 0.20f * Mathf.Clamp01((t - 0.55f) / 0.12f);
                return (headPos, headRot, new Vector3(0f, 1.1f, z), true, true);
            });
            Assert.AreEqual(0, dropoutSnap, "Stop within 300 ms of tracking re-acquisition must not bump.");

            // Subcase B: single-frame wrist teleport > 0.25 m (0.20 m -> 0.52 m in one frame) then stationary
            int frameJump = FeedWithHead(1.2f, t =>
            {
                float z = t < 0.6f ? 0.20f : 0.52f; // 0.32 m single-frame jump!
                return (headPos, headRot, new Vector3(0f, 1.1f, z), true, true);
            });
            Assert.AreEqual(0, frameJump, "Single-frame wrist jump > 0.25 m must be rejected.");
        }

        [Test]
        public void WalkingSway_IsNotBump()
        {
            // User walks forward at 1.15 m/s with arm swinging in fist, then halts abruptly at t = 0.9 s.
            int bumps = FeedWithHead(1.4f, t =>
            {
                float walkZ = 1.15f * Mathf.Clamp(t, 0f, 0.9f);
                float swayX = t < 0.9f ? 0.04f * Mathf.Sin(2f * Mathf.PI * 1.8f * t) : 0f;
                float armSwingZ = t < 0.9f ? 0.08f * Mathf.Sin(2f * Mathf.PI * 1.8f * t) : 0f;
                var headPos = new Vector3(swayX, 1.2f, walkZ);
                var wristPos = headPos + new Vector3(0.12f, -0.18f, 0.36f + armSwingZ);
                return (headPos, Quaternion.identity, wristPos, true, true);
            });
            Assert.AreEqual(0, bumps, "Walking forward with arm sway must be suppressed by the head linear-speed gate.");
        }

        [Test]
        public void ClipRecorder_JsonRoundTrip_ReplaysTrueAndFalseAccurately()
        {
            var recTrue = new BumpClipRecorder();
            var recFalse = new BumpClipRecorder();
            var headPos = new Vector3(0f, 1.2f, 0f);

            for (float t = 0f; t <= 1.1f; t += Dt)
            {
                float zBump = 0.22f + 0.24f * Mathf.Clamp01((t - 0.45f) / 0.11f);
                var rightBump = new BumpClipHand
                {
                    tracked = true,
                    wristPos = new Vector3(0.05f, 1.12f, zBump),
                    wristRot = Quaternion.identity,
                    curl = 215f,
                    indexCurl = 215f, middleCurl = 215f, ringCurl = 215f, pinkyCurl = 215f,
                    fist = true
                };
                recTrue.Record(t, headPos, Quaternion.identity, default, rightBump);

                float yaw = 180f * Mathf.Clamp(t - 0.4f, 0f, 0.4f);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                var rightFalse = rightBump;
                rightFalse.wristPos = headPos + rot * new Vector3(0.08f, -0.1f, 0.42f);
                recFalse.Record(t, headPos, rot, default, rightFalse);
            }

            string jsonTrue = JsonUtility.ToJson(recTrue.BuildClip("TRUE"));
            string jsonFalse = JsonUtility.ToJson(recFalse.BuildClip("FALSE"));

            BumpClip parsedTrue = JsonUtility.FromJson<BumpClip>(jsonTrue);
            BumpClip parsedFalse = JsonUtility.FromJson<BumpClip>(jsonFalse);
            Assert.AreEqual("TRUE", parsedTrue.label);
            Assert.AreEqual("FALSE", parsedFalse.label);
            Assert.Greater(BumpDetectorCore.ReplayClip(parsedTrue, _s), 0, "TRUE clip must replay as a bump.");
            Assert.AreEqual(0, BumpDetectorCore.ReplayClip(parsedFalse, _s), "FALSE clip must replay as 0 bumps.");
        }

        [Test]
        public void TestClipsFolder_ReplaysRecordedClipsMatchingLabels_OrSkipsIfEmpty()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestClips"));
            if (!Directory.Exists(dir))
                Assert.Ignore("TestClips/ directory does not exist; skipping on-device clip replay.");

            string[] files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
                Assert.Ignore("TestClips/ contains 0 JSON clips; skipping on-device clip replay.");

            foreach (string file in files)
            {
                BumpClip clip = JsonUtility.FromJson<BumpClip>(File.ReadAllText(file));
                Assert.IsNotNull(clip, "Unreadable clip JSON: " + file);
                if (string.Equals(clip.label, "INCOMPLETE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clip.label, "SKIP", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool expectedBump = string.Equals(clip.label, "TRUE", StringComparison.OrdinalIgnoreCase);
                bool actualBump = BumpDetectorCore.ReplayClip(clip, _s) > 0;
                Assert.AreEqual(expectedBump, actualBump, $"Clip {Path.GetFileName(file)} (label={clip.label}) replay mismatch.");
            }
        }

        [UnityTest]
        public IEnumerator BumpMain_DevHiddenUntilFiveVersionPokes_AndSavesClips()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            string tempClips = Path.Combine(Path.GetTempPath(), "BumpClipsTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("BumpMain");
                yield return null;
                yield return null;
                var ctrl = UnityEngine.Object.FindFirstObjectByType<BumpMainController>();
                Assert.IsNotNull(ctrl);

                Assert.IsFalse(ctrl.DevButtonVisible, "Dev button must be hidden for normal users at startup.");
                for (int i = 0; i < 4; i++) ctrl.PokeVersionText();
                Assert.IsFalse(ctrl.DevButtonVisible, "Dev button must stay hidden before 5 pokes.");
                ctrl.PokeVersionText();
                Assert.IsTrue(ctrl.DevButtonVisible, "Dev button must appear on the 5th poke of the version text.");

                string falsePath = ctrl.SaveBumpClip("FALSE", tempClips);
                string truePath = ctrl.SaveBumpClip("TRUE", tempClips);
                Assert.IsTrue(File.Exists(falsePath));
                Assert.IsTrue(File.Exists(truePath));
                Assert.AreEqual("FALSE", JsonUtility.FromJson<BumpClip>(File.ReadAllText(falsePath)).label);
                Assert.AreEqual("TRUE", JsonUtility.FromJson<BumpClip>(File.ReadAllText(truePath)).label);
            }
            finally
            {
                if (Directory.Exists(tempClips)) Directory.Delete(tempClips, true);
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }
    }
}


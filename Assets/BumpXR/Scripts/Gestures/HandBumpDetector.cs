using System;
using System.Globalization;
using System.IO;
using BumpXR.Core;
using Oculus.Interaction.Input;
using Oculus.Interaction.PoseDetection;
using UnityEngine;

namespace BumpXR.Gestures
{
    /// <summary>
    /// Detects a fist bump with either hand and records a 3 s ring buffer of head + dual-hand tracking frames for clip export.
    /// Fist = Interaction SDK finger curl (FingerShapes.GetCurlValue, degrees) on index/middle/ring/pinky;
    /// motion = wrist world-space pose (IHand.GetRootPose) gated against head pose in BumpDetectorCore.
    /// Docs: https://developers.meta.com/vr/documentation/unity/unity-isdk-hand-pose-detection
    ///       https://developers.meta.com/vr/reference/interaction/v207/interface_oculus_interaction_input_i_hand
    /// </summary>
    public sealed class HandBumpDetector : MonoBehaviour, IBumpDetector
    {
        private static readonly HandFinger[] FistFingers = { HandFinger.Index, HandFinger.Middle, HandFinger.Ring, HandFinger.Pinky };

        private BumpSettings _settings;
        private BumpDetectorCore _core;
        private readonly BumpClipRecorder _recorder = new BumpClipRecorder();
        private readonly FingerShapes _shapes = new FingerShapes();
        private Hand[] _hands = Array.Empty<Hand>();
        private float _nextHandScan;

        public event Action<BumpEvent> Bumped;
        /// <summary>Reject reason of the latest failed bump candidate (see BumpDetectorCore.Rejected).</summary>
        public event Action<string> Rejected;

        /// <summary>Fist targets making the direction gate target-aware (set by the Demo Buddies).</summary>
        public IFistTargets Targets
        {
            get => _core?.Targets;
            set { if (_core != null) _core.Targets = value; }
        }

        public string StateLabel => _core != null ? _core.State : "Not started";
        public float LeftSpeed => _core?.LeftSpeed ?? 0f;
        public float RightSpeed => _core?.RightSpeed ?? 0f;
        public float CooldownLeft => _core?.CooldownLeft ?? 0f;
        public int TrackedHandCount { get; private set; }
        public string LastDebugLine => _core?.LastDebugLine ?? "";
        public int ClipSampleCount => _recorder.Count;

        public void Init(BumpSettings settings)
        {
            _settings = settings;
            _core = new BumpDetectorCore(settings);
            _core.Bumped += e => Bumped?.Invoke(e);
            _core.Rejected += r => Rejected?.Invoke(r);
        }

        /// <summary>Dev overlay / Editor / test path: raise a bump at an explicit world point.</summary>
        public void SimulateBumpAt(Vector3 point) => _core?.ForceBump(Time.unscaledTime, point);

        /// <summary>Dev overlay / Editor path: raise a bump at a point 0.4 m in front of the head.</summary>
        public void SimulateBump()
        {
            Transform head = Camera.main != null ? Camera.main.transform : null;
            Vector3 p = head != null ? head.position + head.forward * 0.4f : new Vector3(0f, 1.2f, 0.4f);
            _core?.ForceBump(Time.unscaledTime, p);
        }

        /// <summary>Write the last 3 s ring buffer to Application.persistentDataPath/BumpClips/ and return the saved file path.</summary>
        public string SaveClip(string label, string directoryOverride = null)
        {
            string safeLabel = string.Equals(label, "TRUE", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
            string dir = string.IsNullOrEmpty(directoryOverride)
                ? Path.Combine(Application.persistentDataPath, "BumpClips")
                : directoryOverride;
            Directory.CreateDirectory(dir);
            BumpClip clip = _recorder.BuildClip(safeLabel);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, $"bump_{safeLabel}_{stamp}.json");
            File.WriteAllText(path, JsonUtility.ToJson(clip, true));
            Debug.Log($"[BumpXR] Saved bump clip ({safeLabel}, {clip.samples.Count} samples): {path}");
            return path;
        }

        private void LateUpdate()
        {
            if (_core == null) return;

            if (Time.unscaledTime >= _nextHandScan)
            {
                _nextHandScan = Time.unscaledTime + 1f; // hands can be (re)created by the rig; rescan cheaply
                _hands = FindObjectsByType<Hand>(FindObjectsSortMode.None);
            }

            Transform headTf = Camera.main != null ? Camera.main.transform : null;
            Vector3 headPos = headTf != null ? headTf.position : Vector3.zero;
            Quaternion headRot = headTf != null ? headTf.rotation : Quaternion.identity;

            int tracked = 0;
            BumpClipHand leftClip = default;
            BumpClipHand rightClip = default;
            float now = Time.unscaledTime;

            foreach (Hand hand in _hands)
            {
                if (hand == null) continue;
                Pose wrist = default;
                bool ok = hand.IsConnected && hand.IsTrackedDataValid && hand.GetRootPose(out wrist);
                if (ok) tracked++;
                bool isLeft = hand.Handedness == Handedness.Left;
                BumpClipHand sample = SampleHand(hand, ok, wrist);
                if (isLeft) leftClip = sample; else rightClip = sample;

                _core.Update(hand.GetInstanceID(), isLeft, now, wrist.position, ok, sample.fist, headPos, headRot);
            }

            _recorder.Record(now, headPos, headRot, leftClip, rightClip);
            TrackedHandCount = tracked;
        }

        private BumpClipHand SampleHand(IHand hand, bool ok, Pose wrist)
        {
            if (!ok) return new BumpClipHand { tracked = false, wristRot = Quaternion.identity };
            float ic = _shapes.GetCurlValue(FistFingers[0], hand);
            float mc = _shapes.GetCurlValue(FistFingers[1], hand);
            float rc = _shapes.GetCurlValue(FistFingers[2], hand);
            float pc = _shapes.GetCurlValue(FistFingers[3], hand);

            int curled = 0;
            if (ic >= _settings.fistCurlDegrees) curled++;
            if (mc >= _settings.fistCurlDegrees) curled++;
            if (rc >= _settings.fistCurlDegrees) curled++;
            if (pc >= _settings.fistCurlDegrees) curled++;

            return new BumpClipHand
            {
                tracked = true,
                wristPos = wrist.position,
                wristRot = wrist.rotation,
                curl = (ic + mc + rc + pc) * 0.25f,
                indexCurl = ic,
                middleCurl = mc,
                ringCurl = rc,
                pinkyCurl = pc,
                fist = curled >= _settings.minCurledFingers
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BumpXR.Core
{
    [Serializable]
    public struct BumpClipHand
    {
        public bool tracked;
        public Vector3 wristPos;
        public Quaternion wristRot;
        public float curl;
        public float indexCurl;
        public float middleCurl;
        public float ringCurl;
        public float pinkyCurl;
        public bool fist;
    }

    [Serializable]
    public struct BumpClipSample
    {
        public float t;
        public Vector3 headPos;
        public Quaternion headRot;
        public BumpClipHand left;
        public BumpClipHand right;
    }

    [Serializable]
    public sealed class BumpClip
    {
        public string label;
        public string timestamp;
        public List<BumpClipSample> samples = new List<BumpClipSample>();
    }

    /// <summary>Ring buffer of the last 3 s of head + dual-hand pose/curl/tracking samples for on-device clip export and offline replay.</summary>
    public sealed class BumpClipRecorder
    {
        public const float WindowSeconds = 3.0f;
        private readonly List<BumpClipSample> _ring = new List<BumpClipSample>(300);

        public int Count => _ring.Count;

        public void Record(float time, Vector3 headPos, Quaternion headRot, BumpClipHand left, BumpClipHand right)
        {
            if (_ring.Count > 0 && time < _ring[_ring.Count - 1].t) _ring.Clear();
            _ring.Add(new BumpClipSample
            {
                t = time,
                headPos = headPos,
                headRot = headRot,
                left = left,
                right = right
            });
            while (_ring.Count > 1 && time - _ring[0].t > WindowSeconds) _ring.RemoveAt(0);
        }

        public BumpClip BuildClip(string label)
        {
            var clip = new BumpClip
            {
                label = label,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                samples = new List<BumpClipSample>(_ring.Count)
            };
            float t0 = _ring.Count > 0 ? _ring[0].t : 0f;
            for (int i = 0; i < _ring.Count; i++)
            {
                BumpClipSample s = _ring[i];
                s.t -= t0;
                clip.samples.Add(s);
            }
            return clip;
        }
    }

    /// <summary>
    /// Pure bump logic (no SDK types) so it can be driven by real hands, the Demo Friend orb, recorded clips, and synthetic test trajectories.
    /// Enforces approach/deceleration + 5 false-bump gates:
    /// (a) head-motion gate, (b) continuous tracking + frame-jump gate, (c) frozen-forward direction + travel gate,
    /// (d) reach zone in front of head, and (e) continuous fist hold gate.
    /// </summary>
    public sealed class BumpDetectorCore
    {
        private struct Sample
        {
            public float Time, Forward, Speed;
            public bool Fist;
        }

        private struct HeadSample
        {
            public float Time, LinearSpeed, AngularSpeed;
        }

        private sealed class Track
        {
            public bool HasPrev;
            public bool Tracked;
            public float PrevTime;
            public Vector3 PrevPos;
            public float TrackedSince;
            public float FistSince = float.PositiveInfinity;
            public float Forward, Speed;
            public bool InApproach;
            public float ApproachStartTime;
            public Vector3 ApproachStartPos;
            public Vector3 ApproachForward;
            public bool CandidateLogged;
            public string State = "No hands";
            public readonly List<Sample> History = new List<Sample>(32);

            public void Reset()
            {
                HasPrev = false;
                Tracked = false;
                TrackedSince = 0f;
                FistSince = float.PositiveInfinity;
                Forward = Speed = 0f;
                InApproach = false;
                CandidateLogged = false;
                State = "No hands";
                History.Clear();
            }
        }

        private readonly BumpSettings _s;
        private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        private readonly List<HeadSample> _headHistory = new List<HeadSample>(48);
        private bool _hasPrevHead;
        private float _prevHeadTime;
        private Vector3 _prevHeadPos;
        private Quaternion _prevHeadRot = Quaternion.identity;
        private float _cooldownUntil = float.NegativeInfinity;
        private float _now;

        public event Action<BumpEvent> Bumped;
        /// <summary>Raised with the reject reason (open_hand, head_motion, off_axis, ...) whenever a candidate bump is rejected.</summary>
        public event Action<string> Rejected;

        /// <summary>Fist targets that make the direction gate target-aware (null = classic head-forward rule).</summary>
        public IFistTargets Targets { get; set; }

        public float LeftSpeed { get; private set; }
        public float RightSpeed { get; private set; }
        public string State { get; private set; } = "No hands";
        public float CooldownLeft => Mathf.Max(0f, _cooldownUntil - _now);
        public string LastDebugLine { get; private set; } = "";
        public string LastRejectReason { get; private set; } = "";
        public bool LogCandidates = true;

        public BumpDetectorCore(BumpSettings settings) => _s = settings;

        /// <summary>Full head-pose-aware update used by real hands, recorded clips, and head-motion synthetic tests.</summary>
        public bool Update(int handId, bool isLeft, float time, Vector3 wristPos, bool tracked, bool fist, Vector3 headPos, Quaternion headRot)
        {
            Vector3 flatFwd = FlatForward(headRot * Vector3.forward);
            return UpdateInternal(handId, isLeft, time, wristPos, tracked, fist, flatFwd, true, headPos, headRot);
        }

        /// <summary>Forward-vector overload (DemoFriendOrb and legacy head-free unit trajectories).</summary>
        public bool Update(int handId, bool isLeft, float time, Vector3 position, bool tracked, bool fist, Vector3 forward)
        {
            return UpdateInternal(handId, isLeft, time, position, tracked, fist, FlatForward(forward), false, Vector3.zero, Quaternion.identity);
        }

        private bool UpdateInternal(int handId, bool isLeft, float time, Vector3 position, bool tracked, bool fist,
            Vector3 forward, bool hasHeadPose, Vector3 headPos, Quaternion headRot)
        {
            _now = time;
            if (hasHeadPose) RecordHead(time, headPos, headRot);

            if (!_tracks.TryGetValue(handId, out Track t)) _tracks[handId] = t = new Track();

            if (!tracked)
            {
                t.Reset();
                SetSpeed(isLeft, 0f);
                RefreshSummaryState(time);
                return false;
            }

            t.Tracked = true;
            float dt = time - t.PrevTime;
            bool valid = t.HasPrev && dt > 1e-4f && dt < 0.25f;
            if (!valid)
            {
                t.HasPrev = true;
                t.PrevTime = time;
                t.PrevPos = position;
                t.TrackedSince = time;
                t.FistSince = fist ? time : float.PositiveInfinity;
                t.Forward = t.Speed = 0f;
                t.InApproach = false;
                t.CandidateLogged = false;
                t.State = fist ? "Fist" : "Open hand";
                t.History.Clear();
                SetSpeed(isLeft, 0f);
                RefreshSummaryState(time);
                return false;
            }

            Vector3 prevPos = t.PrevPos;
            float prevTime = t.PrevTime;
            float stepDist = (position - prevPos).magnitude;
            t.PrevTime = time;
            t.PrevPos = position;

            // Gate (b) part 2: single-frame wrist jump > maxFrameJumpMeters resets approach and continuous-tracking clock.
            if (stepDist > _s.maxFrameJumpMeters)
            {
                EmitDebug(false, "wrist_jump", isLeft, stepDist / dt, stepDist, hasHeadPose ? (position - headPos).magnitude : 0f, 0f, 0f);
                t.TrackedSince = time;
                t.FistSince = fist ? time : float.PositiveInfinity;
                t.Forward = t.Speed = 0f;
                t.InApproach = false;
                t.CandidateLogged = false;
                t.History.Clear();
                t.State = fist ? "Fist" : "Open hand";
                SetSpeed(isLeft, 0f);
                RefreshSummaryState(time);
                return false;
            }

            if (fist)
            {
                if (float.IsPositiveInfinity(t.FistSince)) t.FistSince = time;
            }
            else
            {
                t.FistSince = float.PositiveInfinity;
            }

            Vector3 vel = (position - prevPos) / dt;
            float instForward = Vector3.Dot(vel, forward);

            // Freeze forward axis when forward motion starts from rest; reset when motion settles without peaking.
            if (!t.InApproach && instForward > 0.15f)
            {
                t.InApproach = true;
                t.ApproachStartTime = prevTime;
                t.ApproachStartPos = prevPos;
                t.ApproachForward = forward;
                t.CandidateLogged = false;
            }

            Vector3 axis = t.InApproach ? t.ApproachForward : forward;
            float rawForward = Vector3.Dot(vel, axis);
            t.Forward = Mathf.Lerp(t.Forward, rawForward, _s.speedSmoothing);
            t.Speed = Mathf.Lerp(t.Speed, vel.magnitude, _s.speedSmoothing);
            SetSpeed(isLeft, t.Forward);

            t.History.Add(new Sample { Time = time, Forward = t.Forward, Speed = t.Speed, Fist = fist });
            while (t.History.Count > 0 && time - t.History[0].Time > _s.decelWindow + 0.05f) t.History.RemoveAt(0);

            // Peak forward speed inside deceleration window (regardless of fist so candidate rejections can log cleanly).
            Sample peak = default;
            bool havePeak = false;
            foreach (Sample s in t.History)
            {
                if (time - s.Time > _s.decelWindow) continue;
                if (!havePeak || s.Forward > peak.Forward) { peak = s; havePeak = true; }
            }

            if (t.InApproach && (!havePeak || peak.Forward < _s.speedThreshold) &&
                ((rawForward <= 0.05f && t.Forward < 0.15f && time - t.ApproachStartTime > 0.08f) || time - t.ApproachStartTime > 1.0f))
            {
                t.InApproach = false;
                t.CandidateLogged = false;
            }

            if (time < _cooldownUntil)
            {
                t.State = "Cooldown";
                RefreshSummaryState(time);
                return false;
            }

            t.State = !fist ? "Open hand" : t.Forward >= _s.speedThreshold ? "Approaching" : "Fist";
            RefreshSummaryState(time);

            // Check whether a high-speed forward peak just decelerated into a stop (a candidate bump).
            if (!havePeak || peak.Forward < _s.speedThreshold) return false;
            if (t.Forward > peak.Forward * _s.decelRatio) return false;

            Vector3 disp = t.InApproach ? (position - t.ApproachStartPos) : Vector3.zero;
            float travel = disp.magnitude;
            float headDist = hasHeadPose ? (position - headPos).magnitude : 0f;
            MaxHeadMotion(time, out float maxHeadLin, out float maxHeadAng);

            if (!fist || !peak.Fist)
                return RejectCandidate(t, "open_hand", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            if (peak.Speed <= 0f || peak.Forward / peak.Speed < _s.minForwardFraction)
                return RejectCandidate(t, "lateral_speed", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            if (t.Forward < -peak.Forward * _s.reboundLimit || rawForward < -peak.Forward * _s.reboundLimit)
                return RejectCandidate(t, "retract", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            // Gate (a): Head-motion gate (linear & angular speed in the headMotionWindow before stop).
            if (hasHeadPose && (maxHeadLin > _s.maxHeadLinearSpeed || maxHeadAng > _s.maxHeadAngularSpeed))
                return RejectCandidate(t, "head_motion", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            // Gate (b) part 1: Hand continuously tracked for >= minTrackedDuration.
            if (time - t.TrackedSince < _s.minTrackedDuration - 1e-4f)
                return RejectCandidate(t, "tracking_short", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            // Gate (c): Frozen forward direction within maxApproachAngleDegrees and net travel >= minApproachTravel.
            if (!t.InApproach || travel < _s.minApproachTravel)
                return RejectCandidate(t, "short_travel", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            float angle = Vector3.Angle(disp, GateAxis(t, position, disp));
            if (angle > _s.maxApproachAngleDegrees)
                return RejectCandidate(t, "off_axis", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            // Gate (d): Reach zone (0.25-0.65 m from head and in front of it).
            if (hasHeadPose)
            {
                Vector3 fromHead = position - headPos;
                Vector3 headFlatFwd = FlatForward(headRot * Vector3.forward);
                if (headDist < _s.minReachDistance || headDist > _s.maxReachDistance || Vector3.Dot(fromHead, headFlatFwd) <= 0.05f)
                    return RejectCandidate(t, "reach_zone", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);
            }

            // Gate (e): Fist continuously held for >= minFistHoldDuration before approach ends.
            if (time - t.FistSince < _s.minFistHoldDuration - 1e-4f)
                return RejectCandidate(t, "fist_hold", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);

            EmitDebug(true, "accepted", isLeft, peak.Forward, travel, headDist, maxHeadLin, maxHeadAng);
            Fire(new BumpEvent { Position = position, PeakSpeed = peak.Forward, IsLeft = isLeft, Time = time, Direction = disp.sqrMagnitude > 1e-6f ? disp.normalized : Vector3.zero });
            return true;
        }

        /// <summary>
        /// Phase 5B target-aware direction rule: when fist targets exist, the approach direction is judged against the vector from the
        /// approach start toward the nearest target fist (nearest to where the fist stopped); otherwise the frozen head-forward axis is used.
        /// </summary>
        private Vector3 GateAxis(Track t, Vector3 position, Vector3 disp)
        {
            IFistTargets targets = Targets;
            if (targets == null) return t.ApproachForward;
            Vector3 contact = disp.sqrMagnitude > 1e-6f ? position + disp.normalized * FistTargeting.WristToKnuckles : position;
            int index = FistTargeting.Nearest(targets, contact);
            if (index < 0) return t.ApproachForward;
            Vector3 toTarget = targets.Position(index) - t.ApproachStartPos;
            return toTarget.sqrMagnitude > 1e-4f ? toTarget : t.ApproachForward;
        }

        private bool RejectCandidate(Track t, string reason, bool isLeft, float peak, float travel, float dist, float headLin, float headAng)
        {
            if (!t.CandidateLogged)
            {
                t.CandidateLogged = true;
                EmitDebug(false, reason, isLeft, peak, travel, dist, headLin, headAng);
            }
            return false;
        }

        private void EmitDebug(bool accepted, string reason, bool isLeft, float peak, float travel, float dist, float headLin, float headAng)
        {
            LastRejectReason = accepted ? "" : reason;
            LastDebugLine = string.Format(
                CultureInfo.InvariantCulture,
                "BUMPDBG {0} reason={1} hand={2} peak={3:0.00} travel={4:0.00} dist={5:0.00} headLin={6:0.00} headAng={7:0.0}",
                accepted ? "ACCEPT" : "REJECT", reason, isLeft ? "L" : "R", peak, travel, dist, headLin, headAng);
            if (LogCandidates) Debug.Log(LastDebugLine);
            if (!accepted) Rejected?.Invoke(reason);
        }

        private void RecordHead(float time, Vector3 headPos, Quaternion headRot)
        {
            if (!_hasPrevHead || time < _prevHeadTime)
            {
                _hasPrevHead = true;
                _prevHeadTime = time;
                _prevHeadPos = headPos;
                _prevHeadRot = headRot;
                _headHistory.Clear();
                return;
            }

            float dt = time - _prevHeadTime;
            if (dt <= 1e-4f) return; // second hand on the same frame

            if (dt < 0.25f)
            {
                float lin = (headPos - _prevHeadPos).magnitude / dt;
                float ang = Quaternion.Angle(_prevHeadRot, headRot) / dt;
                _headHistory.Add(new HeadSample { Time = time, LinearSpeed = lin, AngularSpeed = ang });
            }
            else
            {
                _headHistory.Clear();
            }

            _prevHeadTime = time;
            _prevHeadPos = headPos;
            _prevHeadRot = headRot;
            while (_headHistory.Count > 0 && time - _headHistory[0].Time > _s.headMotionWindow + 0.05f)
                _headHistory.RemoveAt(0);
        }

        private void MaxHeadMotion(float time, out float maxLin, out float maxAng)
        {
            maxLin = 0f;
            maxAng = 0f;
            for (int i = 0; i < _headHistory.Count; i++)
            {
                HeadSample h = _headHistory[i];
                if (time - h.Time > _s.headMotionWindow) continue;
                if (h.LinearSpeed > maxLin) maxLin = h.LinearSpeed;
                if (h.AngularSpeed > maxAng) maxAng = h.AngularSpeed;
            }
        }

        private static Vector3 FlatForward(Vector3 fwd)
        {
            Vector3 f = new Vector3(fwd.x, 0f, fwd.z);
            if (f.sqrMagnitude > 1e-4f) return f.normalized;
            return fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        }

        /// <summary>Replay a recorded BumpClip through a fresh BumpDetectorCore and return the number of bumps detected.</summary>
        public static int ReplayClip(BumpClip clip, BumpSettings settings)
        {
            if (clip == null || clip.samples == null || clip.samples.Count == 0) return 0;
            var core = new BumpDetectorCore(settings) { LogCandidates = false };
            int bumps = 0;
            core.Bumped += _ => bumps++;
            for (int i = 0; i < clip.samples.Count; i++)
            {
                BumpClipSample s = clip.samples[i];
                bool lf = EvalClipFist(s.left, settings);
                bool rf = EvalClipFist(s.right, settings);
                core.Update(1, true, s.t, s.left.wristPos, s.left.tracked, lf, s.headPos, s.headRot);
                core.Update(2, false, s.t, s.right.wristPos, s.right.tracked, rf, s.headPos, s.headRot);
            }
            return bumps;
        }

        private static bool EvalClipFist(BumpClipHand h, BumpSettings s)
        {
            if (!h.tracked) return false;
            bool hasFingerCurls = h.indexCurl > 0f || h.middleCurl > 0f || h.ringCurl > 0f || h.pinkyCurl > 0f;
            if (hasFingerCurls)
            {
                int n = 0;
                if (h.indexCurl >= s.fistCurlDegrees) n++;
                if (h.middleCurl >= s.fistCurlDegrees) n++;
                if (h.ringCurl >= s.fistCurlDegrees) n++;
                if (h.pinkyCurl >= s.fistCurlDegrees) n++;
                return n >= s.minCurledFingers;
            }
            if (h.curl > 0f) return h.curl >= s.fistCurlDegrees;
            return h.fist;
        }

        /// <summary>Raise a bump from outside the gesture (Dev overlay / Editor). Still honours the cooldown.</summary>
        public bool ForceBump(float time, Vector3 position, float peakSpeed = 1.5f)
        {
            _now = time;
            if (time < _cooldownUntil) return false;
            Fire(new BumpEvent { Position = position, PeakSpeed = peakSpeed, IsLeft = false, Time = time });
            return true;
        }

        private void Fire(BumpEvent e)
        {
            _cooldownUntil = e.Time + _s.cooldown;
            foreach (Track t in _tracks.Values)
            {
                t.History.Clear();
                t.InApproach = false;
                t.CandidateLogged = false;
                if (t.Tracked) t.State = "BUMP";
            }
            State = "BUMP";
            Bumped?.Invoke(e);
        }

        private void RefreshSummaryState(float time)
        {
            if (time < _cooldownUntil) { State = "Cooldown"; return; }
            string best = "No hands";
            int bestRank = 0;
            foreach (Track t in _tracks.Values)
            {
                if (!t.Tracked) continue;
                int rank = t.State == "Approaching" ? 3 : t.State == "Fist" ? 2 : 1;
                if (rank > bestRank) { bestRank = rank; best = t.State; }
            }
            State = best;
        }

        private void SetSpeed(bool isLeft, float v)
        {
            if (isLeft) LeftSpeed = v; else RightSpeed = v;
        }
    }
}

using System.Globalization;
using UnityEngine;

namespace BumpXR.Core
{
    /// <summary>All tunable bump-gesture constants. Loaded from Resources/BumpSettings; defaults below are the starting point for on-device tuning.</summary>
    [CreateAssetMenu(fileName = "BumpSettings", menuName = "BumpXR/Bump Settings")]
    public sealed class BumpSettings : ScriptableObject
    {
        public const string PrefSpeedKey = "BumpXR.Tune.Speed";
        public const string PrefCurlKey = "BumpXR.Tune.Curl";
        public const string PrefDecelKey = "BumpXR.Tune.Decel";
        [Header("Approach")]
        [Tooltip("Minimum smoothed speed along head-forward (m/s) before a stop counts as a bump.")]
        public float speedThreshold = 1.90f;
        [Tooltip("Share of the hand's speed that must point along head-forward (rejects sideways waves).")]
        [Range(0f, 1f)] public float minForwardFraction = 0.6f;
        [Tooltip("EMA factor for velocity smoothing (1 = raw, lower = smoother).")]
        [Range(0.1f, 1f)] public float speedSmoothing = 0.6f;

        [Header("Deceleration")]
        [Tooltip("The speed peak must lie within this many seconds of the stop.")]
        public float decelWindow = 0.15f;
        [Tooltip("Current forward speed must be at most this fraction of the peak.")]
        [Range(0.05f, 0.9f)] public float decelRatio = 0.25f;
        [Tooltip("Reject pulling straight back: current speed must stay above -peak * this.")]
        [Range(0f, 1f)] public float reboundLimit = 0.5f;

        [Header("Fist")]
        [Tooltip("Finger curl (degrees, Interaction SDK FingerShapes.GetCurlValue). Closed fingers read about 200-210+.")]
        public float fistCurlDegrees = 200f;
        [Tooltip("How many of index/middle/ring/pinky must be at least fistCurlDegrees.")]
        [Range(1, 4)] public int minCurledFingers = 3;
        [Tooltip("Fist must remain continuously closed for at least this many seconds before approach ends.")]
        public float minFistHoldDuration = 0.15f;

        [Header("False-Bump Gates")]
        [Tooltip("Lookback window (s) before stop in which excessive head motion suppresses a bump candidate.")]
        public float headMotionWindow = 0.25f;
        [Tooltip("Maximum head linear speed (m/s) allowed inside headMotionWindow before a stop.")]
        public float maxHeadLinearSpeed = 0.35f;
        [Tooltip("Maximum head angular speed (deg/s) allowed inside headMotionWindow before a stop.")]
        public float maxHeadAngularSpeed = 65f;
        [Tooltip("Minimum continuous hand tracking duration (s) required before a bump can fire.")]
        public float minTrackedDuration = 0.35f;
        [Tooltip("Reject and reset tracking history on any single-frame wrist jump above this distance (m).")]
        public float maxFrameJumpMeters = 0.25f;
        [Tooltip("Maximum angle (degrees) between net approach displacement and the forward axis frozen at approach start.")]
        public float maxApproachAngleDegrees = 25f;
        [Tooltip("Minimum net wrist displacement (m) from approach start to stop.")]
        public float minApproachTravel = 0.20f;
        [Tooltip("Minimum wrist distance from head (m) at the stop.")]
        public float minReachDistance = 0.25f;
        [Tooltip("Maximum wrist distance from head (m) at the stop.")]
        public float maxReachDistance = 0.65f;

        [Header("Timing")]
        public float cooldown = 1.5f;
        [Tooltip("A bump up to this many seconds BEFORE an incoming card appears (Share panel open) accepts it.")]
        public float simultaneousBumpWindow = 1.0f;

        /// <summary>True when a bump made with the Share panel open counts for a card that just appeared.</summary>
        public static bool IsSimultaneous(float bumpTime, bool shareOpenAtBump, float cardTime, float window) =>
            shareOpenAtBump && cardTime >= bumpTime && cardTime - bumpTime <= window;

        /// <summary>Load persisted runtime tuning overrides from PlayerPrefs, if present.</summary>
        public void LoadOverrides()
        {
            if (PlayerPrefs.HasKey(PrefSpeedKey)) speedThreshold = PlayerPrefs.GetFloat(PrefSpeedKey, speedThreshold);
            if (PlayerPrefs.HasKey(PrefCurlKey)) fistCurlDegrees = PlayerPrefs.GetFloat(PrefCurlKey, fistCurlDegrees);
            if (PlayerPrefs.HasKey(PrefDecelKey)) decelRatio = PlayerPrefs.GetFloat(PrefDecelKey, decelRatio);
        }

        /// <summary>Persist speedThreshold, fistCurlDegrees, and decelRatio to PlayerPrefs.</summary>
        public void SaveOverrides()
        {
            PlayerPrefs.SetFloat(PrefSpeedKey, speedThreshold);
            PlayerPrefs.SetFloat(PrefCurlKey, fistCurlDegrees);
            PlayerPrefs.SetFloat(PrefDecelKey, decelRatio);
            PlayerPrefs.Save();
        }

        /// <summary>Clear persisted overrides and restore defaults from defaultsAsset (or fresh BumpSettings defaults).</summary>
        public void ResetToDefaults(BumpSettings defaultsAsset = null)
        {
            PlayerPrefs.DeleteKey(PrefSpeedKey);
            PlayerPrefs.DeleteKey(PrefCurlKey);
            PlayerPrefs.DeleteKey(PrefDecelKey);
            PlayerPrefs.Save();
            BumpSettings src = defaultsAsset != null ? defaultsAsset : CreateInstance<BumpSettings>();
            speedThreshold = src.speedThreshold;
            fistCurlDegrees = src.fistCurlDegrees;
            decelRatio = src.decelRatio;
            if (defaultsAsset == null) DestroyImmediate(src);
        }

        /// <summary>Single-line tuning summary readable via `metavr logs`.</summary>
        public string FormatTuneLine() => string.Format(
            CultureInfo.InvariantCulture,
            "BUMPTUNE speed={0:0.00} curl={1:0} decel={2:0.00}",
            speedThreshold, fistCurlDegrees, decelRatio);
    }
}

